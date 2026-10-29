#include "DeviceApi.h"

#include <ArduinoJson.h>
#if defined(ARDUINO_ARCH_ESP32)
#include <WiFi.h>
#else
#include <ESP8266WiFi.h>
#endif

static const char* const InfoKey = "info";
static const char* const RestartKey = "restart";
static const char* const ShutdownKey = "shutdown";
static const char* const Online = "online";
static const char* const Offline = "offline";
static const uint8_t QoS = 1;

// Same rules as HomeManagement.Shared.DeviceTopics.ToDeviceId, e.g. "Temperature And Humidity" -> "temperature-and-humidity".
static String toDeviceId(const char* name) {
  String source(name);
  source.trim();
  source.toLowerCase();

  String id;
  for (size_t i = 0; i < source.length(); i++) {
    char c = source[i];
    if (isAlphaNumeric(c) || c == '-' || c == '_') {
      id += c;
    } else if (id.length() > 0 && id[id.length() - 1] != '-') {
      id += '-';
    }
  }

  while (id.endsWith("-")) {
    id.remove(id.length() - 1);
  }

  return id;
}

MqttDevice::MqttDevice(const char* name, const DeviceAction* actions, size_t actionCount)
  : _name(name), _actions(actions), _actionCount(actionCount), _deviceId(toDeviceId(name)) {
}

void MqttDevice::setActions(const DeviceAction* actions, size_t actionCount) {
  _actions = actions;
  _actionCount = actionCount;
  if (_isConnected) {
    publishTwin();
  }
}

void MqttDevice::onCommand(CommandHandler handler) {
  _commandHandler = handler;
}

void MqttDevice::addTelemetry(const char* key, TelemetryReader reader) {
  _telemetry.push_back({ key, reader });
}

void MqttDevice::onPowerOff(PowerOffHandler handler) {
  _powerOffHandler = handler;
}

void MqttDevice::setTelemetryInterval(uint32_t intervalMs) {
  _telemetryIntervalMs = intervalMs;
}

bool MqttDevice::isConnected() const {
  return _isConnected;
}

const String& MqttDevice::deviceId() const {
  return _deviceId;
}

void MqttDevice::begin(const MqttSettings& settings) {
  _bootMillis = millis();
  _url = String(settings.host) + ":" + settings.port;

  _mqtt.onConnect([this](H4AMC_ConnackParam) { onConnected(); });
  _mqtt.onDisconnect([this] {
    _isConnected = false;
    Serial.println("Disconnected from MQTT broker.");
  });
  _mqtt.onError([](int error, int info) {
    Serial.printf("MQTT error %d, info %d.\n", error, info);
  });
  _mqtt.onMessage([this](const char* topic, const uint8_t* payload, size_t length, H4AMC_MessageOptions options) {
    handleMessage(topic, payload, length, options);
  });

  String statusTopic = topic("status");
  _mqtt.setWill(statusTopic.c_str(), QoS, Offline, H4AMC_WillOptions(true));
  _mqtt.connect(_url.c_str(), settings.username, settings.password, _deviceId.c_str());

  h4.every(_telemetryIntervalMs, [this] {
    if (_isConnected) {
      publishAllTelemetry();
    }
  });
}

void MqttDevice::publishTelemetry(const char* key) {
  if (!_isConnected) {
    return;
  }

  if (strcmp(key, InfoKey) == 0) {
    publish(topic("telemetry/") + InfoKey, buildInfoJson(), true);
    return;
  }

  for (const Telemetry& telemetry : _telemetry) {
    if (strcmp(telemetry.key, key) != 0) {
      continue;
    }

    String value = telemetry.reader();
    if (value.length() > 0) {
      publish(topic("telemetry/") + key, value, true);
    }

    return;
  }
}

void MqttDevice::onConnected() {
  _isConnected = true;
  Serial.printf("Connected to MQTT broker %s as '%s'.\n", _url.c_str(), _deviceId.c_str());

  _mqtt.subscribe(topic("commands/#").c_str(), H4AMC_SubscriptionOptions(QoS));
  publishTwin();
  publish(topic("status"), Online, true);
  publishAllTelemetry();
}

void MqttDevice::publishTwin() {
  static const DeviceAction builtInActions[] = {
    { "INFO", "Get", InfoKey },
    { "RESTART", "Post", RestartKey },
    { "SHUTDOWN", "Post", ShutdownKey }
  };

  JsonDocument doc;
  doc["name"] = _name;
  JsonArray actions = doc["actions"].to<JsonArray>();
  for (const DeviceAction& action : builtInActions) {
    JsonObject item = actions.add<JsonObject>();
    item["action"] = action.action;
    item["commandType"] = action.commandType;
    item["command"] = action.command;
  }

  for (size_t i = 0; i < _actionCount; i++) {
    JsonObject item = actions.add<JsonObject>();
    item["action"] = _actions[i].action;
    item["commandType"] = _actions[i].commandType;
    item["command"] = _actions[i].command;
  }

  String json;
  serializeJson(doc, json);
  publish(topic("twin"), json, true);
}

void MqttDevice::publishAllTelemetry() {
  publishTelemetry(InfoKey);
  for (const Telemetry& telemetry : _telemetry) {
    publishTelemetry(telemetry.key);
  }
}

void MqttDevice::handleMessage(const char* topicName, const uint8_t*, size_t, H4AMC_MessageOptions options) {
  String receivedTopic(topicName);
  String commandsPrefix = topic("commands/");
  if (!receivedTopic.startsWith(commandsPrefix)) {
    return;
  }

  String command = receivedTopic.substring(commandsPrefix.length());
  MQTT5PublishProperties& properties = options.getProperties();
  H4AMC_BinaryData correlationData = properties.correlation_data;
  std::string responseTopic = properties.response_topic;

  if (!correlationData.empty() && !markHandled(correlationData)) {
    Serial.printf("Skipping duplicate command '%s'.\n", command.c_str());
    return;
  }

  bool isPowerOff = command == RestartKey || command == ShutdownKey;
  CommandResult result = isPowerOff
                           ? CommandResult{ true, command == RestartKey ? "Restarting" : "Shutting down" }
                           : execute(command);
  Serial.printf("Command '%s' executed. Success: %s.\n", command.c_str(), result.isSuccess ? "true" : "false");

  if (!responseTopic.empty()) {
    JsonDocument response;
    response["isSuccess"] = result.isSuccess;
    response["output"] = result.output;

    String json;
    serializeJson(response, json);
    publish(String(responseTopic.c_str()), json, false, MQTT5PublishProperties(0, 0, {}, {}, correlationData));
  }

  if (isPowerOff) {
    powerOff(command == RestartKey);
  }
}

CommandResult MqttDevice::execute(const String& command) {
  if (!_commandHandler) {
    return { false, "Unknown command '" + command + "'." };
  }

  return _commandHandler(command);
}

bool MqttDevice::markHandled(const H4AMC_BinaryData& requestId) {
  for (const H4AMC_BinaryData& handled : _handledRequests) {
    if (handled == requestId) {
      return false;
    }
  }

  _handledRequests[_nextHandledRequest] = requestId;
  _nextHandledRequest = (_nextHandledRequest + 1) % HandledRequestCount;
  return true;
}

void MqttDevice::powerOff(bool restart) {
  if (_powerOffHandler) {
    _powerOffHandler();
  }

  // The last will is only sent on an unexpected disconnect.
  publish(topic("status"), Offline, true);

  // Give the response and the status time to be sent before the connection closes.
  h4.once(500, [this] { _mqtt.disconnect(); });
  h4.once(1000, [restart] {
    if (restart) {
      ESP.restart();
    } else {
#if defined(ARDUINO_ARCH_ESP32)
      esp_deep_sleep_start();
#else
      ESP.deepSleep(0);
#endif
    }
  });
}

void MqttDevice::publish(const String& topicName, const String& payload, bool retained, MQTT5PublishProperties properties) {
  _mqtt.publish(topicName.c_str(), payload.c_str(), payload.length(), QoS, H4AMC_PublishOptions(retained, properties));
}

String MqttDevice::topic(const char* kind) const {
  return "home/devices/" + _deviceId + "/" + kind;
}

String MqttDevice::buildInfoJson() const {
  JsonDocument doc;
  doc["name"] = _name;
  doc["address"] = WiFi.localIP().toString();

  JsonObject metrics = doc["metrics"].to<JsonObject>();
  metrics["UptimeSeconds"] = String((millis() - _bootMillis) / 1000UL);
  metrics["FreeHeap"] = String(ESP.getFreeHeap());
  metrics["Rssi"] = String(WiFi.RSSI());

  String json;
  serializeJson(doc, json);
  return json;
}
