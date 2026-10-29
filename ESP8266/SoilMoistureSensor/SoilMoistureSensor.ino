#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <H4.h>
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
const DeviceAction actions[] = {
  { "STATUS", "Get", "status" }
};

H4 h4(9600);
MqttDevice device(name, actions, sizeof(actions) / sizeof(actions[0]));
#pragma endregion

void h4setup() {
  pinMode(SENSOR_PIN, INPUT);

  connectToWifi(ssid, password);

  device.addTelemetry("status", readStatus);
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });

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

String readStatus() {
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

  // Automation logic handling thresholds
  if (moisturePercent < WATER_TRIGGER_PCT) {
    doc["status"] = "ALERT: Soil is DRY! Needs watering.";
  } else if (moisturePercent > 75) {
    doc["status"] = "STATUS: Soil is highly saturated / wet.";
  } else {
    doc["status"] = "STATUS: Soil moisture level is optimal.";
  }

  String out;
  serializeJson(doc, out);
  return out;
}
