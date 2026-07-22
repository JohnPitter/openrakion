#pragma once

#include <cstdint>

bool LoadServerAddress();
bool InstallInitialServerRedirect();
bool InstallBotTelemetryHook();
bool TryGetWorldLocalPort(uint16_t& port);
