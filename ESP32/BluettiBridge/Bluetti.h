#pragma once

#include <Arduino.h>
#include <freertos/FreeRTOS.h>
#include <freertos/queue.h>
#include <freertos/semphr.h>
#include <atomic>
#include <deque>
#include <mutex>
#include <vector>
#include "BluettiEncryption.h"

class BLEClient;
class BLERemoteCharacteristic;
struct ble_gap_event;

enum class BluettiOutput {
  Ac,
  Dc
};

struct BluettiSolarInput {
  int power = -1;
  float voltage = -1;
  float current = -1;
};

// The latest values read from the power station. Numbers are -1 when the model does not report them.
struct BluettiStatus {
  // False while polling is stopped: the power station is then disconnected and the readings are cleared.
  bool isPolling = true;
  bool isConnected = false;
  bool isEncrypted = false;
  String model;
  String serialNumber;
  // 1 for AC200M, AC300, AC500, EP500 and EB3A, 2 for the newer models such as AC180, AC70, AP300 and EP600.
  uint8_t protocolVersion = 0;
  String armVersion;
  String dspVersion;
  // Whether the outputs of the model can be switched.
  bool hasAcSwitch = false;
  bool hasDcSwitch = false;
  int batteryPercent = -1;
  int acInputPower = -1;
  int dcInputPower = -1;
  int acOutputPower = -1;
  int dcOutputPower = -1;
  float acOutputVoltage = -1;
  float acOutputFrequency = -1;
  // Hours until full while charging, until empty while discharging, as in the Bluetti app.
  float timeRemaining = -1;
  float powerGeneration = -1;
  BluettiSolarInput solarInputs[2];
  int acOutputOn = -1;
  int dcOutputOn = -1;
  String error;
};

// Reads a Bluetti power station over Bluetooth LE with the Modbus protocol of the Bluetti app,
// encrypted on the newer firmware.
// The client runs in its own task: it finds the power station, polls it, and reconnects when the connection drops.
class BluettiClient {
public:
  // An empty address connects to the first Bluetti power station found.
  void begin(const char* address, uint32_t pollIntervalMs, bool isPolling);

  // A copy of the latest status, safe to call from any task.
  BluettiStatus status();

  // Starts or stops polling. Stopping disconnects from the power station, so it is free for the Bluetti app.
  void setPolling(bool isPolling);

  // Switches an output and waits for the power station to confirm it.
  bool setOutput(BluettiOutput output, bool isOn, String& error);

private:
  struct WriteRequest {
    BluettiOutput output;
    bool isOn;
  };

  struct WriteResult {
    bool isSuccess;
    char error[96];
  };

  const char* _address = "";
  uint32_t _pollIntervalMs = 10000;
  BLEClient* _client = nullptr;
  BLERemoteCharacteristic* _writeCharacteristic = nullptr;
  QueueHandle_t _writeRequests = nullptr;
  QueueHandle_t _writeResults = nullptr;
  BluettiEncryption _encryption;
  std::atomic<bool> _isPolling{ true };
  // Wakes the task of the client when polling starts again.
  SemaphoreHandle_t _pollingChanged = nullptr;

  std::mutex _statusMutex;
  BluettiStatus _status;

  // Notifications are handled in the task of the client, because answering the key exchange writes to the power station.
  std::mutex _notificationMutex;
  std::deque<std::vector<uint8_t>> _notifications;
  SemaphoreHandle_t _notified = nullptr;

#if defined(CONFIG_NIMBLE_ENABLED)
  // The notification characteristic of the connection, read by onGapEvent from the NimBLE task.
  volatile uint16_t _connectionHandle = 0xFFFF;
  volatile uint16_t _notifyHandle = 0;

  static int onGapEvent(ble_gap_event* event, void* arg);
#endif

  static void runTask(void* parameter);
  void run();
  bool connect(String& error);
  void disconnect();
  void pause();
  bool exchangeKeys(String& error);
  bool identify(BluettiStatus& status, String& error);
  bool poll(BluettiStatus& status, String& error);
  bool write(const WriteRequest& request, String& error);
  bool readRegisters(uint16_t address, uint16_t count, std::vector<uint16_t>& registers, String& error);
  bool writeRegister(uint16_t address, uint16_t value, String& error);
  bool transact(uint8_t function, uint16_t address, uint16_t value, size_t responseLength, std::vector<uint8_t>& response, String& error);
  bool send(const uint8_t* data, size_t length, String& error);
  bool nextMessage(std::vector<uint8_t>& message, TickType_t deadline, String& error);
  bool handleEncrypted(const std::vector<uint8_t>& notification, std::vector<uint8_t>& message, String& error);
  bool nextNotification(std::vector<uint8_t>& notification, TickType_t deadline);
  void onNotify(const uint8_t* data, size_t length);
  void updateStatus(const BluettiStatus& status);
  void setError(const String& error);
};
