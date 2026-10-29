#pragma once

#include <Arduino.h>
#include <H4.h>
#include <H4AsyncMQTT.h>
#include <functional>
#include <string>
#include <vector>

// An action of the device, matching HomeManagement.Shared.DeviceAction.
// commandType is "Get" (read from the latest telemetry) or "Post" (executed as a command).
struct DeviceAction {
  const char* action;
  const char* commandType;
  const char* command;
};

struct CommandResult {
  bool isSuccess;
  String output;
};

struct MqttSettings {
  const char* host;
  uint16_t port = 1883;
  // Empty when the broker does not require a login.
  const char* username = "";
  const char* password = "";
};

// The device in the protocol of HomeManagement/DeviceProtocol.md: connects to the MQTT broker with MQTT v5,
// publishes its twin, status (with an "offline" last will) and telemetry, and executes commands.
// INFO, RESTART and SHUTDOWN are built in. Requires the H4 scheduler: declare `H4 h4(baud);` in the sketch.
class MqttDevice {
public:
  using CommandHandler = std::function<CommandResult(const String& command)>;
  using TelemetryReader = std::function<String()>;
  using PowerOffHandler = std::function<void()>;

  MqttDevice(const char* name, const DeviceAction* actions, size_t actionCount);

  // Replaces the actions, e.g. once the device knows what its hardware supports, and publishes the twin again.
  void setActions(const DeviceAction* actions, size_t actionCount);

  // Executes the Post actions of the device.
  void onCommand(CommandHandler handler);

  // Publishes the value of a Get action at every telemetry interval and when publishTelemetry is called.
  // A reader may return an empty string to skip publishing.
  void addTelemetry(const char* key, TelemetryReader reader);

  // Called before the device restarts or shuts down, e.g. to switch relays off.
  void onPowerOff(PowerOffHandler handler);

  void setTelemetryInterval(uint32_t intervalMs);

  // Call from h4setup(). Connects, and reconnects whenever the connection drops.
  void begin(const MqttSettings& settings);

  // Publishes one telemetry value now, e.g. after a state change.
  void publishTelemetry(const char* key);

  bool isConnected() const;

  const String& deviceId() const;

private:
  struct Telemetry {
    const char* key;
    TelemetryReader reader;
  };

  static const size_t HandledRequestCount = 8;

  const char* _name;
  const DeviceAction* _actions;
  size_t _actionCount;
  String _deviceId;
  String _url;
  H4AsyncMQTT _mqtt;
  CommandHandler _commandHandler;
  PowerOffHandler _powerOffHandler;
  std::vector<Telemetry> _telemetry;
  uint32_t _telemetryIntervalMs = 60000;
  bool _isConnected = false;
  unsigned long _bootMillis = 0;
  H4AMC_BinaryData _handledRequests[HandledRequestCount];
  size_t _nextHandledRequest = 0;

  void onConnected();
  void publishTwin();
  void publishAllTelemetry();
  void handleMessage(const char* topic, const uint8_t* payload, size_t length, H4AMC_MessageOptions options);
  CommandResult execute(const String& command);
  bool markHandled(const H4AMC_BinaryData& requestId);
  void powerOff(bool restart);
  void publish(const String& topic, const String& payload, bool retained, MQTT5PublishProperties properties = {});
  String topic(const char* kind) const;
  String buildInfoJson() const;
};
