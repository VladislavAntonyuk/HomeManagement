#include <Wire.h>
#include <Adafruit_AHTX0.h>
#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266mDNS.h>
#include <ArduinoJson.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

const char* name = "Temperature And Humidity";
float currentTemperature = 0.0;
float currentHumidity = 0.0;

ESP8266WebServer server(80);
unsigned long bootMillis = 0;

const DeviceAction actions[] = {
  { "INFO", "GET", "info", "" },
  { "STATUS", "GET", "status", "" },
  { "RESTART", "POST", "restart", "{}" },
  { "SHUTDOWN", "POST", "shutdown", "{}" }
};

// Конфігурація апаратної шини (Датчик AHT10)
#define AHT_SDA 4  // D2
#define AHT_SCL 5  // D1

Adafruit_AHTX0 aht;

void setup() {
  Serial.begin(9600);
  bootMillis = millis();

  connectToWifi(ssid, password);
  startMDNS(name);

  server.on("/config", HTTP_GET, handleGetConfig);
  server.on("/info", HTTP_GET, handleGetInfo);
  server.on("/status", HTTP_GET, handleGetStatus);
  server.on("/restart", HTTP_POST, handleRestart);
  server.on("/shutdown", HTTP_POST, handleShutdown);
  registerNotFoundHandler(server);

  server.begin();

  // Ініціалізація AHT10 на апаратній шині
  Wire.begin(AHT_SDA, AHT_SCL);
  if (!aht.begin()) {
    Serial.println(F("Помилка AHT10 на D2/D1"));
    while (1) delay(10);
  }
}

void loop() {
  MDNS.update();
  server.handleClient();
}

void readSensorData() {
  sensors_event_t humidity, temp;
  aht.getEvent(&humidity, &temp);

  currentTemperature = temp.temperature;
  currentHumidity = humidity.relative_humidity;
}

void handleGetStatus() {
  readSensorData();

  JsonDocument doc;
  doc["temperature"] = currentTemperature;
  doc["humidity"] = currentHumidity;

  String out;
  serializeJson(doc, out);
  server.send(200, "application/json", out);
}

void handleGetConfig() {
  server.send(200, "application/json", buildDeviceConfigJson(name, actions, sizeof(actions) / sizeof(actions[0])));
}

void handleGetInfo() {
  server.send(200, "application/json", buildDeviceInfoJson(name, bootMillis));
}

void handleRestart() {
  sendRestartResponse(server);
}

void handleShutdown() {
  sendShutdownResponse(server);
}

