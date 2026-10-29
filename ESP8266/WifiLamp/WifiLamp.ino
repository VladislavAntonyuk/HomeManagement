#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <H4.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

#pragma region Variables
const char* name = "second-floor-door-lamp";

const char* lampPinName = "D1";
const int lampPin = D1;

struct PinMap {
  int dNumber;  // e.g., 1 for D1
  int gpio;     // e.g., D1
};

const DeviceAction actions[] = {
  { "ON", "Post", "lamp/on" },
  { "OFF", "Post", "lamp/off" },
  { "STATUS", "Get", "lamp" }
};

// Only safe pins
PinMap availablePins[] = {
  { 1, D1 },
  { 2, D2 },
  { 5, D5 },
  { 6, D6 },
  { 7, D7 }
};

H4 h4(9600);
MqttDevice device(name, actions, sizeof(actions) / sizeof(actions[0]));
#pragma endregion Variables

void h4setup() {
  resetPins();
  connectToWifi(ssid, password);

  device.onCommand(handleCommand);
  device.addTelemetry("lamp", readLampStatus);
  device.onPowerOff(resetPins);
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });
}

#pragma region Commands
CommandResult handleCommand(const String& command) {
  if (command != "lamp/on" && command != "lamp/off") {
    return { false, "Unknown command '" + command + "'." };
  }

  bool isOn = command == "lamp/on";
  digitalWrite(lampPin, isOn ? LOW : HIGH);
  Serial.printf("%s %s\n", lampPinName, isOn ? "on" : "off");

  device.publishTelemetry("lamp");
  return { true, readLampStatus() };
}

String readLampStatus() {
  JsonDocument doc;
  doc["pin"] = lampPinName;
  doc["status"] = digitalRead(lampPin) == LOW ? "on" : "off";

  String json;
  serializeJson(doc, json);
  return json;
}
#pragma endregion Commands

void resetPins() {
  // Initialize all available pins as OUTPUT and turn OFF (HIGH)
  for (size_t i = 0; i < sizeof(availablePins) / sizeof(availablePins[0]); i++) {
    pinMode(availablePins[i].gpio, OUTPUT);
    digitalWrite(availablePins[i].gpio, HIGH);  // OFF for active LOW
  }
}
