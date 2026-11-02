#include <WiFi.h>
#include <ArduinoJson.h>
#include <H4.h>
#include <Preferences.h>
#include <vector>
#include "Config.h"
#include "Bluetti.h"
#include <WifiHelper.h>
#include <DeviceApi.h>

const char* name = "Bluetti";

const DeviceAction statusAction = { "STATUS", "Get", "status" };
const DeviceAction pollingActions[] = {
  { "POLLING ON", "Post", "polling/on" },
  { "POLLING OFF", "Post", "polling/off" }
};
const DeviceAction acActions[] = {
  { "AC ON", "Post", "ac/on" },
  { "AC OFF", "Post", "ac/off" }
};
const DeviceAction dcActions[] = {
  { "DC ON", "Post", "dc/on" },
  { "DC OFF", "Post", "dc/off" }
};

const uint8_t acSwitch = 1;
const uint8_t dcSwitch = 2;

const uint32_t pollIntervalMs = 10000;
const uint32_t changeCheckIntervalMs = 1000;

H4 h4(115200);
MqttDevice device(name, nullptr, 0);
BluettiClient bluetti;
Preferences preferences;
std::vector<DeviceAction> actions;
// The switches of the power station, kept across restarts so the actions do not disappear until it is connected again.
uint8_t switches = 0;
String publishedStatus;

void h4setup() {
  connectToWifi(ssid, password);

  preferences.begin("bluetti");
  // Kept across restarts, so a restart at night does not start polling again.
  bluetti.begin(bluettiAddress, pollIntervalMs, preferences.getBool("polling", true));
  setSwitches(preferences.getUChar("switches", 0));

  device.onCommand(handleCommand);
  device.addTelemetry("status", [] {
    publishedStatus = readStatus();
    return publishedStatus;
  });
  device.begin({ mqttHost, mqttPort, mqttUsername, mqttPassword });

  // The power station is polled in its own task, so the status is published from here as soon as it changes.
  h4.every(changeCheckIntervalMs, [] {
    BluettiStatus status = bluetti.status();
    uint8_t current = (status.hasAcSwitch ? acSwitch : 0) | (status.hasDcSwitch ? dcSwitch : 0);
    if (status.model.length() > 0 && current != switches) {
      preferences.putUChar("switches", current);
      setSwitches(current);
    }

    if (readStatus() != publishedStatus) {
      device.publishTelemetry("status");
    }
  });
}

void setSwitches(uint8_t value) {
  switches = value;
  actions = { statusAction };
  actions.insert(actions.end(), std::begin(pollingActions), std::end(pollingActions));
  if (switches & acSwitch) {
    actions.insert(actions.end(), std::begin(acActions), std::end(acActions));
  }

  if (switches & dcSwitch) {
    actions.insert(actions.end(), std::begin(dcActions), std::end(dcActions));
  }

  device.setActions(actions.data(), actions.size());
}

#pragma region Commands
CommandResult handleCommand(const String& command) {
  int separator = command.indexOf('/');
  String output = command.substring(0, separator);
  String state = command.substring(separator + 1);
  if (separator < 0 || (output != "polling" && output != "ac" && output != "dc") || (state != "on" && state != "off")) {
    return { false, "Unknown command '" + command + "'." };
  }

  if (output == "polling") {
    bool isPolling = state == "on";
    preferences.putBool("polling", isPolling);
    bluetti.setPolling(isPolling);
    return { true, "Polling " + state };
  }

  String error;
  if (!bluetti.setOutput(output == "ac" ? BluettiOutput::Ac : BluettiOutput::Dc, state == "on", error)) {
    return { false, error };
  }

  return { true, output + " output " + state };
}

String readStatus() {
  BluettiStatus status = bluetti.status();

  JsonDocument doc;
  doc["polling"] = status.isPolling;
  doc["connected"] = status.isConnected;
  if (status.model.length() > 0) {
    doc["model"] = status.model;
    doc["serialNumber"] = status.serialNumber;
    doc["protocol"] = status.protocolVersion;
    doc["encrypted"] = status.isEncrypted;
  }

  if (status.armVersion.length() > 0) {
    doc["armVersion"] = status.armVersion;
    doc["dspVersion"] = status.dspVersion;
  }

  setNumber(doc, "batteryPercent", status.batteryPercent);
  setNumber(doc, "acInputPower", status.acInputPower);
  setNumber(doc, "dcInputPower", status.dcInputPower);
  setNumber(doc, "acOutputPower", status.acOutputPower);
  setNumber(doc, "dcOutputPower", status.dcOutputPower);
  setDecimal(doc, "acOutputVoltage", status.acOutputVoltage);
  setDecimal(doc, "acOutputFrequency", status.acOutputFrequency);
  setDecimal(doc, "timeRemaining", status.timeRemaining);
  for (size_t i = 0; i < 2; i++) {
    const BluettiSolarInput& input = status.solarInputs[i];
    if (input.power >= 0) {
      JsonObject solar = doc["solar" + String(i + 1)].to<JsonObject>();
      solar["power"] = input.power;
      solar["voltage"] = serialized(String(input.voltage, 1));
      solar["current"] = serialized(String(input.current, 1));
    }
  }

  if (status.powerGeneration >= 0) {
    doc["powerGeneration"] = status.powerGeneration;
  }

  if (status.acOutputOn >= 0) {
    doc["acOutput"] = status.acOutputOn ? "on" : "off";
  }

  if (status.dcOutputOn >= 0) {
    doc["dcOutput"] = status.dcOutputOn ? "on" : "off";
  }

  if (status.error.length() > 0) {
    doc["error"] = status.error;
  }

  String json;
  serializeJson(doc, json);
  return json;
}

void setNumber(JsonDocument& doc, const char* key, int value) {
  if (value >= 0) {
    doc[key] = value;
  }
}

// One decimal, the precision of the registers.
void setDecimal(JsonDocument& doc, const char* key, float value) {
  if (value >= 0) {
    doc[key] = serialized(String(value, 1));
  }
}
#pragma endregion Commands
