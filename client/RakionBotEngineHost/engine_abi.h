#pragma once

#include <windows.h>

#include <cstddef>

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
constexpr char WorldNetworkSymbol[] =
    "?_pRakionWorldNet@@3PAVIScavengerWorldNet@@A";
constexpr char StringConstructorSymbol[] = "??0CTString@@QAE@PBD@Z";
constexpr char StringDestructorSymbol[] = "??1CTString@@QAE@XZ";
constexpr char FileNameConstructorSymbol[] =
    "??0CTFileName@@QAE@ABVCTString@@@Z";
constexpr char FileNameDestructorSymbol[] = "??1CTFileName@@QAE@XZ";
constexpr char InitEngineSymbol[] = "?SE_InitEngine@@YAXVCTString@@@Z";
constexpr char EndEngineSymbol[] = "?SE_EndEngine@@YAXXZ";
constexpr char StartPeerToPeerSymbol[] =
    "?StartPeerToPeer_t@CNetworkLibrary@@QAEXABVCTString@@"
    "ABVCTFileName@@KJHPAX@Z";
constexpr char StopGameSymbol[] = "?StopGame@CNetworkLibrary@@QAEXXZ";
constexpr char NetworkMainLoopSymbol[] =
    "?MainLoop@CNetworkLibrary@@QAEXXZ";
constexpr char TimerHandlersSymbol[] =
    "?HandleTimerHandlers@CTimer@@QAEXXZ";
constexpr char EnableStreamHandlingSymbol[] =
    "?EnableStreamHandling@CTStream@@SAXXZ";
constexpr char DisableStreamHandlingSymbol[] =
    "?DisableStreamHandling@CTStream@@SAXXZ";
constexpr char StreamExceptionFilterSymbol[] =
    "?ExceptionFilter@CTStream@@SAHKPAU_EXCEPTION_POINTERS@@@Z";
constexpr char CreateGameSymbol[] = "?GAME_Create@@YAPAVCGame@@XZ";
constexpr char DestroyGameSymbol[] = "?GAME_Destroy@@YAXXZ";
constexpr char WorldNetworkConstructorSymbol[] =
    "??0IScavengerWorldNet@@QAE@XZ";
constexpr char WorldNetworkDestructorSymbol[] =
    "??1IScavengerWorldNet@@QAE@XZ";
constexpr char FieldInfoConstructorSymbol[] = "??0FieldInfo@@QAE@XZ";
constexpr char FieldInfoDestructorSymbol[] = "??1FieldInfo@@QAE@XZ";
constexpr char InitializeWorldNetworkSymbol[] =
    "?InitWorldNetLib@@YAEPAVIScavengerWorldNet@@@Z";
constexpr char DestroyWorldNetworkSymbol[] = "?DestroyWorldNetLib@@YAXXZ";
constexpr char PlayerConstructorSymbol[] =
    "??0CPlayerCharacter@@QAE@ABVCTString@@0@Z";
constexpr char PlayerDestructorSymbol[] = "??1CPlayerCharacter@@QAE@XZ";
constexpr char AddPlayerSymbol[] =
    "?AddPlayer_t@CNetworkLibrary@@QAEPAVCPlayerSource@@"
    "AAVCPlayerCharacter@@@Z";
constexpr char GetLocalPlayerCountSymbol[] =
    "?GetLocalPlayerCount@CNetworkLibrary@@QAEJXZ";
constexpr char GetLocalPlayerEntitySymbol[] =
    "?GetLocalPlayerEntity@CNetworkLibrary@@QAEPAVCEntity@@"
    "PAVCPlayerSource@@@Z";
constexpr char GetPlacementSymbol[] =
    "?GetPlacement@CEntity@@QBEABVCPlacement3D@@XZ";
constexpr char SetPlacementSymbol[] =
    "?SetPlacement@CEntity@@QAEXABVCPlacement3D@@@Z";
constexpr char DirectionVectorToAnglesSymbol[] =
    "?DirectionVectorToAngles@@YAXABV?$Vector@M$02@@AAV1@@Z";
constexpr char IsAliveSymbol[] = "?IsAlive@CPlayer@@QAEHXZ";
constexpr char GetHpSymbol[] = "?GetHP@CPlayer@@UAEMXZ";
constexpr char SetAliveSymbol[] = "?SetAlive@CPlayer@@QAEXXZ";
constexpr char SetDeadSymbol[] = "?SetDead@CPlayer@@QAEXXZ";
constexpr char ExecDamageAnimSymbol[] =
    "?ExecDamageAnim@CPlayer@@QAEXJJH@Z";
constexpr char ApplyActionSymbol[] =
    "?ApplyAction@CPlayerSource@@QAEXAAH@Z";
constexpr char SendActionSymbol[] =
    "?SendAction@CPlayerSource@@QAEXXZ";
// Primitivas de simulação: o driver de tick da SE1 está stubado no binário do
// Rakion, então o host dirige estas diretamente.
constexpr char SessionHandleTimersSymbol[] =
    "?HandleTimers@CSessionState@@QAEXM@Z";
constexpr char SessionHandleMoversSymbol[] =
    "?HandleMovers@CSessionState@@QAEXXZ";
constexpr char TimerSetCurrentTickSymbol[] = "?SetCurrentTick@CTimer@@QAEXM@Z";
constexpr char TimerGetCurrentTickSymbol[] =
    "?GetCurrentTick@CTimer@@QBE?BMXZ";
constexpr char TimerQuantumSymbol[] = "?TickQuantum@CTimer@@2MB";
// Ponteiro do session state dentro do CNetworkLibrary, lido do IsPaused
// (mov eax,[ecx+0x24]; mov eax,[eax+0x70]).
constexpr std::size_t NetworkSessionStateOffset = 0x24;
// Aplicador do lado da ENTIDADE. O caminho fonte → servidor → game stream da SE1
// está stubado no Rakion, então o host entrega a ação diretamente ao CPlayer.
constexpr char PlayerApplyActionSymbol[] =
    "?ApplyAction@CPlayer@@UAEXABVCPlayerAction@@MAAH@Z";
// Handler por tick do player ativo: lê a translação da ação e chama
// CMovableEntity::SetDesiredTranslation. É o que efetivamente move o personagem.
constexpr char PlayerActiveActionsSymbol[] =
    "?ActiveActions@CPlayer@@QAEXABVCPlayerAction@@@Z";
// Driver de animação por tick: recebe a ação e liga as flags de modo de movimento
// (CPlayerAnimator+0x12C/0x134/0x13C/0x140/0x144/0x150) que o ActiveActions
// consulta antes de produzir translação.
constexpr char PlayerAnimatorSymbol[] =
    "?GetPlayerAnimator@CPlayer@@QAEPAVCPlayerAnimator@@XZ";
constexpr char PlayerAnimateSymbol[] =
    "?AnimatePlayer@CPlayerAnimator@@QAEXABVCPlayerAction@@@Z";
constexpr char EntitiesInstanceSymbol[] =
    "?getInstance@CEntitiesDLL@@SAAAV1@XZ";
constexpr char EntitiesLoadSymbol[] =
    "?loadDLL@CEntitiesDLL@@QAEXVCTFileName@@@Z";
constexpr char EntitiesHandleSymbol[] =
    "?getDLL@CEntitiesDLL@@QAEPAUHINSTANCE__@@XZ";

using StringConstructor = void*(__thiscall*)(void*, const char*);
using StringDestructor = void(__thiscall*)(void*);
using FileNameConstructor = void*(__thiscall*)(
    void*,
    const LegacyString*);
using FileNameDestructor = void(__thiscall*)(void*);
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
using EngineStep = void(__thiscall*)(void*);
using StreamHandling = void(__cdecl*)();
using StreamExceptionFilter = int(__cdecl*)(DWORD, EXCEPTION_POINTERS*);
using CreateGame = void*(__cdecl*)();
using DestroyGame = void(__cdecl*)();
using WorldNetworkConstructor = void*(__thiscall*)(void*);
using WorldNetworkDestructor = void(__thiscall*)(void*);
using FieldInfoConstructor = void*(__thiscall*)(void*);
using FieldInfoDestructor = void(__thiscall*)(void*);
using InitializeWorldNetwork = void(__cdecl*)(void*);
using DestroyWorldNetwork = void(__cdecl*)();
using PlayerConstructor = void*(__thiscall*)(
    void*,
    const LegacyString*,
    const LegacyString*);
using PlayerDestructor = void(__thiscall*)(void*);
using AddPlayer = void*(__thiscall*)(void*, void*);
using GetLocalPlayerCount = long(__thiscall*)(void*);
using GetLocalPlayerEntity = void*(__thiscall*)(void*, void*);
using GetPlacement = const float*(__thiscall*)(const void*);
using SetPlacement = void(__thiscall*)(void*, const float*);
using DirectionVectorToAngles = void(__cdecl*)(const float*, float*);
using IsAlive = int(__thiscall*)(void*);
using GetHp = float(__thiscall*)(void*);
using SetLifecycle = void(__thiscall*)(void*);
using ExecDamageAnim = void(__thiscall*)(void*, long, long, int);
using ApplyAction = void(__thiscall*)(void*, int&);
using SendAction = void(__thiscall*)(void*);
using InitializeGame = void(__thiscall*)(
    void*,
    const LegacyString*,
    int);
using GetEntitiesInstance = void*(__cdecl*)();
using LoadEntitiesPackage = void(__thiscall*)(void*, LegacyString);
using GetEntitiesHandle = HMODULE(__thiscall*)(void*);
using SessionTick = void(__thiscall*)(void*, float);
using TimerQuery = float(__thiscall*)(const void*);
using PlayerApplyAction = void(__thiscall*)(void*, const void*, float, int&);
using PlayerActionHandler = void(__thiscall*)(void*, const void*);
using PlayerAnimatorGetter = void*(__thiscall*)(void*);
}
