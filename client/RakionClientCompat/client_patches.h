#pragma once

#include <cstdint>

bool ApplyFinalClientPatches();
bool TryGetCurrentFieldSeat(uint8_t& seat);
bool ApplyLauncherPatches();
void PatchKeyHook();
