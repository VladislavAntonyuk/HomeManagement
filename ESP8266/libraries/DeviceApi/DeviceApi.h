#pragma once

#include <Arduino.h>

struct DeviceAction {
  const char* action;
  const char* commandType;
  const char* command;
  const char* commandArgs = "";
};

// Matches HomeManagement.Shared.DeviceConfig: {"name", "address", "actions": [{"action","commandType","command","commandArgs"}]}
String buildDeviceConfigJson(const char* name, const DeviceAction* actions, size_t actionCount);

// {"name", "address", "metrics": [{"uptimeSeconds"}]}
String buildDeviceInfoJson(const char* name, unsigned long bootMillis);

template <typename ServerT>
void registerNotFoundHandler(ServerT& server) {
  server.onNotFound([&server]() {
    server.send(404, "text/plain", "Not found");
  });
}

template <typename ServerT>
void sendRestartResponse(ServerT& server) {
  server.send(200, "text/plain", "Restarting");
  delay(500);
  ESP.restart();
}

template <typename ServerT>
void sendShutdownResponse(ServerT& server) {
  server.send(200, "text/plain", "Entering Deep Sleep");
  delay(500);
  ESP.deepSleep(0);
}
