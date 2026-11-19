#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <H4.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

#pragma region Configuration
const char* name = "CODetector";

const int analogPin = A0;
const int digitalPin = D1; // Comparator of the module, LOW when the gas is above the threshold set by its potentiometer

// CALIBRATION:
// The clean air reading differs per sensor, module, supply voltage and potentiometer position, so it is measured with
// the CALIBRATE action while the sensor is in clean air, and stored in flash. It is never learned automatically:
// a sensor that starts in gas would take the gas for clean air.
// RISK comes from the comparator of the module, set with its potentiometer just above clean air.
// The analog reading raises VERY BAD and CRITICAL, far above anything the potentiometer or drift cause.
const int DANGER_DELTA = 155;     // Analog rise above the baseline that counts as 100%
const int MAX_CALIBRATION_CHANGE = 3; // The last two measurements have to agree this well to take them as settled
const int MIN_VALID_READING = 20; // A powered sensor never reads this low, lower means it is unpowered or disconnected
// Clean air has to leave room below the ADC maximum of 1024 to reach CRITICAL (300%), higher means the sensor is in gas
const int MAX_BASELINE = 1024 - DANGER_DELTA * 3;

const unsigned long CLEANING_PHASE = 60000;  // 60s @ 5V
const unsigned long MEASURING_PHASE = 90000; // 90s @ 1.4V (simulated)
const unsigned long SAMPLING_WINDOW = 30000; // The sensor is read at the end of the measuring phase
#pragma endregion

#pragma region Globals
unsigned long stateStartTime = 0;
bool isCleaning = true;
long sampleSum = 0;
int sampleCount = 0;
bool hasMeasurement = false;
int previousRawA = -1; // -1 until the second measurement
float baseline = 0; // 0 until calibrated

Preferences preferences;

struct SensorData {
    int rawA = 0;
    int rawD = 1;
    float coPercent = 0.0;
    const char* phase = "Warm-up";
    const char* severity = "SAFE";
} currentReadings;

const DeviceAction actions[] = {
  { "STATUS", "Get", "status" },
  { "CALIBRATE", "Post", "calibrate" }
};

H4 h4(9600);
MqttDevice device(name, actions, sizeof(actions) / sizeof(actions[0]));
#pragma endregion

void h4setup() {
  stateStartTime = millis();

  pinMode(digitalPin, INPUT);

  preferences.begin("co", true); // readonly = true
  baseline = preferences.getFloat("baseline", 0);
  preferences.end();
  if (!isCalibrated()) {
    Serial.println("Not calibrated. Run CALIBRATE in clean air.");
  }

  connectToWifi(ssid, password);

  device.onCommand(handleCommand);
  device.addTelemetry("status", readStatus);
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });

  h4.every(1000, [] { processSensorLogic(millis()); });
  Serial.println("System Ready.");
}

bool isCalibrated() {
  return baseline > 0;
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
  if (hasMeasurement) {
    previousRawA = currentReadings.rawA;
  }
  currentReadings.rawA = sampleSum / sampleCount;
  currentReadings.rawD = digitalRead(digitalPin);
  sampleSum = 0;
  sampleCount = 0;
  hasMeasurement = true;

  if (currentReadings.rawA < MIN_VALID_READING) {
    currentReadings.coPercent = 0;
    currentReadings.severity = "FAULT";
    Serial.printf("A: %d | D: %d | Sensor fault, check its power and wiring\n", currentReadings.rawA, currentReadings.rawD);
    return;
  }

  bool isComparatorAlarm = currentReadings.rawD == LOW;

  if (!isCalibrated()) {
    currentReadings.coPercent = 0;
    currentReadings.severity = isComparatorAlarm ? "RISK" : "UNCALIBRATED";
    Serial.printf("A: %d | D: %d | Not calibrated | Status: %s\n",
                  currentReadings.rawA,
                  currentReadings.rawD,
                  currentReadings.severity);
    return;
  }

  float rawPercent = (currentReadings.rawA - baseline) / DANGER_DELTA * 100.0;
  currentReadings.coPercent = constrain(rawPercent, 0.0, 500.0);

  if (currentReadings.coPercent >= 300.0) {
    currentReadings.severity = "CRITICAL";
  }
  else if (currentReadings.coPercent >= 100.0) {
    currentReadings.severity = "VERY BAD";
  }
  else if (isComparatorAlarm) {
    currentReadings.severity = "RISK";
  }
  else {
    currentReadings.severity = "SAFE";
  }

  Serial.printf("A: %d | D: %d | Baseline: %.1f | CO: %.2f%% | Status: %s\n",
                currentReadings.rawA,
                currentReadings.rawD,
                baseline,
                currentReadings.coPercent,
                currentReadings.severity);
}

#pragma region Commands
CommandResult handleCommand(const String& command) {
  if (command == "calibrate") {
    return calibrate();
  }

  return { false, "Unknown command '" + command + "'." };
}

// Takes the latest measurement as clean air, so run it only when the sensor is in clean air.
CommandResult calibrate() {
  if (previousRawA < 0) {
    return { false, "Not enough measurements yet, try again in " + String((CLEANING_PHASE + MEASURING_PHASE) / 60000.0, 1) + " minutes." };
  }

  if (abs(currentReadings.rawA - previousRawA) > MAX_CALIBRATION_CHANGE) {
    return { false, "The sensor is not settled yet (" + String(previousRawA) + ", then " + String(currentReadings.rawA) + "), try again after the next measurement." };
  }

  if (currentReadings.rawA < MIN_VALID_READING) {
    return { false, "Sensor fault, check its power and wiring." };
  }

  if (currentReadings.rawA > MAX_BASELINE) {
    return { false, "The sensor reads " + String(currentReadings.rawA) + ", clean air reads at most " + String(MAX_BASELINE) + ". Calibrate in clean air." };
  }

  if (currentReadings.rawD == LOW) {
    return { false, "The module detects gas, calibrate in clean air." };
  }

  preferences.begin("co", false);
  bool isStored = preferences.putFloat("baseline", currentReadings.rawA) > 0;
  preferences.end();
  if (!isStored) {
    return { false, "Failed to store the calibration." };
  }

  baseline = currentReadings.rawA;

  // The current measurement is clean air by definition.
  currentReadings.coPercent = 0;
  currentReadings.severity = "SAFE";
  device.publishTelemetry("status");

  return { true, "Baseline set to " + String(baseline, 1) + "." };
}
#pragma endregion

#pragma region Telemetry
String readStatus() {
  JsonDocument doc;
  doc["phase"] = currentReadings.phase;
  doc["analog"] = currentReadings.rawA;
  doc["digital"] = currentReadings.rawD;
  if (isCalibrated()) {
    doc["baseline"] = serialized(String(baseline, 1));
  }
  doc["co_percent"] = serialized(String(currentReadings.coPercent, 2));
  doc["severity"] = currentReadings.severity;

  String out;
  serializeJson(doc, out);
  return out;
}
#pragma endregion
