#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266mDNS.h>
#include <ArduinoJson.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

#pragma region Configuration
const char* name = "CODetector";

const int analogPin = A0;   
const int digitalPin = D1;

// CALIBRATION:
int baseline = 45;       
const int DANGER_VAL = 200; 

const unsigned long CLEANING_PHASE = 60000;  // 60s @ 5V
const unsigned long MEASURING_PHASE = 90000; // 90s @ 1.4V (simulated)
#pragma endregion

#pragma region Globals
ESP8266WebServer server(80);
unsigned long bootMillis = 0;
unsigned long stateStartTime = 0;
unsigned long lastLoopTick = 0;
bool isCleaning = true;

struct SensorData {
    int rawA = 0;
    int rawD = 1;
    float coPercent = 0.0;
    const char* phase = "Warm-up";
    const char* severity = "SAFE";
} currentReadings;

const DeviceAction actions[] = {
  { "STATUS", "GET", "status", "" },
  { "RESTART", "POST", "restart", "{}" },
  { "SHUTDOWN", "POST", "shutdown", "{}" }
};
#pragma endregion

void setup() {
  Serial.begin(9600);
  bootMillis = millis();
  stateStartTime = bootMillis;
  
  pinMode(digitalPin, INPUT);
  
  connectToWifi(ssid, password);
  startMDNS(name);

  server.on("/config", HTTP_GET, handleGetConfig);
  server.on("/info", HTTP_GET, handleGetInfo);
  server.on("/status", HTTP_GET, handleGetStatus);
  server.on("/restart", HTTP_POST, handleRestart);
  server.on("/shutdown", HTTP_POST, handleShutdown);
  registerNotFoundHandler(server);
  
  server.begin();
  Serial.println("System Ready.");
}

void loop() {
  MDNS.update();
  server.handleClient();

  unsigned long now = millis();
  
  if (now - lastLoopTick >= 1000) {
    lastLoopTick = now;
    processSensorLogic(now);
  }
}

void processSensorLogic(unsigned long now) {
  unsigned long elapsed = now - stateStartTime;

  if (isCleaning) {
    currentReadings.phase = "Cleaning";
    if (elapsed >= CLEANING_PHASE) {
      isCleaning = false;
      stateStartTime = now;
    }
  } else {
    currentReadings.phase = "Measuring";
    currentReadings.rawA = analogRead(analogPin);
    currentReadings.rawD = digitalRead(digitalPin);
    
    // Auto-adjust baseline if current air is cleaner than stored baseline
    if (currentReadings.rawA < baseline && currentReadings.rawA > 10) {
        baseline = currentReadings.rawA;
    }

    float rawPercent = ((float)(currentReadings.rawA - baseline) / (DANGER_VAL - baseline)) * 100.0;
    currentReadings.coPercent = constrain(rawPercent, 0.0, 500.0);

    if (currentReadings.coPercent >= 300.0){
      currentReadings.severity = "CRITICAL";
    } 
    else if (currentReadings.coPercent >= 100.0){
      currentReadings.severity = "VERY BAD";
    } 
    else if (currentReadings.coPercent >= 15.0){
      currentReadings.severity = "RISK";
    }
    else {
      currentReadings.severity = "SAFE";
    }

    Serial.printf("P: %s | A: %d | D: %d | CO: %.2f%% | Status: %s\n", 
                  currentReadings.phase, 
                  currentReadings.rawA, 
                  currentReadings.rawD, 
                  currentReadings.coPercent,
                  currentReadings.severity);

    if (elapsed >= MEASURING_PHASE) {
      isCleaning = true;
      stateStartTime = now;
    }
  }
}

#pragma region Handlers
void handleGetStatus() {
  JsonDocument doc;
  doc["phase"] = currentReadings.phase;
  doc["analog"] = currentReadings.rawA;
  doc["digital"] = currentReadings.rawD;
  doc["co_percent"] = serialized(String(currentReadings.coPercent, 2));
  doc["severity"] = currentReadings.severity;

  String out;
  serializeJson(doc, out);
  server.send(200, "application/json", out);
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

#pragma endregion