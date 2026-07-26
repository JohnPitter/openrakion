#pragma once

#include <cstdint>

bool LoadServerAddress();
bool InstallInitialServerRedirect();
bool InstallBotTelemetryHook();
bool TryGetServerAddress(uint32_t& address);
bool TryGetWorldLocalPort(uint16_t& port);
bool TryGetPeerToPeerPort(uint16_t& port);
bool EnsureWorldUdpHandshake(
    uint16_t networkSlot, uint32_t sessionKey, uint16_t advertisedPort);
