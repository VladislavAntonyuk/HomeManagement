#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266mDNS.h>
#include <ArduinoJson.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

const int TRIG_PIN = 4; // D2 (GPIO4)
const int ECHO_PIN = 5; // D1 (GPIO5)

#pragma region Configuration
const char* name = "WellWaterLevel";
#pragma endregion

#pragma region Globals
ESP8266WebServer server(80);
unsigned long bootMillis = 0;
const DeviceAction actions[] = {
  { "INFO", "GET", "info", "" },
  { "Distance", "GET", "distance", "" },
  { "RESTART", "POST", "restart", "{}" },
  { "SHUTDOWN", "POST", "shutdown", "{}" }
};
#pragma endregion

void setup() {
  Serial.begin(9600); 
  bootMillis = millis();
  
  pinMode(TRIG_PIN, OUTPUT);
  digitalWrite(TRIG_PIN, LOW);
  pinMode(ECHO_PIN, INPUT);

  connectToWifi(ssid, password);
  startMDNS(name);

  server.on("/config", HTTP_GET, handleGetConfig);
  server.on("/info", HTTP_GET, handleGetInfo);
  server.on("/distance", HTTP_GET, handleGetDistance);
  server.on("/restart", HTTP_POST, handleRestart);
  server.on("/shutdown", HTTP_POST, handleShutdown);
  registerNotFoundHandler(server);
  
  server.begin();
  Serial.println("Setup initialized...");
}

void loop() {
  MDNS.update();
  server.handleClient();
  delay(1); // Explicit yield to prevent background soft-WDT issues
}

void handleGetConfig() {
  server.send(200, "application/json", buildDeviceConfigJson(name, actions, sizeof(actions) / sizeof(actions[0])));
}

void handleGetInfo() {
  server.send(200, "application/json", buildDeviceInfoJson(name, bootMillis));
}

float getFilteredDistance() {
  const int SAMPLES = 5;
  float distances[SAMPLES];
  int validSamples = 0;

  for (int i = 0; i < SAMPLES; i++) {
    digitalWrite(TRIG_PIN, LOW);
    delayMicroseconds(2);
    digitalWrite(TRIG_PIN, HIGH);
    delayMicroseconds(10);
    digitalWrite(TRIG_PIN, LOW);

    // Timeout adjusted to 35000µs (~5.9 meters absolute maximum)
    long duration = pulseIn(ECHO_PIN, HIGH, 35000); 
    
    // Adjusted coefficient for ~+10°C environment in a well
    float dist = (duration * 0.03373) / 2.0;

    if (duration > 0 && dist >= 20.0 && dist <= 450.0) {
      distances[validSamples] = dist;
      validSamples++;
    }
    
    // Mandate a recovery delay regardless of whether pulseIn timed out or succeeded
    delay(80); 
  }

  if (validSamples == 0) return -1.0;

  float sum = 0;
  for (int i = 0; i < validSamples; i++) {
    sum += distances[i];
  }
  return sum / validSamples;
}

void handleGetDistance() {
  float distanceCm = getFilteredDistance();

  if (distanceCm < 0) {
    JsonDocument doc;
    doc["type"] = "https://tools.ietf.org/html/rfc7231#section-6.6.5";
    doc["title"] = "Well Sensor Error";
    doc["status"] = 504;
    doc["detail"] = "Failed to get stable ultrasonic echo from water surface. Check for condensation or wall interference.";
    doc["instance"] = "/distance";

    String out;
    serializeJson(doc, out);
    server.send(504, "application/problem+json", out);
  } else {
    JsonDocument doc;
    doc["distanceToWaterCm"] = distanceCm;

    String out;
    serializeJson(doc, out);
    server.send(200, "application/json", out);
  }
}

void handleRestart() {
  sendRestartResponse(server);
}

void handleShutdown() {
  sendShutdownResponse(server);
}

#pragma endregion