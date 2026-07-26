#include "engine_invocation.h"

namespace bot_engine
{
bool StartPeerWithStreamFaults(
    StartPeerToPeer startPeer,
    StreamExceptionFilter exceptionFilter,
    void* network,
    const LegacyString* session,
    const LegacyString* world,
    void* sessionProperties)
{
    __try
    {
        startPeer(
            network,
            session,
            world,
            0xFFFFFFFFUL,
            20,
            0,
            sessionProperties);
        return true;
    }
    __except (
        exceptionFilter(GetExceptionCode(), GetExceptionInformation()))
    {
        return false;
    }
}

void* CreateGameWithStreamFaults(
    CreateGame createGame,
    StreamExceptionFilter exceptionFilter)
{
    __try
    {
        return createGame();
    }
    __except (
        exceptionFilter(GetExceptionCode(), GetExceptionInformation()))
    {
        return nullptr;
    }
}

bool InitializeGameWithStreamFaults(
    InitializeGame initialize,
    StreamExceptionFilter exceptionFilter,
    void* game,
    const LegacyString* fileName)
{
    __try
    {
        initialize(game, fileName, 0);
        return true;
    }
    __except (
        exceptionFilter(GetExceptionCode(), GetExceptionInformation()))
    {
        return false;
    }
}
}
