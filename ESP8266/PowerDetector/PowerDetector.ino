#include <ESP8266WiFi.h>
#include <H4.h>
#include "Config.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

#pragma region Variables
const char* name = "power-detector";

const DeviceAction actions[] = {};

H4 h4(9600);
MqttDevice device(name, actions, 0);
#pragma endregion Variables

void h4setup() {
  connectToWifi(ssid, password);

  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });
}
