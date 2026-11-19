#include "Bluetti.h"

#include <BLEDevice.h>
#include <algorithm>

#if defined(CONFIG_NIMBLE_ENABLED)
#include <host/ble_att.h>
#include <host/ble_gap.h>
#include <host/ble_hs_mbuf.h>
#endif

// Protocol from github.com/warhammerkid/bluetti_mqtt and github.com/Patrick762/bluetti-bt-lib:
// Modbus RTU frames (slave address 1, CRC-16/MODBUS, low byte first) written to FF02 and answered on FF01.
static const char* const ServiceUuid = "0000ff00-0000-1000-8000-00805f9b34fb";
static const char* const NotifyUuid = "0000ff01-0000-1000-8000-00805f9b34fb";
static const char* const WriteUuid = "0000ff02-0000-1000-8000-00805f9b34fb";

static const uint8_t SlaveAddress = 1;
static const uint8_t ReadHoldingRegisters = 3;
static const uint8_t WriteSingleRegister = 6;
static const uint8_t ExceptionFlag = 0x80;

static const uint32_t ScanSeconds = 5;
static const uint32_t ConnectTimeoutMs = 10000;
static const uint32_t ReconnectDelayMs = 10000;
// The encrypted firmware sends its challenge right after the notifications are enabled.
static const uint32_t ChallengeWaitMs = 2000;
static const uint32_t KeyExchangeTimeoutMs = 20000;
static const uint32_t ResponseTimeoutMs = 5000;
static const uint32_t WriteTimeoutMs = 15000;
static const uint8_t MaxPollFailures = 3;
static const uint32_t StopCheckIntervalMs = 200;

// Advertised names are the model, followed by the serial number on most models, e.g. "AC3002235000574321" or "AP300".
static const char* const Models[] = {
  "AC2A", "AC2P", "AC50B", "AC60", "AC60P", "AC70", "AC70P", "AC180", "AC180P", "AC180T", "AC200L", "AC200M", "AC200PL",
  "AC300", "AC500", "AP300", "EB3A", "EL10", "EL30V2", "EL100V2", "EP500", "EP500P", "EP600", "EP760", "EP800", "EP2000",
  "PR30V2", "PR100V2"
};

// What the protocol 2 models report besides the battery, from the device definitions of bluetti-bt-lib.
// Every protocol 1 model has switchable AC and DC outputs.
struct ModelFeatures {
  const char* model;
  bool hasAcSwitch;
  bool hasDcSwitch;
  // Register 154.
  bool hasPowerGeneration;
  // Registers 140 and 146. Every model except the EP series has the AC output and DC input power in 142 and 144.
  bool hasDcOutputAndAcInputPower;
  // Register 104.
  bool hasTimeRemaining;
  // Registers 1212 to 1214 and 1220 to 1222.
  bool hasSolarInputs;
  // Registers 1500 and 1511.
  bool hasAcOutputDetails;
};

static const ModelFeatures ProtocolTwoModels[] = {
  { "AC180", true, true, false, true, false, false, false },
  { "AC180P", true, true, false, true, false, false, false },
  { "AC2P", true, true, false, true, false, false, false },
  { "AC60", true, true, true, true, false, false, false },
  { "AC60P", true, true, false, true, false, false, false },
  { "AC70", true, true, false, true, false, false, false },
  { "AP300", false, false, false, false, true, true, true },
  { "EL30V2", true, true, false, true, false, false, false },
  { "EP600", true, false, true, false, false, false, false }
};

static ModelFeatures featuresOf(const String& model) {
  for (const ModelFeatures& features : ProtocolTwoModels) {
    if (model == features.model) {
      return features;
    }
  }

  return { "", false, false, false, true, false, false, false };
}

static bool isModel(const String& value) {
  for (const char* model : Models) {
    if (value == model) {
      return true;
    }
  }

  return false;
}

static bool isBluettiName(const String& name) {
  for (const char* model : Models) {
    size_t length = strlen(model);
    if (!name.startsWith(model)) {
      continue;
    }

    bool isSerialNumber = true;
    for (size_t i = length; i < name.length(); i++) {
      isSerialNumber = isSerialNumber && isDigit(name[i]);
    }

    if (isSerialNumber) {
      return true;
    }
  }

  return false;
}

static bool isKeyExchange(const std::vector<uint8_t>& notification) {
  return notification.size() >= 2 && notification[0] == '*' && notification[1] == '*';
}

static uint16_t modbusCrc(const uint8_t* data, size_t length) {
  uint16_t crc = 0xFFFF;
  for (size_t i = 0; i < length; i++) {
    crc ^= data[i];
    for (int bit = 0; bit < 8; bit++) {
      crc = (crc & 1) ? (crc >> 1) ^ 0xA001 : crc >> 1;
    }
  }

  return crc;
}

// Two ASCII characters per register. Protocol 2 stores them swapped.
static String readString(const std::vector<uint16_t>& registers, size_t start, size_t count, bool isSwapped) {
  String value;
  for (size_t i = start; i < start + count; i++) {
    char high = registers[i] >> 8;
    char low = registers[i] & 0xFF;
    char first = isSwapped ? low : high;
    char second = isSwapped ? high : low;
    if (first != 0) {
      value += first;
    }

    if (second != 0) {
      value += second;
    }
  }

  value.trim();
  return value;
}

// Four registers, least significant word first.
static String readSerialNumber(const std::vector<uint16_t>& registers, size_t start) {
  uint64_t value = 0;
  for (size_t i = 0; i < 4; i++) {
    value |= (uint64_t)registers[start + i] << (16 * i);
  }

  char text[21];
  snprintf(text, sizeof(text), "%llu", (unsigned long long)value);
  return text;
}

// Two registers, least significant word first, in hundredths.
static String readVersion(const std::vector<uint16_t>& registers, size_t start) {
  uint32_t value = registers[start] | ((uint32_t)registers[start + 1] << 16);
  return String(value / 100.0, 2);
}

static bool isPast(TickType_t deadline) {
  return (int32_t)(xTaskGetTickCount() - deadline) >= 0;
}

void BluettiClient::begin(const char* address, uint32_t pollIntervalMs, bool isPolling) {
  _address = address;
  _pollIntervalMs = pollIntervalMs;
  _isPolling = isPolling;
  _status.isPolling = isPolling;
  _writeRequests = xQueueCreate(1, sizeof(WriteRequest));
  _writeResults = xQueueCreate(1, sizeof(WriteResult));
  _notified = xSemaphoreCreateBinary();
  _pollingChanged = xSemaphoreCreateBinary();

  BLEDevice::init("");
  // The messages of the key exchange and the encrypted requests are longer than the 20 bytes of the default MTU.
  BLEDevice::setMTU(517);

#if defined(CONFIG_NIMBLE_ENABLED)
  // With NimBLE (ESP32-C3, C6, S3) the BLE library passes only the first 128 byte buffer of a notification to the callback
  // and drops the rest, so the notifications are read from the GAP events, which carry the whole buffer chain.
  static ble_gap_event_listener listener;
  ble_gap_event_listener_register(&listener, onGapEvent, this);
#endif

  // The key exchange needs the room for the elliptic curve operations.
  xTaskCreate(runTask, "bluetti", 16384, this, 1, nullptr);
}

BluettiStatus BluettiClient::status() {
  std::lock_guard<std::mutex> lock(_statusMutex);
  return _status;
}

void BluettiClient::setPolling(bool isPolling) {
  _isPolling = isPolling;
  {
    std::lock_guard<std::mutex> lock(_statusMutex);
    _status.isPolling = isPolling;
  }

  xSemaphoreGive(_pollingChanged);
}

bool BluettiClient::setOutput(BluettiOutput output, bool isOn, String& error) {
  BluettiStatus current = status();
  if (!current.isPolling) {
    error = "Polling is stopped, so the power station is not connected.";
    return false;
  }

  if (!current.isConnected) {
    error = "Not connected to the power station.";
    return false;
  }

  bool hasSwitch = output == BluettiOutput::Ac ? current.hasAcSwitch : current.hasDcSwitch;
  if (!hasSwitch) {
    error = "The " + current.model + " cannot switch its " + (output == BluettiOutput::Ac ? "AC" : "DC") + " output.";
    return false;
  }

  // A result that arrived after an earlier request timed out must not answer this one.
  xQueueReset(_writeResults);
  WriteRequest request{ output, isOn };
  if (xQueueSend(_writeRequests, &request, pdMS_TO_TICKS(WriteTimeoutMs)) != pdTRUE) {
    error = "The power station is busy.";
    return false;
  }

  WriteResult result;
  if (xQueueReceive(_writeResults, &result, pdMS_TO_TICKS(WriteTimeoutMs)) != pdTRUE) {
    error = "The power station did not confirm the change in time.";
    return false;
  }

  error = result.error;
  return result.isSuccess;
}

void BluettiClient::runTask(void* parameter) {
  static_cast<BluettiClient*>(parameter)->run();
}

void BluettiClient::run() {
  uint8_t pollFailures = 0;
  TickType_t lastPoll = 0;
  bool isPaused = false;

  for (;;) {
    if (!_isPolling) {
      // Once, because the disconnect completes asynchronously and the client still reports connected right after it.
      if (!isPaused) {
        pause();
        isPaused = true;
      }

      xSemaphoreTake(_pollingChanged, portMAX_DELAY);
      continue;
    }

    isPaused = false;

    if (_client == nullptr || !_client->isConnected()) {
      String error;
      if (!connect(error)) {
        Serial.printf("Bluetti: %s\n", error.c_str());
        setError(error);
        disconnect();
        vTaskDelay(pdMS_TO_TICKS(ReconnectDelayMs));
        continue;
      }

      pollFailures = 0;
      lastPoll = 0;
    }

    TickType_t pollInterval = pdMS_TO_TICKS(_pollIntervalMs);
    TickType_t elapsed = xTaskGetTickCount() - lastPoll;
    TickType_t wait = lastPoll == 0 || elapsed >= pollInterval ? 0 : pollInterval - elapsed;

    // Waits in short slices, so a stop of the polling takes effect right away.
    TickType_t waitEnd = xTaskGetTickCount() + wait;
    WriteRequest request;
    bool hasRequest = false;
    for (;;) {
      TickType_t remaining = isPast(waitEnd) ? 0 : waitEnd - xTaskGetTickCount();
      hasRequest = xQueueReceive(_writeRequests, &request, std::min(remaining, pdMS_TO_TICKS(StopCheckIntervalMs))) == pdTRUE;
      if (hasRequest || !_isPolling || remaining == 0) {
        break;
      }
    }

    if (!_isPolling) {
      continue;
    }

    if (hasRequest) {
      String error;
      WriteResult result{ write(request, error), {} };
      strlcpy(result.error, error.c_str(), sizeof(result.error));
      xQueueOverwrite(_writeResults, &result);
      if (!result.isSuccess) {
        continue;
      }
    }

    BluettiStatus next = status();
    String error;
    lastPoll = xTaskGetTickCount();
    if (poll(next, error)) {
      pollFailures = 0;
      next.error = "";
      updateStatus(next);
      continue;
    }

    Serial.printf("Bluetti: %s\n", error.c_str());
    if (++pollFailures >= MaxPollFailures) {
      disconnect();
    }

    setError(error);
  }
}

bool BluettiClient::connect(String& error) {
  BLEScan* scan = BLEDevice::getScan();
  scan->setActiveScan(true);
  BLEScanResults* results = scan->start(ScanSeconds, false);

  bool isFound = false;
  BLEAddress address(String("00:00:00:00:00:00"));
  uint8_t addressType = 0;
  String name;
  int rssi = 0;
  bool isConnectable = false;
  for (int i = 0; i < results->getCount() && !isFound; i++) {
    BLEAdvertisedDevice device = results->getDevice(i);
    name = device.haveName() ? device.getName() : String();
    isFound = strlen(_address) > 0 ? device.getAddress().toString().equalsIgnoreCase(_address) : isBluettiName(name);
    if (isFound) {
      address = device.getAddress();
      addressType = device.getAddressType();
      rssi = device.getRSSI();
      // The type is that of the first packet received, which can be the scan response (4) with a weak signal,
      // so only the scannable (2) and non connectable (3) advertisements mean the power station refuses connections.
      isConnectable = device.getAdvType() != 2 && device.getAdvType() != 3;
    }
  }

  scan->clearResults();
  if (!isFound) {
    error = strlen(_address) > 0 ? "Power station " + String(_address) + " not found." : String("No Bluetti power station found.");
    return false;
  }

  // A weak signal or a power station that does not accept connections fails the connection. The AP300 refuses them for
  // about 2 minutes after a disconnect, and while it is connected to another device, such as the Bluetti app.
  String signal = "signal " + String(rssi) + " dBm"
                  + (isConnectable ? "" : ", not accepting connections yet, which lasts about 2 minutes after a disconnect, "
                                          "or longer while the Bluetti app is connected");
  Serial.printf("Bluetti: connecting to %s (%s, %s).\n", name.c_str(), address.toString().c_str(), signal.c_str());
  if (_client == nullptr) {
    _client = BLEDevice::createClient();
  }

  if (!_client->connect(address, addressType, ConnectTimeoutMs)) {
    error = "Could not connect to " + name + " (" + signal + ").";
    return false;
  }

  BLERemoteService* service = _client->getService(BLEUUID(ServiceUuid));
  BLERemoteCharacteristic* notifyCharacteristic = service ? service->getCharacteristic(BLEUUID(NotifyUuid)) : nullptr;
  _writeCharacteristic = service ? service->getCharacteristic(BLEUUID(WriteUuid)) : nullptr;
  if (notifyCharacteristic == nullptr || _writeCharacteristic == nullptr || !notifyCharacteristic->canNotify()) {
    error = name + " does not have the Bluetti service.";
    return false;
  }

  _encryption.reset();
  {
    std::lock_guard<std::mutex> lock(_notificationMutex);
    _notifications.clear();
  }

#if defined(CONFIG_NIMBLE_ENABLED)
  // The notifications arrive through onGapEvent, the callback only enables them.
  _connectionHandle = _client->getConnId();
  _notifyHandle = notifyCharacteristic->getHandle();
  notifyCharacteristic->registerForNotify([](BLERemoteCharacteristic*, uint8_t*, size_t, bool) {});
#else
  notifyCharacteristic->registerForNotify([this](BLERemoteCharacteristic*, uint8_t* data, size_t length, bool) {
    onNotify(data, length);
  });
#endif

  Serial.printf("Bluetti: MTU %u.\n", _client->getMTU());
  if (!exchangeKeys(error)) {
    return false;
  }

  BluettiStatus next;
  next.isEncrypted = _encryption.isActive();
  if (!identify(next, error)) {
    return false;
  }

  next.isConnected = true;
  updateStatus(next);
  Serial.printf("Bluetti: connected to %s %s, protocol %u%s.\n", next.model.c_str(), next.serialNumber.c_str(), next.protocolVersion,
                next.isEncrypted ? ", encrypted" : "");
  return true;
}

void BluettiClient::disconnect() {
  if (_client != nullptr && _client->isConnected()) {
    _client->disconnect();
  }

  _writeCharacteristic = nullptr;
  // A command that waited for the connection must not run after it comes back.
  xQueueReset(_writeRequests);

  std::lock_guard<std::mutex> lock(_statusMutex);
  _status.isConnected = false;
}

// Disconnects and clears the readings, which would be stale, but keeps what identifies the power station.
void BluettiClient::pause() {
  bool wasConnected = _client != nullptr && _client->isConnected();
  disconnect();

  std::lock_guard<std::mutex> lock(_statusMutex);
  BluettiStatus paused;
  paused.isPolling = false;
  paused.isEncrypted = _status.isEncrypted;
  paused.model = _status.model;
  paused.serialNumber = _status.serialNumber;
  paused.protocolVersion = _status.protocolVersion;
  paused.armVersion = _status.armVersion;
  paused.dspVersion = _status.dspVersion;
  paused.hasAcSwitch = _status.hasAcSwitch;
  paused.hasDcSwitch = _status.hasDcSwitch;
  _status = paused;
  if (wasConnected) {
    Serial.println("Bluetti: polling stopped, disconnected.");
  }
}

// Power stations with the older firmware send nothing, so they are used without encryption.
bool BluettiClient::exchangeKeys(String& error) {
  TickType_t start = xTaskGetTickCount();
  TickType_t deadline = start + pdMS_TO_TICKS(ChallengeWaitMs);
  while (!_encryption.isReady()) {
    // The deadline is checked for every notification, because the key exchange outlasts the wait for the challenge.
    std::vector<uint8_t> notification;
    if (!nextNotification(notification, deadline)) {
      if (!_encryption.isActive()) {
        return true;
      }

      error = "Key exchange failed: timeout. Waiting for " + _encryption.progress() + ", MTU " + String(_client->getMTU()) + ".";
      return false;
    }

    if (!_encryption.isActive() && !isKeyExchange(notification)) {
      Serial.printf("Bluetti: ignored %u bytes before the key exchange.\n", notification.size());
      continue;
    }

    std::vector<uint8_t> message;
    String handleError;
    if (!handleEncrypted(notification, message, handleError)) {
      error = "Key exchange failed: " + handleError + " Waiting for " + _encryption.progress() + ", MTU "
              + String(_client->getMTU()) + ".";
      return false;
    }

    deadline = start + pdMS_TO_TICKS(KeyExchangeTimeoutMs);
  }

  return true;
}

bool BluettiClient::identify(BluettiStatus& status, String& error) {
  // Protocol 2 devices may fill the protocol 1 type register too, so protocol 2 is checked first.
  std::vector<uint16_t> registers;
  if (readRegisters(110, 10, registers, error)) {
    String model = readString(registers, 0, 6, true);
    if (isModel(model)) {
      ModelFeatures features = featuresOf(model);
      status.protocolVersion = 2;
      status.model = model;
      status.serialNumber = readSerialNumber(registers, 6);
      status.hasAcSwitch = features.hasAcSwitch;
      status.hasDcSwitch = features.hasDcSwitch;
      return true;
    }
  }

  if (!readRegisters(10, 11, registers, error)) {
    return false;
  }

  String model = readString(registers, 0, 6, false);
  if (!isModel(model)) {
    error = "Unknown device type '" + model + "'.";
    return false;
  }

  status.protocolVersion = 1;
  status.model = model;
  status.serialNumber = readSerialNumber(registers, 7);
  status.hasAcSwitch = true;
  status.hasDcSwitch = true;
  return true;
}

bool BluettiClient::poll(BluettiStatus& status, String& error) {
  std::vector<uint16_t> registers;
  if (status.protocolVersion == 1) {
    // Registers 10 to 49 hold everything the AC200M, AC300, AC500, EP500 and EB3A share.
    if (!readRegisters(10, 40, registers, error)) {
      return false;
    }

    status.armVersion = readVersion(registers, 13);
    status.dspVersion = readVersion(registers, 15);
    status.dcInputPower = registers[26];
    status.acInputPower = registers[27];
    status.acOutputPower = registers[28];
    status.dcOutputPower = registers[29];
    // The EB3A does not count the generated energy.
    status.powerGeneration = status.model == "EB3A" ? -1 : registers[31] / 10.0;
    status.batteryPercent = registers[33];
    status.acOutputOn = registers[38] == 1;
    status.dcOutputOn = registers[39] == 1;
    return true;
  }

  // Registers 100 to 161 hold the battery, the powers and the generated energy.
  if (!readRegisters(100, 62, registers, error)) {
    return false;
  }

  ModelFeatures features = featuresOf(status.model);
  status.batteryPercent = registers[2];
  status.timeRemaining = features.hasTimeRemaining ? registers[4] / 10.0 : -1;
  status.powerGeneration = features.hasPowerGeneration ? registers[54] / 10.0 : -1;
  // The EP series reports the powers per phase in other registers.
  if (!status.model.startsWith("EP")) {
    status.acOutputPower = registers[42];
    status.dcInputPower = registers[44];
  }

  if (features.hasDcOutputAndAcInputPower) {
    status.dcOutputPower = registers[40];
    status.acInputPower = registers[46];
  }

  if (features.hasSolarInputs) {
    // Registers 1212 to 1222: power, voltage and current of the first input, then of the second one 8 registers later.
    if (!readRegisters(1212, 11, registers, error)) {
      return false;
    }

    for (size_t i = 0; i < 2; i++) {
      status.solarInputs[i].power = registers[8 * i];
      status.solarInputs[i].voltage = registers[8 * i + 1] / 10.0;
      status.solarInputs[i].current = registers[8 * i + 2] / 10.0;
    }
  }

  if (features.hasAcOutputDetails) {
    // Registers 1500 to 1511: the frequency first, the voltage last.
    if (!readRegisters(1500, 12, registers, error)) {
      return false;
    }

    status.acOutputFrequency = registers[0] / 10.0;
    status.acOutputVoltage = registers[11] / 10.0;
  }

  if (status.hasAcSwitch || status.hasDcSwitch) {
    if (!readRegisters(2011, 2, registers, error)) {
      return false;
    }

    status.acOutputOn = status.hasAcSwitch ? registers[0] == 1 : -1;
    status.dcOutputOn = status.hasDcSwitch ? registers[1] == 1 : -1;
  }

  return true;
}

bool BluettiClient::write(const WriteRequest& request, String& error) {
  uint8_t protocolVersion = status().protocolVersion;
  uint16_t address = protocolVersion == 1
                       ? (request.output == BluettiOutput::Ac ? 3007 : 3008)
                       : (request.output == BluettiOutput::Ac ? 2011 : 2012);
  return writeRegister(address, request.isOn ? 1 : 0, error);
}

bool BluettiClient::readRegisters(uint16_t address, uint16_t count, std::vector<uint16_t>& registers, String& error) {
  std::vector<uint8_t> response;
  if (!transact(ReadHoldingRegisters, address, count, 5 + 2 * count, response, error)) {
    return false;
  }

  if (response[2] != 2 * count) {
    error = "Unexpected length of register " + String(address) + ".";
    return false;
  }

  registers.resize(count);
  for (uint16_t i = 0; i < count; i++) {
    registers[i] = (response[3 + 2 * i] << 8) | response[4 + 2 * i];
  }

  return true;
}

bool BluettiClient::writeRegister(uint16_t address, uint16_t value, String& error) {
  std::vector<uint8_t> response;
  if (!transact(WriteSingleRegister, address, value, 8, response, error)) {
    return false;
  }

  // The power station echoes the request.
  if (((response[4] << 8) | response[5]) != value) {
    error = "The power station did not accept the value of register " + String(address) + ".";
    return false;
  }

  return true;
}

bool BluettiClient::transact(uint8_t function, uint16_t address, uint16_t value, size_t responseLength, std::vector<uint8_t>& response, String& error) {
  uint8_t frame[8] = { SlaveAddress, function, (uint8_t)(address >> 8), (uint8_t)address, (uint8_t)(value >> 8), (uint8_t)value };
  uint16_t crc = modbusCrc(frame, 6);
  frame[6] = crc & 0xFF;
  frame[7] = crc >> 8;

  // Without encryption every notification is a response, so a late one of an earlier request must not answer this one.
  if (!_encryption.isActive()) {
    std::lock_guard<std::mutex> lock(_notificationMutex);
    _notifications.clear();
  }

  if (_encryption.isReady()) {
    std::vector<uint8_t> encrypted = _encryption.encrypt(frame, sizeof(frame));
    if (!send(encrypted.data(), encrypted.size(), error)) {
      return false;
    }
  } else if (!send(frame, sizeof(frame), error)) {
    return false;
  }

  // A response can be split across several notifications.
  response.clear();
  TickType_t deadline = xTaskGetTickCount() + pdMS_TO_TICKS(ResponseTimeoutMs);
  while (response.size() < responseLength) {
    std::vector<uint8_t> message;
    if (!nextMessage(message, deadline, error)) {
      error = "No response for register " + String(address) + ": " + error;
      return false;
    }

    response.insert(response.end(), message.begin(), message.end());
    if (response.size() >= 5 && response[1] == (function | ExceptionFlag)) {
      error = "The power station rejected register " + String(address) + " with Modbus exception " + String(response[2]) + ".";
      return false;
    }
  }

  uint16_t expectedCrc = modbusCrc(response.data(), responseLength - 2);
  if (response.size() != responseLength || response[responseLength - 2] != (expectedCrc & 0xFF) || response[responseLength - 1] != (expectedCrc >> 8)) {
    error = "Invalid response for register " + String(address) + ".";
    return false;
  }

  return true;
}

bool BluettiClient::send(const uint8_t* data, size_t length, String& error) {
  if (_writeCharacteristic == nullptr || !_writeCharacteristic->writeValue(const_cast<uint8_t*>(data), length, true)) {
    error = "Could not write to the power station.";
    return false;
  }

  return true;
}

// The next Modbus frame, after answering the key exchange messages that arrive before it.
bool BluettiClient::nextMessage(std::vector<uint8_t>& message, TickType_t deadline, String& error) {
  for (;;) {
    std::vector<uint8_t> notification;
    if (!nextNotification(notification, deadline)) {
      error = "timeout.";
      return false;
    }

    if (!_encryption.isActive() && !isKeyExchange(notification)) {
      message = notification;
      return true;
    }

    if (!handleEncrypted(notification, message, error)) {
      return false;
    }

    if (!message.empty()) {
      return true;
    }
  }
}

// Decrypts a notification into message, which stays empty while a message is incomplete,
// and answers the messages of the key exchange.
bool BluettiClient::handleEncrypted(const std::vector<uint8_t>& notification, std::vector<uint8_t>& message, String& error) {
  if (!_encryption.isReady()) {
    Serial.printf("Bluetti: key exchange notification of %u bytes, starting %02x %02x.\n", notification.size(), notification[0],
                  notification.size() > 1 ? notification[1] : 0);
  }

  std::vector<uint8_t> reply;
  if (!_encryption.handle(notification, reply, message, error)) {
    return false;
  }

  if (!reply.empty()) {
    if (!send(reply.data(), reply.size(), error)) {
      return false;
    }

    Serial.printf("Bluetti: key exchange reply of %u bytes sent.\n", reply.size());
  }

  return true;
}

bool BluettiClient::nextNotification(std::vector<uint8_t>& notification, TickType_t deadline) {
  for (;;) {
    {
      std::lock_guard<std::mutex> lock(_notificationMutex);
      if (!_notifications.empty()) {
        notification = std::move(_notifications.front());
        _notifications.pop_front();
        return true;
      }
    }

    if (isPast(deadline)) {
      return false;
    }

    xSemaphoreTake(_notified, deadline - xTaskGetTickCount());
  }
}

#if defined(CONFIG_NIMBLE_ENABLED)
int BluettiClient::onGapEvent(ble_gap_event* event, void* arg) {
  BluettiClient* client = static_cast<BluettiClient*>(arg);
  if (event->type != BLE_GAP_EVENT_NOTIFY_RX
      || event->notify_rx.conn_handle != client->_connectionHandle
      || event->notify_rx.attr_handle != client->_notifyHandle) {
    return 0;
  }

  uint8_t data[BLE_ATT_ATTR_MAX_LEN];
  uint16_t length = 0;
  if (ble_hs_mbuf_to_flat(event->notify_rx.om, data, sizeof(data), &length) == 0) {
    client->onNotify(data, length);
  }

  return 0;
}
#endif

void BluettiClient::onNotify(const uint8_t* data, size_t length) {
  {
    std::lock_guard<std::mutex> lock(_notificationMutex);
    _notifications.emplace_back(data, data + length);
  }

  xSemaphoreGive(_notified);
}

void BluettiClient::updateStatus(const BluettiStatus& status) {
  std::lock_guard<std::mutex> lock(_statusMutex);
  _status = status;
  // Polling can stop while a poll runs.
  _status.isPolling = _isPolling;
}

void BluettiClient::setError(const String& error) {
  std::lock_guard<std::mutex> lock(_statusMutex);
  _status.error = error;
}
