#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <H4.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

const char* name = "ChickenDoor";

Preferences preferences;

const DeviceAction actions[] = {
  { "STATUS", "Get", "status" },
  { "OPEN", "Post", "open" },
  { "CLOSE", "Post", "close" }
};

H4 h4(9600);
MqttDevice device(name, actions, sizeof(actions) / sizeof(actions[0]));

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

CommandResult triggerOpen() {
  if (isOpened || (isMotorRunning && pendingState == 1)) {
    return { false, "Door is already open or currently opening" };
  }

  if (isMotorRunning) stopMotor();

  Serial.println("Запуск: ВІДКРИТТЯ дверей...");
  digitalWrite(RELAY_OPEN_PIN, MOTOR_ON);

  isMotorRunning = true;
  motorStartTime = millis();
  pendingState = 1;

  device.publishTelemetry("status");
  return { true, "Opening" };
}

CommandResult triggerClose() {
  if (!isOpened && !isMotorRunning) {
    return { false, "Door is already closed" };
  }
  if (isMotorRunning && pendingState == -1) {
    return { false, "Door is currently closing" };
  }

  if (isMotorRunning) stopMotor();

  Serial.println("Запуск: ЗАКРИТТЯ дверей...");

  isMotorRunning = true;
  motorStartTime = millis() + WORK_TIME_MS;
  pendingState = -1;

  device.publishTelemetry("status");
  return { true, "Closing" };
}

CommandResult handleCommand(const String& command) {
  if (command == "open") {
    return triggerOpen();
  }

  if (command == "close") {
    return triggerClose();
  }

  return { false, "Unknown command '" + command + "'." };
}

String readStatus() {
  JsonDocument doc;
  doc["isOpened"] = isOpened;
  doc["isMoving"] = isMotorRunning;

  String out;
  serializeJson(doc, out);
  return out;
}

void checkMotor() {
  if (isMotorRunning && (millis() - motorStartTime >= WORK_TIME_MS)) {
    if (pendingState == 1) {
      saveState(true);
    } else if (pendingState == -1) {
      saveState(false);
    }

    stopMotor();
    Serial.println("Операцію завершено за таймером. Стан збережено.");
    device.publishTelemetry("status");
  }
}

void h4setup() {
  pinMode(RELAY_OPEN_PIN, OUTPUT);
  stopMotor();

  // Load saved state from NVS flash
  preferences.begin("door", true); // readonly = true
  isOpened = preferences.getBool("isOpened", false);
  preferences.end();

  connectToWifi(ssid, password);

  device.onCommand(handleCommand);
  device.addTelemetry("status", readStatus);
  device.onPowerOff(stopMotor);
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });

  h4.every(50, checkMotor);
  Serial.println("Пристрій запущено.");
}
