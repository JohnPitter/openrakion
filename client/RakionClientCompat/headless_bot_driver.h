#pragma once

#include <windows.h>

bool IsHeadlessBotDriverEnabled();
bool IsHeadlessGameplayReady();
bool InstallHeadlessRemotePlayerTrace(HMODULE engine);
void QueueHeadlessPeerPacket(const void* packet, unsigned short size);
void ApplyHeadlessBotAction(const void* source, void* action);
void PumpHeadlessBotAction();
