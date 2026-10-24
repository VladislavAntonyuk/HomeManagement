#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266mDNS.h>
#include <ArduinoJson.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

#define SENSOR_PIN A0
const int VAL_DRY = 785;  // 0% Moisture (In Air)
const int VAL_WET = 284;  // 100% Moisture (In Water)

const int WATER_TRIGGER_PCT = 20;  // Trigger watering alert when moisture falls below 20%

#pragma region Configuration
const char* name = "SoilMoistureSensor";
#pragma endregion

#pragma region Globals
ESP8266WebServer server(80);
unsigned long bootMillis = 0;
const DeviceAction actions[] = {
  { "STATUS", "GET", "status", "" },
  { "RESTART", "POST", "restart", "{}" },
  { "SHUTDOWN", "POST", "shutdown", "{}" }
};
#pragma endregion

void setup() {
  Serial.begin(9600);
  bootMillis = millis();
  pinMode(SENSOR_PIN, INPUT);

  connectToWifi(ssid, password);
  startMDNS(name);

  server.on("/config", HTTP_GET, handleGetConfig);
  server.on("/info", HTTP_GET, handleGetInfo);
  server.on("/status", HTTP_GET, handleGetStatus);
  server.on("/restart", HTTP_POST, handleRestart);
  server.on("/shutdown", HTTP_POST, handleShutdown);
  registerNotFoundHandler(server);

  server.begin();
  Serial.println("\n================================================");
  Serial.println("--- Automated Soil Moisture System Started ---");
  Serial.print("Calibration Limits: Dry=");
  Serial.print(VAL_DRY);
  Serial.print(" | Wet=");
  Serial.println(VAL_WET);
  Serial.print("Watering Trigger Threshold: ");
  Serial.print(WATER_TRIGGER_PCT);
  Serial.println("%");
  Serial.println("================================================");
}

void loop() {
  MDNS.update();
  server.handleClient();
}

String handleGetStatus() {
  long totalRaw = 0;
  const int samples = 10;

  // Smooth out readings to handle minor electrical noise
  for (int i = 0; i < samples; i++) {
    totalRaw += analogRead(SENSOR_PIN);
    delay(10);  // 10ms gap between samples
  }

  int avgRaw = totalRaw / samples;

  // Calculate voltage based on the ESP8266 3.3V ADC reference
  float voltage = (avgRaw * 3.3) / 1023.0;

  // Map and clamp the inverted range to 0-100%
  int moisturePercent = map(avgRaw, VAL_DRY, VAL_WET, 0, 100);
  moisturePercent = constrain(moisturePercent, 0, 100);

  JsonDocument doc;
  doc["ADC"] = avgRaw;
  doc["voltage"] = voltage;
  doc["moisture"] = moisturePercent;

  String result = "";
  // Automation logic handling thresholds
  if (moisturePercent < WATER_TRIGGER_PCT) {
    doc["status"] = "ALERT: Soil is DRY! Needs watering.";
    result = "ALERT: Soil is DRY! Needs watering." + String(moisturePercent) + "%";
  } else if (moisturePercent > 75) {
    doc["status"] = "STATUS: Soil is highly saturated / wet.";
    result = "STATUS: Soil is highly saturated / wet." + String(moisturePercent) + "%";
  } else {
    doc["status"] = "STATUS: Soil moisture level is optimal.";
  }

  String out;
  serializeJson(doc, out);
  server.send(200, "application/json", out);
  return result;
}

void handleGetInfo() {
  server.send(200, "application/json", buildDeviceInfoJson(name, bootMillis));
}

void handleGetConfig() {
  server.send(200, "application/json", buildDeviceConfigJson(name, actions, sizeof(actions) / sizeof(actions[0])));
}

void handleRestart() {
  sendRestartResponse(server);
}

void handleShutdown() {
  sendShutdownResponse(server);
}

