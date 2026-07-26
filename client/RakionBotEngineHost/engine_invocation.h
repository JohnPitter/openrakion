#pragma once

#include "engine_abi.h"

namespace bot_engine
{
bool StartPeerWithStreamFaults(
    StartPeerToPeer startPeer,
    StreamExceptionFilter exceptionFilter,
    void* network,
    const LegacyString* session,
    const LegacyString* world,
    void* sessionProperties);

void* CreateGameWithStreamFaults(
    CreateGame createGame,
    StreamExceptionFilter exceptionFilter);

bool InitializeGameWithStreamFaults(
    InitializeGame initialize,
    StreamExceptionFilter exceptionFilter,
    void* game,
    const LegacyString* fileName);

bool AdvanceEngineSafely(
    EngineStep handleTimerHandlers,
    void* timer,
    EngineStep mainLoop,
    void* network);
}
