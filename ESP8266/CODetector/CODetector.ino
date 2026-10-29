#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <H4.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

#pragma region Configuration
const char* name = "CODetector";

const int analogPin = A0;
const int digitalPin = D1;

// CALIBRATION:
// The clean air reading differs per sensor and module, so the baseline is learned from the first measurement after boot.
// It follows cleaner air straight away and drifts up slowly while the air is safe to compensate temperature and humidity drift.
const int DANGER_DELTA = 155;       // Analog rise above the baseline that counts as 100%
const float BASELINE_DRIFT = 0.01;  // Share of a safe measurement the baseline moves up by, about 4 hours to follow a new level

const unsigned long CLEANING_PHASE = 60000;  // 60s @ 5V
const unsigned long MEASURING_PHASE = 90000; // 90s @ 1.4V (simulated)
const unsigned long SAMPLING_WINDOW = 30000; // The sensor is read at the end of the measuring phase
#pragma endregion

#pragma region Globals
unsigned long stateStartTime = 0;
bool isCleaning = true;
float baseline = 0; // 0 until the first measurement
long sampleSum = 0;
int sampleCount = 0;

struct SensorData {
    int rawA = 0;
    int rawD = 1;
    float coPercent = 0.0;
    const char* phase = "Warm-up";
    const char* severity = "SAFE";
} currentReadings;

const DeviceAction actions[] = {
  { "STATUS", "Get", "status" }
};

H4 h4(9600);
MqttDevice device(name, actions, sizeof(actions) / sizeof(actions[0]));
#pragma endregion

void h4setup() {
  stateStartTime = millis();

  pinMode(digitalPin, INPUT);

  connectToWifi(ssid, password);

  device.addTelemetry("status", readStatus);
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });

  h4.every(1000, [] { processSensorLogic(millis()); });
  Serial.println("System Ready.");
}

void processSensorLogic(unsigned long now) {
  unsigned long elapsed = now - stateStartTime;
  const char* previousPhase = currentReadings.phase;
  const char* previousSeverity = currentReadings.severity;

  if (isCleaning) {
    currentReadings.phase = "Cleaning";
    if (elapsed >= CLEANING_PHASE) {
      isCleaning = false;
      stateStartTime = now;
    }
  } else {
    currentReadings.phase = "Measuring";
    if (elapsed >= MEASURING_PHASE - SAMPLING_WINDOW) {
      sampleSum += analogRead(analogPin);
      sampleCount++;
    }

    if (elapsed >= MEASURING_PHASE) {
      completeMeasurement();
      isCleaning = true;
      stateStartTime = now;
    }
  }

  // Report changes straight away instead of waiting for the next telemetry interval.
  if (currentReadings.phase != previousPhase || currentReadings.severity != previousSeverity) {
    device.publishTelemetry("status");
  }
}

void completeMeasurement() {
  currentReadings.rawA = sampleSum / sampleCount;
  currentReadings.rawD = digitalRead(digitalPin);
  sampleSum = 0;
  sampleCount = 0;

  if (baseline == 0 || currentReadings.rawA < baseline) {
    baseline = currentReadings.rawA;
  }

  float rawPercent = (currentReadings.rawA - baseline) / DANGER_DELTA * 100.0;
  currentReadings.coPercent = constrain(rawPercent, 0.0, 500.0);

  if (currentReadings.coPercent >= 300.0) {
    currentReadings.severity = "CRITICAL";
  }
  else if (currentReadings.coPercent >= 100.0) {
    currentReadings.severity = "VERY BAD";
  }
  else if (currentReadings.coPercent >= 15.0) {
    currentReadings.severity = "RISK";
  }
  else {
    currentReadings.severity = "SAFE";
    baseline += (currentReadings.rawA - baseline) * BASELINE_DRIFT;
  }

  Serial.printf("A: %d | D: %d | Baseline: %.1f | CO: %.2f%% | Status: %s\n",
                currentReadings.rawA,
                currentReadings.rawD,
                baseline,
                currentReadings.coPercent,
                currentReadings.severity);
}

#pragma region Telemetry
String readStatus() {
  JsonDocument doc;
  doc["phase"] = currentReadings.phase;
  doc["analog"] = currentReadings.rawA;
  doc["digital"] = currentReadings.rawD;
  doc["baseline"] = serialized(String(baseline, 1));
  doc["co_percent"] = serialized(String(currentReadings.coPercent, 2));
  doc["severity"] = currentReadings.severity;

  String out;
  serializeJson(doc, out);
  return out;
}
#pragma endregion
