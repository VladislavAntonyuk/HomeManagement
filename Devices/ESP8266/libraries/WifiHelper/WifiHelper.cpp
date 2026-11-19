#include "WifiHelper.h"

#if defined(ARDUINO_ARCH_ESP32)
#include <WiFi.h>
#include <ESPmDNS.h>
#else
#include <ESP8266WiFi.h>
#include <ESP8266mDNS.h>
#endif

void connectToWifi(const char* ssid, const char* password, unsigned long timeoutMs) {
  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid, password);
  Serial.print("Connecting to Wi-Fi");

  unsigned long startAttemptTime = millis();

  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");

    if (timeoutMs > 0 && millis() - startAttemptTime > timeoutMs) {
      Serial.println("\nWi-Fi connection timeout. Proceeding to loop initialization...");
      break;
    }
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.printf("\nConnected. IP: %s\n", WiFi.localIP().toString().c_str());
  }
}

void startMDNS(const char* name) {
  if (MDNS.begin(name)) {
    MDNS.addService("http", "tcp", 80);
  }
}
