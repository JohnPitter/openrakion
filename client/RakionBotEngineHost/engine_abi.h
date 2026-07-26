#pragma once

#include <windows.h>

namespace bot_engine
{
struct LegacyString
{
    void* value{};
    long metadata{};
};

static_assert(sizeof(LegacyString) == 8);

constexpr char EngineModuleName[] = "engine.dll";
constexpr char EntitiesModuleName[] = "entitiesmp.dll";
constexpr char GameModuleName[] = "gamemp.dll";
constexpr char DedicatedServerSymbol[] = "?_bDedicatedServer@@3HA";
constexpr char NetworkSymbol[] = "?_pNetwork@@3PAVCNetworkLibrary@@A";
constexpr char TimerSymbol[] = "?_pTimer@@3PAVCTimer@@A";
constexpr char StringConstructorSymbol[] = "??0CTString@@QAE@PBD@Z";
constexpr char InitEngineSymbol[] = "?SE_InitEngine@@YAXVCTString@@@Z";
constexpr char EndEngineSymbol[] = "?SE_EndEngine@@YAXXZ";
constexpr char StartPeerToPeerSymbol[] =
    "?StartPeerToPeer_t@CNetworkLibrary@@QAEXABVCTString@@"
    "ABVCTFileName@@KJHPAX@Z";
constexpr char StopGameSymbol[] = "?StopGame@CNetworkLibrary@@QAEXXZ";
constexpr char EnableStreamHandlingSymbol[] =
    "?EnableStreamHandling@CTStream@@SAXXZ";
constexpr char DisableStreamHandlingSymbol[] =
    "?DisableStreamHandling@CTStream@@SAXXZ";
constexpr char StreamExceptionFilterSymbol[] =
    "?ExceptionFilter@CTStream@@SAHKPAU_EXCEPTION_POINTERS@@@Z";
constexpr char CreateGameSymbol[] = "?GAME_Create@@YAPAVCGame@@XZ";
constexpr char DestroyGameSymbol[] = "?GAME_Destroy@@YAXXZ";
constexpr char EntitiesInstanceSymbol[] =
    "?getInstance@CEntitiesDLL@@SAAAV1@XZ";
constexpr char EntitiesLoadSymbol[] =
    "?loadDLL@CEntitiesDLL@@QAEXVCTFileName@@@Z";
constexpr char EntitiesHandleSymbol[] =
    "?getDLL@CEntitiesDLL@@QAEPAUHINSTANCE__@@XZ";

using StringConstructor = void*(__thiscall*)(void*, const char*);
using StringDestructor = void(__thiscall*)(void*);
using InitEngine = void(__cdecl*)(LegacyString);
using EndEngine = void(__cdecl*)();
using StartPeerToPeer = void(__thiscall*)(
    void*,
    const LegacyString*,
    const LegacyString*,
    unsigned long,
    long,
    int,
    void*);
using StopGame = void(__thiscall*)(void*);
using StreamHandling = void(__cdecl*)();
using StreamExceptionFilter = int(__cdecl*)(DWORD, EXCEPTION_POINTERS*);
using CreateGame = void*(__cdecl*)();
using DestroyGame = void(__cdecl*)();
using GetEntitiesInstance = void*(__cdecl*)();
using LoadEntitiesPackage = void(__thiscall*)(void*, LegacyString);
using GetEntitiesHandle = HMODULE(__thiscall*)(void*);
}
