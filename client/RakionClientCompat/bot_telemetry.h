#pragma once

#include <cstdint>

bool LoadServerAddress();
bool InstallInitialServerRedirect();
bool InstallBotTelemetryHook();
void __stdcall ReportBotHit(uint8_t botSeat);
