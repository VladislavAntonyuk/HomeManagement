#pragma once

void connectToWifi(const char* ssid, const char* password, unsigned long timeoutMs = 15000);
void startMDNS(const char* name);
