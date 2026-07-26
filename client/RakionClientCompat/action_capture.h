#pragma once

#include <cstdint>

bool IsActionCaptureEnabled();
void CapturePeerAction(uint16_t type, const void* payload, uint16_t payloadLength);
void CapturePlayerAction(const void* source, const void* action);
void CaptureRemotePlayerAction(const void* action);
void CaptureProviderPacket(uint16_t targetPort, const void* payload, uint16_t payloadLength);
void CaptureInboundPacket(uint16_t sourcePort, const void* payload, uint16_t payloadLength);
