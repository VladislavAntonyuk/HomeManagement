#include <Wire.h>
#include <Adafruit_AHTX0.h>
#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <H4.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

const char* name = "Temperature And Humidity";

const DeviceAction actions[] = {
  { "STATUS", "Get", "status" }
};

H4 h4(9600);
MqttDevice device(name, actions, sizeof(actions) / sizeof(actions[0]));

// Конфігурація апаратної шини (Датчик AHT10)
#define AHT_SDA 4  // D2
#define AHT_SCL 5  // D1

Adafruit_AHTX0 aht;
bool isSensorReady = false;

void h4setup() {
  connectToWifi(ssid, password);

  // Ініціалізація AHT10 на апаратній шині
  Wire.begin(AHT_SDA, AHT_SCL);
  isSensorReady = aht.begin();
  if (!isSensorReady) {
    Serial.println(F("Помилка AHT10 на D2/D1"));
  }

  device.addTelemetry("status", readStatus);
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });
}

String readStatus() {
  JsonDocument doc;
  if (isSensorReady) {
    sensors_event_t humidity, temp;
    aht.getEvent(&humidity, &temp);

    doc["temperature"] = temp.temperature;
    doc["humidity"] = humidity.relative_humidity;
  } else {
    // The device stays reachable, so the error shows on the Devices page instead of the device going silent.
    doc["error"] = "AHT10 not found on D2/D1";
  }

  String out;
  serializeJson(doc, out);
  return out;
}
