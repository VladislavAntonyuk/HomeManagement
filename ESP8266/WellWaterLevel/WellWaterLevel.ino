#include <ESP8266WiFi.h>
#include <ArduinoJson.h>
#include <H4.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

const int TRIG_PIN = 4; // D2 (GPIO4)
const int ECHO_PIN = 5; // D1 (GPIO5)

#pragma region Configuration
const char* name = "WellWaterLevel";
#pragma endregion

#pragma region Globals
const DeviceAction actions[] = {
  { "Distance", "Get", "distance" }
};

H4 h4(9600);
MqttDevice device(name, actions, sizeof(actions) / sizeof(actions[0]));
#pragma endregion

void h4setup() {
  pinMode(TRIG_PIN, OUTPUT);
  digitalWrite(TRIG_PIN, LOW);
  pinMode(ECHO_PIN, INPUT);

  connectToWifi(ssid, password);

  device.addTelemetry("distance", readDistance);
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });
  Serial.println("Setup initialized...");
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

String readDistance() {
  float distanceCm = getFilteredDistance();

  JsonDocument doc;
  if (distanceCm < 0) {
    doc["title"] = "Well Sensor Error";
    doc["detail"] = "Failed to get stable ultrasonic echo from water surface. Check for condensation or wall interference.";
  } else {
    doc["distanceToWaterCm"] = distanceCm;
  }

  String out;
  serializeJson(doc, out);
  return out;
}
