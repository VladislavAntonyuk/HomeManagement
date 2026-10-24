#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266mDNS.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

const char* name = "ChickenDoor";

ESP8266WebServer server(80);
Preferences preferences;

unsigned long bootMillis = 0;

const DeviceAction actions[] = {
  { "INFO", "GET", "info", "" },
  { "STATUS", "GET", "status", "" },
  { "OPEN", "POST", "open", "{}" },
  { "CLOSE", "POST", "close", "{}" },
  { "RESTART", "POST", "restart", "{}" },
  { "SHUTDOWN", "POST", "shutdown", "{}" }
};

const int RELAY_OPEN_PIN = 14; // D5 на платі це GPIO14

const unsigned long WORK_TIME_MS = 5000;

const int MOTOR_ON = LOW;
const int MOTOR_OFF = HIGH;

bool isOpened = false;
bool isMotorRunning = false;
unsigned long motorStartTime = 0;
int pendingState = -1; // 0: none, -1: closing, 1: opening

void saveState(bool state) {
  isOpened = state;
  preferences.begin("door", false);
  preferences.putBool("isOpened", isOpened);
  preferences.end();
}

void stopMotor() {
  digitalWrite(RELAY_OPEN_PIN, MOTOR_OFF);
  isMotorRunning = false;
  pendingState = 0;
  Serial.println("Мотор зупинено.");
}

void triggerOpen() {
  if (isOpened || (isMotorRunning && pendingState == 1)) {
    server.send(400, "application/json", "{\"error\":\"Door is already open or currently opening\"}");
    return;
  }

  if (isMotorRunning) stopMotor();

  Serial.println("Запуск: ВІДКРИТТЯ дверей...");
  digitalWrite(RELAY_OPEN_PIN, MOTOR_ON);
  
  isMotorRunning = true;
  motorStartTime = millis();
  pendingState = 1;

  server.send(200, "application/json", "{\"status\":\"Opening\"}");
}

void triggerClose() {
  if (!isOpened && !isMotorRunning) {
    server.send(400, "application/json", "{\"error\":\"Door is already closed\"}");
    return;
  }
  if (isMotorRunning && pendingState == -1) {
    server.send(400, "application/json", "{\"error\":\"Door is currently closing\"}");
    return;
  }

  if (isMotorRunning) stopMotor();

  Serial.println("Запуск: ЗАКРИТТЯ дверей...");

  isMotorRunning = true;
  motorStartTime = millis() + WORK_TIME_MS;
  pendingState = -1;

  server.send(200, "application/json", "{\"status\":\"Closing\"}");
}

void handleGetStatus() {
  JsonDocument doc;
  doc["isOpened"] = isOpened;
  doc["isMoving"] = isMotorRunning;

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
  stopMotor();
  sendRestartResponse(server);
}

void handleShutdown() {
  stopMotor();
  sendShutdownResponse(server);
}

void setup() {
  Serial.begin(9600);

  pinMode(RELAY_OPEN_PIN, OUTPUT);
  stopMotor();

  // Load saved state from NVS flash
  preferences.begin("door", true); // readonly = true
  isOpened = preferences.getBool("isOpened", false);
  preferences.end();

  bootMillis = millis();

  connectToWifi(ssid, password);

  startMDNS(name);

  server.on("/info", HTTP_GET, handleGetInfo);
  server.on("/config", HTTP_GET, handleGetConfig);
  server.on("/status", HTTP_GET, handleGetStatus);
  server.on("/open", HTTP_POST, triggerOpen);
  server.on("/close", HTTP_POST, triggerClose);
  server.on("/restart", HTTP_POST, handleRestart);
  server.on("/shutdown", HTTP_POST, handleShutdown);

  registerNotFoundHandler(server);

  server.begin();
  Serial.println("HTTP сервер запущено.");
}

void loop() {
  MDNS.update();
  server.handleClient();

  if (isMotorRunning && (millis() - motorStartTime >= WORK_TIME_MS)) {
    if (pendingState == 1) {
      saveState(true);
    } else if (pendingState == -1) {
      saveState(false);
    }

    stopMotor();
    Serial.println("Операцію завершено за таймером. Стан збережено.");
  }
}