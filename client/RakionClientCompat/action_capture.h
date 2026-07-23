#pragma once

#include <cstdint>

bool IsActionCaptureEnabled();
void CapturePeerAction(uint16_t type, const void* payload, uint16_t payloadLength);
void CapturePlayerAction(const void* source, const void* action);
