#pragma once

#include <cstdint>

bool LoadServerAddress();
bool InstallInitialServerRedirect();
bool InstallBotTelemetryHook();
bool TryGetServerAddress(uint32_t& address);
bool TryGetWorldLocalPort(uint16_t& port);
