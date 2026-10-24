#include "DeviceApi.h"

#include <ArduinoJson.h>
#include <ESP8266WiFi.h>

static String deviceAddress() {
  return "http://" + WiFi.localIP().toString();
}

String buildDeviceConfigJson(const char* name, const DeviceAction* actions, size_t actionCount) {
  JsonDocument doc;

  doc["name"] = name;
  doc["address"] = deviceAddress();

  JsonArray actionsJson = doc["actions"].to<JsonArray>();
  for (size_t i = 0; i < actionCount; i++) {
    const DeviceAction& action = actions[i];
    JsonObject obj = actionsJson.add<JsonObject>();
    obj["action"] = action.action;
    obj["command"] = action.command;
    obj["commandType"] = action.commandType;
    obj["commandArgs"] = action.commandArgs;
  }

  String json;
  serializeJson(doc, json);
  return json;
}

String buildDeviceInfoJson(const char* name, unsigned long bootMillis) {
  unsigned long upSeconds = (millis() - bootMillis) / 1000UL;
  JsonDocument doc;

  doc["name"] = name;
  doc["address"] = deviceAddress();

  JsonArray metricsJson = doc["metrics"].to<JsonArray>();
  JsonObject metrics_0 = metricsJson.add<JsonObject>();
  metrics_0["uptimeSeconds"] = upSeconds;

  String json;
  serializeJson(doc, json);
  return json;
}
