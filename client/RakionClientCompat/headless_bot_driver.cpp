#include <windows.h>
#include <cmath>
#include <cstdio>
#include <cstring>

#include "compat_log.h"
#include "headless_bot_driver.h"
#include "headless_navigation.h"

namespace
{
constexpr size_t ButtonsOffset = 0x10;
constexpr size_t StrafeAxisOffset = 0x38;
constexpr size_t ForwardAxisOffset = 0x40;
constexpr size_t ActionStateOffset = 0x44;
constexpr size_t SourceActionOffset = 0x58;
constexpr size_t DesiredTranslationOffset = 0x120;
constexpr size_t PlayerActionOffset = 0xBB0;
constexpr size_t PlayerAnimatorOffset = 0x490;
constexpr size_t SpawnFreezeTimerOffset = 0x27DC;
constexpr BYTE PrimaryAttackButton = 0x01;
constexpr DWORD JumpButton = 0x00000004;
constexpr DWORD MoveForwardButton = 0x00000020;
constexpr DWORD MoveBackwardButton = 0x00100000;
constexpr DWORD MoveLeftButton = 0x08000000;
constexpr DWORD MoveRightButton = 0x10000000;
constexpr DWORD NavigationButtons =
    PrimaryAttackButton | JumpButton | MoveForwardButton |
    MoveBackwardButton | MoveLeftButton | MoveRightButton;
constexpr BYTE NormalActionState = 0;
constexpr BYTE AttackActionState = 1;
constexpr float MoveForwardAxis = -6.0f;
constexpr DWORD CycleMilliseconds = 6000;
constexpr DWORD MoveUntilMilliseconds = 3500;
constexpr DWORD AttackUntilMilliseconds = 3900;
constexpr DWORD SendIntervalMilliseconds = 100;
constexpr DWORD TargetingIntervalMilliseconds = 100;
constexpr DWORD AttackCycleMilliseconds = 1200;
constexpr DWORD AttackWindowMilliseconds = 350;
constexpr float MinimumAttackDistance = 1.25f;
constexpr float AttackRange = 3.25f;
constexpr uintptr_t AddRemotePlayerRva = 0x10E2B0;
constexpr uintptr_t HandleMessageRva = 0x10D7C0;
constexpr BYTE AddRemotePlayerPrologue[] = {
    0x81, 0xEC, 0xE8, 0x09, 0x00, 0x00};
constexpr char ApplyActionSymbol[] = "?ApplyAction@CPlayerSource@@QAEXAAH@Z";
constexpr char SendActionSymbol[] = "?SendAction@CPlayerSource@@QAEXXZ";
constexpr char NetworkSymbol[] = "?_pNetwork@@3PAVCNetworkLibrary@@A";
constexpr char WorldNetSymbol[] =
    "?_pRakionWorldNet@@3PAVIScavengerWorldNet@@A";
constexpr char GetLocalPlayerEntitySymbol[] =
    "?GetLocalPlayerEntity@CNetworkLibrary@@QAEPAVCEntity@@PAVCPlayerSource@@@Z";
constexpr char IsLocalEntitySymbol[] = "?IsLocalEntity@CEntity@@QAEHXZ";
constexpr char SetAsLocalEntitySymbol[] = "?SetAsLocalEntity@CEntity@@QAEXXZ";
constexpr char IsAliveSymbol[] = "?IsAlive@CPlayer@@QAEHXZ";
constexpr char GetHpSymbol[] = "?GetHP@CPlayer@@UAEMXZ";
constexpr char GetPlayerWeaponsSymbol[] =
    "?GetPlayerWeapons@CPlayer@@QAEPAVCPlayerWeapons@@XZ";
constexpr char UpdateWeaponHitSymbol[] =
    "?UpdateWeaponHit@CPlayerWeapons@@QAEXXZ";
constexpr char IsPlayerReadySymbol[] = "?IsPlayerReady@CPlayer@@QAEHXZ";
constexpr char CheckFreezeStateSymbol[] = "?CheckFreezeState@CPlayer@@QAEHXZ";
constexpr char GetFieldInfoSymbol[] = "?GetFieldInfo@CPlayer@@QAEPAVFieldInfo@@XZ";
constexpr char GetRoundStateSymbol[] = "?GetRoundState@FieldInfo@@QAE?AW4ERoundState@@XZ";
constexpr char SetRoundStateSymbol[] = "?SetRoundState@FieldInfo@@QAEXW4ERoundState@@@Z";
constexpr char GetNthPlayerEntitySymbol[] =
    "?GetNthPlayerEntity@CNetworkLibrary@@QAEPAVCPlayerEntity@@J@Z";
constexpr char GetPlayerEntitySymbol[] =
    "?GetPlayerEntity@CEntity@@SAPAV1@J@Z";
constexpr char GetPlacementSymbol[] = "?GetPlacement@CEntity@@QBEABVCPlacement3D@@XZ";
constexpr char SetPlacementSymbol[] = "?SetPlacement@CEntity@@QAEXABVCPlacement3D@@@Z";
constexpr char DirectionVectorToAnglesSymbol[] =
    "?DirectionVectorToAngles@@YAXABV?$Vector@M$02@@AAV1@@Z";
constexpr char IsFlagOnSymbol[] = "?IsFlagOn@CPlayer@@QAEHVCTString@@@Z";
constexpr char StringConstructorSymbol[] = "??0CTString@@QAE@PBD@Z";
constexpr char StringDestructorSymbol[] = "??1CTString@@QAE@XZ";
constexpr char NetMessageConstructorSymbol[] = "??0CNetMessage@@QAE@XZ";
constexpr char NetMessageDestructorSymbol[] = "??1CNetMessage@@QAE@XZ";
constexpr char NetMessageDataSymbol[] = "?GetData@CNetMessage@@QAEPAXXZ";
constexpr char NetMessageSizeSymbol[] = "?GatDataSize@CNetMessage@@QAEGXZ";
constexpr char NetMessageWriteSymbol[] = "?Write@CNetMessage@@QAEXQAXG@Z";
constexpr char PlayerInitDataSymbol[] =
    "?GetInitData@CPlayer@@UAEXAAVCNetMessage@@@Z";
constexpr char SendFieldGameAddPlayerSymbol[] =
    "?SendFieldGameAddPlayer@IScavengerWorldNet@@UAEXGPAD@Z";
volatile PVOID HeadlessPlayerSource{};
volatile PVOID HeadlessSessionState{};
volatile LONG HeadlessRemoteSpawnReady{};
volatile LONG HeadlessGameplayReady{};
uintptr_t AddRemotePlayerContinuation{};
bool HasBotTarget{};
float BotTargetDistance{};
HeadlessNavigationAction BotNavigationAction{};
SRWLOCK PeerPacketLock = SRWLOCK_INIT;

struct PeerPacket
{
    unsigned short Type{};
    unsigned short Size{};
    BYTE SourceSlot{};
    BYTE Payload[64]{};
};

PeerPacket PeerPackets[64]{};
size_t PeerPacketRead{};
size_t PeerPacketWrite{};

void __stdcall LogRemotePlayerEntry(
    const void* session, unsigned int seat, unsigned int size, const void* data)
{
    InterlockedExchangePointer(
        &HeadlessSessionState, const_cast<void*>(session));
    void* existing{};
    unsigned int sessionSeat = 0xFF;
    unsigned int fieldSeat = 0xFF;
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    auto** worldNet = engine
        ? reinterpret_cast<void**>(GetProcAddress(engine, WorldNetSymbol))
        : nullptr;
    if (seat < 16 && worldNet && *worldNet)
    {
        auto** vtable = *reinterpret_cast<void***>(*worldNet);
        using GetWorldFieldInfoFn = void*(__thiscall*)(void*);
        auto getFieldInfo = reinterpret_cast<GetWorldFieldInfoFn>(vtable[2]);
        void* fieldInfo = getFieldInfo(*worldNet);
        if (fieldInfo)
        {
            sessionSeat = *(static_cast<const BYTE*>(session) + 0x2946);
            fieldSeat = *(static_cast<const BYTE*>(fieldInfo) + 0x470C);
            existing = *reinterpret_cast<void**>(
                static_cast<BYTE*>(fieldInfo) + 0x4854 + seat * sizeof(void*));
        }
    }
    const LONG ready = InterlockedCompareExchange(
        &HeadlessRemoteSpawnReady, 0, 0);
    char message[224]{};
    std::snprintf(
        message,
        sizeof(message),
        "headless bot driver: AddRemotePlayer session=%p seat=%u localSession=%u "
        "localField=%u bytes=%u data=%p existente=%p pronto=%ld",
        session,
        seat,
        sessionSeat,
        fieldSeat,
        size,
        data,
        existing,
        ready);
    CompatLog(message);
}

bool IsHeadlessMaster()
{
    static const bool master = []
    {
        char role[16]{};
        const DWORD length = GetEnvironmentVariableA(
            "OPENRAKION_HEADLESS_ROLE", role, static_cast<DWORD>(sizeof(role)));
        return length > 0 && length < sizeof(role) &&
            _stricmp(role, "master") == 0;
    }();
    return master;
}

void DispatchQueuedPeerActions()
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* session = InterlockedCompareExchangePointer(
        &HeadlessSessionState, nullptr, nullptr);
    if (!engine || !session) return;

    using ConstructorFn = void*(__thiscall*)(void*);
    using DestructorFn = void(__thiscall*)(void*);
    using WriteFn = void(__thiscall*)(void*, const void*, unsigned short);
    using HandleMessageFn = void(__thiscall*)(
        void*, int, unsigned short, unsigned long, void*, unsigned long, unsigned short);
    auto construct = reinterpret_cast<ConstructorFn>(
        GetProcAddress(engine, NetMessageConstructorSymbol));
    auto destruct = reinterpret_cast<DestructorFn>(
        GetProcAddress(engine, NetMessageDestructorSymbol));
    auto write = reinterpret_cast<WriteFn>(
        GetProcAddress(engine, NetMessageWriteSymbol));
    auto handle = reinterpret_cast<HandleMessageFn>(
        reinterpret_cast<uintptr_t>(engine) + HandleMessageRva);
    if (!construct || !destruct || !write || !handle) return;

    for (int dispatched = 0; dispatched < 16; ++dispatched)
    {
        PeerPacket packet{};
        AcquireSRWLockExclusive(&PeerPacketLock);
        if (PeerPacketRead == PeerPacketWrite)
        {
            ReleaseSRWLockExclusive(&PeerPacketLock);
            break;
        }
        packet = PeerPackets[PeerPacketRead % _countof(PeerPackets)];
        ++PeerPacketRead;
        ReleaseSRWLockExclusive(&PeerPacketLock);

        alignas(16) BYTE message[1008]{};
        construct(message);
        write(message, packet.Payload, packet.Size);
        handle(session, packet.SourceSlot, packet.Type, 0, message, 0, 0);
        destruct(message);
    }
}

__declspec(naked) void AddRemotePlayerTrace()
{
    __asm
    {
        pushfd
        pushad
        mov eax, dword ptr [esp + 24]
        movzx ecx, byte ptr [esp + 40]
        movzx edx, word ptr [esp + 44]
        mov ebx, dword ptr [esp + 48]
        push ebx
        push edx
        push ecx
        push eax
        call LogRemotePlayerEntry
        popad
        popfd
        cmp dword ptr [HeadlessRemoteSpawnReady], 0
        jne continueRemoteSpawn
        ret 0x0C
    continueRemoteSpawn:
        sub esp, 0x9E8
        jmp dword ptr [AddRemotePlayerContinuation]
    }
}

struct TargetSearchResult
{
    void* Entity{};
    const float* Placement{};
    int SessionEntities{};
    int NetworkEntities{};
};

void PopulateBotAction(void* action)
{
    static const bool master = []
    {
        char role[16]{};
        const DWORD length = GetEnvironmentVariableA(
            "OPENRAKION_HEADLESS_ROLE", role, static_cast<DWORD>(sizeof(role)));
        return length > 0 && length < sizeof(role) &&
            _stricmp(role, "master") == 0;
    }();
    auto* bytes = static_cast<BYTE*>(action);
    const DWORD phase = GetTickCount() % CycleMilliseconds;
    auto& buttons = *reinterpret_cast<DWORD*>(bytes + ButtonsOffset);
    buttons &= ~NavigationButtons;
    bytes[ActionStateOffset] = NormalActionState;
    *reinterpret_cast<float*>(bytes + StrafeAxisOffset) = 0.0f;
    *reinterpret_cast<float*>(bytes + ForwardAxisOffset) = 0.0f;
    if (HasBotTarget)
    {
        *reinterpret_cast<float*>(bytes + StrafeAxisOffset) =
            BotNavigationAction.StrafeAxis;
        *reinterpret_cast<float*>(bytes + ForwardAxisOffset) =
            BotNavigationAction.ForwardAxis;
        if (BotNavigationAction.ForwardAxis < 0.0f)
            buttons |= MoveForwardButton;
        else if (BotNavigationAction.ForwardAxis > 0.0f)
            buttons |= MoveBackwardButton;
        if (BotNavigationAction.StrafeAxis < 0.0f)
            buttons |= MoveLeftButton;
        else if (BotNavigationAction.StrafeAxis > 0.0f)
            buttons |= MoveRightButton;
        if (BotNavigationAction.Jump) buttons |= JumpButton;
        if (BotNavigationAction.Attack &&
            GetTickCount() % AttackCycleMilliseconds < AttackWindowMilliseconds)
        {
            buttons |= PrimaryAttackButton;
            bytes[ActionStateOffset] = AttackActionState;
        }
    }
    else if (!HasBotTarget && !master && phase < MoveUntilMilliseconds)
    {
        *reinterpret_cast<float*>(bytes + ForwardAxisOffset) = MoveForwardAxis;
        buttons |= MoveForwardButton;
    }
    else if (!HasBotTarget && !master && phase < AttackUntilMilliseconds)
    {
        buttons |= PrimaryAttackButton;
        bytes[ActionStateOffset] = AttackActionState;
    }
}

bool IsNativeAttackWindow()
{
    return HasBotTarget &&
        BotTargetDistance >= MinimumAttackDistance &&
        BotTargetDistance <= AttackRange &&
        GetTickCount() % AttackCycleMilliseconds < AttackWindowMilliseconds;
}

void UpdateNativeWeaponHit(HMODULE engine, void* source)
{
    if (!IsNativeAttackWindow()) return;

    HMODULE entities = GetModuleHandleW(L"entitiesmp.dll");
    using GetLocalPlayerEntityFn = void*(__thiscall*)(void*, void*);
    using GetPlayerWeaponsFn = void*(__thiscall*)(void*);
    using UpdateWeaponHitFn = void(__thiscall*)(void*);
    auto** network = reinterpret_cast<void**>(
        GetProcAddress(engine, NetworkSymbol));
    auto getLocalPlayerEntity = reinterpret_cast<GetLocalPlayerEntityFn>(
        GetProcAddress(engine, GetLocalPlayerEntitySymbol));
    auto getPlayerWeapons = entities
        ? reinterpret_cast<GetPlayerWeaponsFn>(
            GetProcAddress(entities, GetPlayerWeaponsSymbol))
        : nullptr;
    auto updateWeaponHit = entities
        ? reinterpret_cast<UpdateWeaponHitFn>(
            GetProcAddress(entities, UpdateWeaponHitSymbol))
        : nullptr;
    if (!network || !*network || !getLocalPlayerEntity ||
        !getPlayerWeapons || !updateWeaponHit)
        return;

    void* player = getLocalPlayerEntity(*network, source);
    void* weapons = player ? getPlayerWeapons(player) : nullptr;
    if (!weapons) return;

    updateWeaponHit(weapons);
    static bool logged{};
    if (!logged)
    {
        logged = true;
        CompatLog("headless bot driver: colisao nativa da arma atualizada");
    }
}

TargetSearchResult FindHeadlessTarget(
    HMODULE engine,
    void* network,
    void* player,
    const float*(__thiscall* getPlacement)(const void*))
{
    using GetNthPlayerEntityFn = void*(__thiscall*)(void*, int);
    using GetPlayerEntityFn = void*(__cdecl*)(int);
    auto getNthPlayerEntity = reinterpret_cast<GetNthPlayerEntityFn>(
        GetProcAddress(engine, GetNthPlayerEntitySymbol));
    auto getPlayerEntity = reinterpret_cast<GetPlayerEntityFn>(
        GetProcAddress(engine, GetPlayerEntitySymbol));
    TargetSearchResult result{};
    if (getPlayerEntity)
    {
        for (int index = 0; index < 16; ++index)
        {
            void* candidate = getPlayerEntity(index);
            if (!candidate) continue;
            ++result.SessionEntities;
            if (candidate == player || result.Entity) continue;
            const float* placement = getPlacement(candidate);
            if (!placement) continue;
            result.Entity = candidate;
            result.Placement = placement;
        }
    }
    if (!getNthPlayerEntity) return result;
    for (int index = 0; index < 20; ++index)
    {
        void* candidate = getNthPlayerEntity(network, index);
        if (!candidate) continue;
        ++result.NetworkEntities;
        if (candidate == player || result.Entity) continue;
        const float* placement = getPlacement(candidate);
        if (!placement) continue;
        result.Entity = candidate;
        result.Placement = placement;
    }
    return result;
}

void LogTargetSearch(const TargetSearchResult& result)
{
    static int lastSessionEntities = -1;
    static int lastNetworkEntities = -1;
    static bool lastFound{};
    const bool found = result.Entity != nullptr;
    if (result.SessionEntities == lastSessionEntities &&
        result.NetworkEntities == lastNetworkEntities &&
        found == lastFound)
        return;

    char message[160]{};
    std::snprintf(
        message,
        sizeof(message),
        "headless bot driver: busca de alvo sessao=%d rede=%d encontrado=%s",
        result.SessionEntities,
        result.NetworkEntities,
        found ? "sim" : "nao");
    CompatLog(message);
    lastSessionEntities = result.SessionEntities;
    lastNetworkEntities = result.NetworkEntities;
    lastFound = found;
}

void LogTargetState(HMODULE engine, void* target)
{
    using PredicateFn = int(__thiscall*)(void*);
    using GetHpFn = float(__thiscall*)(void*);
    HMODULE entities = GetModuleHandleW(L"entitiesmp.dll");
    if (!entities || !target) return;

    auto isLocal = reinterpret_cast<PredicateFn>(
        GetProcAddress(engine, IsLocalEntitySymbol));
    auto isAlive = reinterpret_cast<PredicateFn>(
        GetProcAddress(entities, IsAliveSymbol));
    auto isReady = reinterpret_cast<PredicateFn>(
        GetProcAddress(entities, IsPlayerReadySymbol));
    auto checkFreeze = reinterpret_cast<PredicateFn>(
        GetProcAddress(entities, CheckFreezeStateSymbol));
    auto getHp = reinterpret_cast<GetHpFn>(
        GetProcAddress(entities, GetHpSymbol));
    const DWORD flags = *reinterpret_cast<const DWORD*>(
        static_cast<const BYTE*>(target) + 0x3E0);
    const float freezeTimer = *reinterpret_cast<const float*>(
        static_cast<const BYTE*>(target) + SpawnFreezeTimerOffset);
    const float hp = getHp ? getHp(target) : -1.0f;
    char message[192]{};
    std::snprintf(
        message,
        sizeof(message),
        "headless bot driver: alvo estado entity=%p local=%d alive=%d ready=%d "
        "freeze=%d/%.2f hp=%.2f flags=0x%08lX",
        target,
        isLocal ? isLocal(target) : -1,
        isAlive ? isAlive(target) : -1,
        isReady ? isReady(target) : -1,
        checkFreeze ? checkFreeze(target) : -1,
        freezeTimer,
        hp,
        static_cast<unsigned long>(flags));
    CompatLog(message);
}

void UpdateHeadlessTargeting(HMODULE engine, void* player)
{
    using GetPlacementFn = const float*(__thiscall*)(const void*);
    using SetPlacementFn = void(__thiscall*)(void*, const float*);
    using DirectionVectorToAnglesFn = void(__cdecl*)(const float*, float*);
    auto** network = reinterpret_cast<void**>(
        GetProcAddress(engine, NetworkSymbol));
    auto getPlacement = reinterpret_cast<GetPlacementFn>(
        GetProcAddress(engine, GetPlacementSymbol));
    auto setPlacement = reinterpret_cast<SetPlacementFn>(
        GetProcAddress(engine, SetPlacementSymbol));
    auto directionVectorToAngles = reinterpret_cast<DirectionVectorToAnglesFn>(
        GetProcAddress(engine, DirectionVectorToAnglesSymbol));
    if (!network || !*network || !getPlacement || !setPlacement ||
        !directionVectorToAngles)
        return;

    const float* placement = getPlacement(player);
    if (!placement) return;
    const TargetSearchResult target = FindHeadlessTarget(
        engine, *network, player, getPlacement);
    LogTargetSearch(target);
    if (!target.Entity)
    {
        HasBotTarget = false;
        BotNavigationAction = UpdateHeadlessNavigation({
            GetTickCount(), 0, 0.0f, placement[0], placement[2],
            0.0f, 0.0f,
            false, IsHeadlessMaster()});
        return;
    }

    const float x = target.Placement[0] - placement[0];
    const float z = target.Placement[2] - placement[2];
    const float distance = std::sqrt(x * x + z * z);
    if (distance <= 0.001f) return;
    HasBotTarget = true;
    BotTargetDistance = distance;

    static DWORD lastDistanceLog{};
    const DWORD now = GetTickCount();
    const HeadlessNavigationMode previousMode = BotNavigationAction.Mode;
    BotNavigationAction = UpdateHeadlessNavigation({
        now,
        reinterpret_cast<std::uintptr_t>(target.Entity),
        distance,
        placement[0],
        placement[2],
        target.Placement[0],
        target.Placement[2],
        true,
        IsHeadlessMaster()});
    if (BotNavigationAction.Mode != previousMode)
    {
        char navigationMessage[144]{};
        std::snprintf(
            navigationMessage,
            sizeof(navigationMessage),
            "headless bot driver: navegacao=%s distancia=%.2f",
            HeadlessNavigationModeName(BotNavigationAction.Mode),
            distance);
        CompatLog(navigationMessage);
    }
    if (now - lastDistanceLog >= 1000)
    {
        char message[224]{};
        std::snprintf(
            message,
            sizeof(message),
            "headless bot driver: distancia=%.2f local=%.2f/%.2f alvo=%.2f/%.2f",
            distance,
            placement[0],
            placement[2],
            target.Placement[0],
            target.Placement[2]);
        CompatLog(message);
        LogTargetState(engine, target.Entity);
        lastDistanceLog = now;
    }

    static bool wasInAttackRange{};
    const bool inAttackRange =
        distance >= MinimumAttackDistance && distance <= AttackRange;
    if (!inAttackRange || !wasInAttackRange)
    {
        const float directionX = x / distance;
        const float directionZ = z / distance;
        float direction[3]{directionX, 0.0f, directionZ};
        float updatedPlacement[6]{};
        std::memcpy(updatedPlacement, placement, sizeof(updatedPlacement));
        directionVectorToAngles(direction, updatedPlacement + 3);
        setPlacement(player, updatedPlacement);
    }
    if (inAttackRange == wasInAttackRange) return;
    wasInAttackRange = inAttackRange;
    char message[128]{};
    std::snprintf(
        message,
        sizeof(message),
        "headless bot driver: alvo=%p distancia=%.2f estado=%s",
        target.Entity,
        distance,
        inAttackRange ? "ataque" :
        distance < MinimumAttackDistance ? "recuo" : "aproximacao");
    CompatLog(message);
}

bool HasEnvironmentValue(const char* name, const char* expected)
{
    char value[16]{};
    const DWORD length = GetEnvironmentVariableA(
        name, value, static_cast<DWORD>(sizeof(value)));
    return length > 0 && length < sizeof(value) &&
        _stricmp(value, expected) == 0;
}

int ReadPlayerFlag(HMODULE engine, HMODULE entities, void* player, const char* name)
{
    using StringConstructorFn = void*(__thiscall*)(void*, const char*);
    using StringDestructorFn = void(__thiscall*)(void*);
    using IsFlagOnFn = int(__thiscall*)(void*, void*);
    auto construct = reinterpret_cast<StringConstructorFn>(
        GetProcAddress(engine, StringConstructorSymbol));
    auto destruct = reinterpret_cast<StringDestructorFn>(
        GetProcAddress(engine, StringDestructorSymbol));
    auto isFlagOn = reinterpret_cast<IsFlagOnFn>(
        GetProcAddress(entities, IsFlagOnSymbol));
    if (!construct || !destruct || !isFlagOn)
        return -1;

    void* text{};
    construct(&text, name);
    const int enabled = isFlagOn(player, text);
    destruct(&text);
    return enabled;
}

void AdvanceHeadlessSpawnFreeze(void* player)
{
    auto* timer = reinterpret_cast<float*>(
        static_cast<BYTE*>(player) + SpawnFreezeTimerOffset);

    static void* trackedPlayer{};
    static DWORD startedAt{};
    static bool active{};
    static bool released{};
    if (trackedPlayer != player)
    {
        trackedPlayer = player;
        startedAt = 0;
        active = false;
        released = false;
        InterlockedExchange(&HeadlessGameplayReady, 0);
    }
    if (!active)
    {
        if (*timer >= 0.0f || *timer < -4.01f) return;
        startedAt = GetTickCount();
        active = true;
    }

    const float elapsed = static_cast<float>(GetTickCount() - startedAt) / 1000.0f;
    const float advanced = elapsed >= 4.0f ? 0.0f : -4.0f + elapsed;
    if (*timer < 0.0f && *timer >= -4.01f && advanced > *timer)
        *timer = advanced;
    if (elapsed >= 4.0f && !released)
    {
        released = true;
        InterlockedExchange(&HeadlessGameplayReady, 1);
        CompatLog("headless bot driver: freeze de spawn concluido sem HUD");
    }
}

void SynchronizeHeadlessRoundState(
    HMODULE engine, HMODULE entities, void* player)
{
    using GetFieldInfoFn = void*(__thiscall*)(void*);
    using GetRoundStateFn = int(__thiscall*)(void*);
    using SetRoundStateFn = void(__thiscall*)(void*, int);
    auto getFieldInfo = reinterpret_cast<GetFieldInfoFn>(
        GetProcAddress(entities, GetFieldInfoSymbol));
    auto getRoundState = reinterpret_cast<GetRoundStateFn>(
        GetProcAddress(engine, GetRoundStateSymbol));
    auto setRoundState = reinterpret_cast<SetRoundStateFn>(
        GetProcAddress(engine, SetRoundStateSymbol));
    if (!getFieldInfo || !getRoundState || !setRoundState) return;

    static void* synchronizedPlayer{};
    if (synchronizedPlayer == player) return;
    void* fieldInfo = getFieldInfo(player);
    if (!fieldInfo) return;
    const int state = getRoundState(fieldInfo);
    if (state != 0 && state != 2) return;

    setRoundState(fieldInfo, 1);
    synchronizedPlayer = player;
    CompatLog("headless bot driver: FieldInfo sincronizado para round em jogo");
}

void PublishInitialHeadlessPlayerState(
    HMODULE engine, HMODULE entities, void* player)
{
    static void* observedPlayer{};
    static DWORD observedAt{};
    if (observedPlayer != player)
    {
        observedPlayer = player;
        observedAt = GetTickCount();
        InterlockedExchange(&HeadlessRemoteSpawnReady, 0);
        return;
    }
    if (GetTickCount() - observedAt < 2000) return;

    using ConstructorFn = void*(__thiscall*)(void*);
    using DestructorFn = void(__thiscall*)(void*);
    using GetDataFn = void*(__thiscall*)(void*);
    using GetSizeFn = unsigned short(__thiscall*)(void*);
    using GetInitDataFn = void(__thiscall*)(void*, void*);
    using SendInitialStateFn = void(__thiscall*)(
        void*, unsigned short, char*);
    auto** worldNet = reinterpret_cast<void**>(
        GetProcAddress(engine, WorldNetSymbol));
    auto construct = reinterpret_cast<ConstructorFn>(
        GetProcAddress(engine, NetMessageConstructorSymbol));
    auto destruct = reinterpret_cast<DestructorFn>(
        GetProcAddress(engine, NetMessageDestructorSymbol));
    auto getData = reinterpret_cast<GetDataFn>(
        GetProcAddress(engine, NetMessageDataSymbol));
    auto getSize = reinterpret_cast<GetSizeFn>(
        GetProcAddress(engine, NetMessageSizeSymbol));
    auto getInitData = reinterpret_cast<GetInitDataFn>(
        GetProcAddress(entities, PlayerInitDataSymbol));
    auto sendInitialState = reinterpret_cast<SendInitialStateFn>(
        GetProcAddress(engine, SendFieldGameAddPlayerSymbol));
    if (!worldNet || !*worldNet || !construct || !destruct || !getData ||
        !getSize || !getInitData || !sendInitialState)
        return;

    static void* publishedPlayer{};
    if (publishedPlayer == player) return;
    alignas(16) BYTE message[1008]{};
    construct(message);
    getInitData(player, message);
    const unsigned short size = getSize(message);
    void* data = getData(message);
    if (size > 0 && size <= 200 && data)
    {
        InterlockedExchange(&HeadlessRemoteSpawnReady, 1);
        sendInitialState(
            *worldNet, size, static_cast<char*>(data));
        publishedPlayer = player;
        char log[128]{};
        std::snprintf(
            log,
            sizeof(log),
            "headless bot driver: estado inicial 0x4B publicado (%u bytes)",
            static_cast<unsigned>(size));
        CompatLog(log);
    }
    destruct(message);
}

void LogLocalPlayerState(HMODULE engine, void* source, int applied)
{
    using GetLocalPlayerEntityFn = void*(__thiscall*)(void*, void*);
    auto** network = reinterpret_cast<void**>(
        GetProcAddress(engine, NetworkSymbol));
    auto getLocalPlayerEntity = reinterpret_cast<GetLocalPlayerEntityFn>(
        GetProcAddress(engine, GetLocalPlayerEntitySymbol));
    if (!network || !*network || !getLocalPlayerEntity) return;

    void* entity = getLocalPlayerEntity(*network, source);
    if (!entity) return;
    HMODULE entities = GetModuleHandleW(L"entitiesmp.dll");
    if (!entities) return;
    AdvanceHeadlessSpawnFreeze(entity);
    SynchronizeHeadlessRoundState(engine, entities, entity);
    PublishInitialHeadlessPlayerState(engine, entities, entity);
    if (applied < 0) return;

    using EntityPredicateFn = int(__thiscall*)(void*);
    using EntityCommandFn = void(__thiscall*)(void*);
    auto isLocalEntity = reinterpret_cast<EntityPredicateFn>(
        GetProcAddress(engine, IsLocalEntitySymbol));
    auto setAsLocalEntity = reinterpret_cast<EntityCommandFn>(
        GetProcAddress(engine, SetAsLocalEntitySymbol));
    const int wasLocal = isLocalEntity ? isLocalEntity(entity) : -1;
    if (wasLocal == 0 && setAsLocalEntity)
    {
        setAsLocalEntity(entity);
        CompatLog("headless bot driver: entidade do player source marcada como local");
    }

    using PlayerPredicateFn = int(__thiscall*)(void*);
    auto isAlive = reinterpret_cast<PlayerPredicateFn>(
        GetProcAddress(entities, IsAliveSymbol));
    auto isPlayerReady = reinterpret_cast<PlayerPredicateFn>(
        GetProcAddress(entities, IsPlayerReadySymbol));
    auto checkFreezeState = reinterpret_cast<PlayerPredicateFn>(
        GetProcAddress(entities, CheckFreezeStateSymbol));
    const auto flags = *reinterpret_cast<const DWORD*>(
        static_cast<const BYTE*>(entity) + 0x3E0);
    const int local = isLocalEntity ? isLocalEntity(entity) : -1;
    const int alive = isAlive ? isAlive(entity) : -1;
    const int ready = isPlayerReady ? isPlayerReady(entity) : -1;
    const int freezeState = checkFreezeState ? checkFreezeState(entity) : -1;
    const int translationLock = ReadPlayerFlag(
        engine, entities, entity, "TransLock_Switch");
    const int rotationLock = ReadPlayerFlag(
        engine, entities, entity, "RotationLock_Switch");
    const auto* desired = reinterpret_cast<const float*>(
        static_cast<const BYTE*>(entity) + DesiredTranslationOffset);
    const auto* consumedAction = reinterpret_cast<const float*>(
        static_cast<const BYTE*>(entity) + PlayerActionOffset + 0x38);
    const auto* animator = *reinterpret_cast<const BYTE* const*>(
        static_cast<const BYTE*>(entity) + PlayerAnimatorOffset);
    const float freezeTimer = *reinterpret_cast<const float*>(
        static_cast<const BYTE*>(entity) + SpawnFreezeTimerOffset);
    using GetHpFn = float(__thiscall*)(void*);
    auto getHp = reinterpret_cast<GetHpFn>(
        GetProcAddress(entities, GetHpSymbol));
    const float hp = getHp ? getHp(entity) : -1.0f;
    static void* lastEntity{};
    static DWORD lastFlags = MAXDWORD;
    static int lastApplied = -1;
    static int lastAlive = -2;
    static int lastReady = -2;
    static int lastFreezeState = -2;
    static int lastLocal = -2;
    static int lastTranslationLock = -2;
    static int lastRotationLock = -2;
    static float lastDesired[3]{};
    static float lastConsumedAction[3]{};
    static int lastFreezeBucket = -1000;
    const int freezeBucket = static_cast<int>(freezeTimer * 2.0f);
    if (entity == lastEntity && flags == lastFlags && applied == lastApplied &&
        local == lastLocal && alive == lastAlive && ready == lastReady &&
        freezeState == lastFreezeState &&
        translationLock == lastTranslationLock && rotationLock == lastRotationLock &&
        desired[0] == lastDesired[0] && desired[1] == lastDesired[1] &&
        desired[2] == lastDesired[2] &&
        consumedAction[0] == lastConsumedAction[0] &&
        consumedAction[1] == lastConsumedAction[1] &&
        consumedAction[2] == lastConsumedAction[2] &&
        freezeBucket == lastFreezeBucket)
        return;

    const DWORD animatorState =
        animator ? *reinterpret_cast<const DWORD*>(animator + 0x14C) : MAXDWORD;
    char message[384]{};
    std::snprintf(
        message,
        sizeof(message),
        "headless bot driver: entity=%p flags=0x%08lX local=%d alive=%d ready=%d "
        "applied=%d locks=%d/%d freeze=%d/%.2f action=%.2f/%.2f/%.2f "
        "anim=%p/%lu hp=%.2f desired=%.2f/%.2f/%.2f",
        entity,
        static_cast<unsigned long>(flags),
        local,
        alive,
        ready,
        applied,
        translationLock,
        rotationLock,
        freezeState,
        freezeTimer,
        consumedAction[0],
        consumedAction[1],
        consumedAction[2],
        animator,
        static_cast<unsigned long>(animatorState),
        hp,
        desired[0],
        desired[1],
        desired[2]);
    CompatLog(message);
    lastEntity = entity;
    lastFlags = flags;
    lastApplied = applied;
    lastLocal = local;
    lastAlive = alive;
    lastReady = ready;
    lastFreezeState = freezeState;
    lastTranslationLock = translationLock;
    lastRotationLock = rotationLock;
    lastDesired[0] = desired[0];
    lastDesired[1] = desired[1];
    lastDesired[2] = desired[2];
    lastConsumedAction[0] = consumedAction[0];
    lastConsumedAction[1] = consumedAction[1];
    lastConsumedAction[2] = consumedAction[2];
    lastFreezeBucket = freezeBucket;
}
}

bool IsHeadlessBotDriverEnabled()
{
    static const bool enabled =
        HasEnvironmentValue("OPENRAKION_HEADLESS", "1");
    return enabled;
}

void QueueHeadlessPeerPacket(const void* packet, unsigned short size)
{
    if (!IsHeadlessBotDriverEnabled() || !IsHeadlessMaster() ||
        !packet || size < 7)
        return;
    const auto* bytes = static_cast<const BYTE*>(packet);
    const unsigned short type = *reinterpret_cast<const unsigned short*>(bytes);
    const unsigned short payloadSize = static_cast<unsigned short>(size - 7);
    if (type != 0x030A || payloadSize > sizeof(PeerPacket::Payload))
        return;

    AcquireSRWLockExclusive(&PeerPacketLock);
    if (PeerPacketWrite - PeerPacketRead >= _countof(PeerPackets))
        ++PeerPacketRead;
    PeerPacket& queued = PeerPackets[PeerPacketWrite % _countof(PeerPackets)];
    queued.Type = type;
    queued.Size = payloadSize;
    queued.SourceSlot = bytes[6];
    std::memcpy(queued.Payload, bytes + 7, payloadSize);
    ++PeerPacketWrite;
    ReleaseSRWLockExclusive(&PeerPacketLock);
}

bool IsHeadlessGameplayReady()
{
    return InterlockedCompareExchange(&HeadlessGameplayReady, 0, 0) != 0;
}

bool InstallHeadlessRemotePlayerTrace(HMODULE engine)
{
    if (!IsHeadlessBotDriverEnabled()) return true;
    auto* patch = reinterpret_cast<BYTE*>(
        reinterpret_cast<uintptr_t>(engine) + AddRemotePlayerRva);
    if (std::memcmp(
            patch, AddRemotePlayerPrologue, sizeof(AddRemotePlayerPrologue)) != 0)
    {
        CompatLog("headless bot driver: prologo AddRemotePlayer inesperado");
        return false;
    }

    DWORD protection{};
    if (!VirtualProtect(
            patch,
            sizeof(AddRemotePlayerPrologue),
            PAGE_EXECUTE_READWRITE,
            &protection))
        return false;
    AddRemotePlayerContinuation =
        reinterpret_cast<uintptr_t>(patch + sizeof(AddRemotePlayerPrologue));
    patch[0] = 0xE9;
    *reinterpret_cast<int32_t*>(patch + 1) = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(&AddRemotePlayerTrace) -
        reinterpret_cast<uintptr_t>(patch + 5));
    patch[5] = 0x90;
    FlushInstructionCache(
        GetCurrentProcess(), patch, sizeof(AddRemotePlayerPrologue));
    DWORD ignored{};
    VirtualProtect(
        patch, sizeof(AddRemotePlayerPrologue), protection, &ignored);

    CompatLog("headless bot driver: gate AddRemotePlayer instalado");
    return true;
}

void ApplyHeadlessBotAction(const void* source, void* action)
{
    if (!IsHeadlessBotDriverEnabled() || !source || !action)
        return;

    InterlockedExchangePointer(
        &HeadlessPlayerSource, const_cast<void*>(source));
    DispatchQueuedPeerActions();
    static bool logged{};
    PopulateBotAction(action);
    if (!logged)
    {
        logged = true;
        CompatLog("headless bot driver: ciclo nativo de avanço e ataque habilitado");
    }
}

void PumpHeadlessBotAction()
{
    if (!IsHeadlessBotDriverEnabled()) return;
    static DWORD lastSent{};
    const DWORD now = GetTickCount();

    HMODULE engine = GetModuleHandleW(L"engine.dll");
    void* source = InterlockedCompareExchangePointer(
        &HeadlessPlayerSource, nullptr, nullptr);
    if (!engine || !source) return;

    using ApplyActionFn = void(__thiscall*)(void*, int&);
    using SendActionFn = void(__thiscall*)(void*);
    auto applyAction = reinterpret_cast<ApplyActionFn>(
        GetProcAddress(engine, ApplyActionSymbol));
    auto sendAction = reinterpret_cast<SendActionFn>(
        GetProcAddress(engine, SendActionSymbol));
    if (!applyAction || !sendAction) return;

    __try
    {
        LogLocalPlayerState(engine, source, -1);
        static DWORD lastTargeting{};
        if (now - lastTargeting >= TargetingIntervalMilliseconds)
        {
            HMODULE entities = GetModuleHandleW(L"entitiesmp.dll");
            using GetLocalPlayerEntityFn = void*(__thiscall*)(void*, void*);
            auto** network = reinterpret_cast<void**>(
                GetProcAddress(engine, NetworkSymbol));
            auto getLocalPlayerEntity = reinterpret_cast<GetLocalPlayerEntityFn>(
                GetProcAddress(engine, GetLocalPlayerEntitySymbol));
            if (entities && network && *network && getLocalPlayerEntity)
            {
                void* player = getLocalPlayerEntity(*network, source);
                if (player) UpdateHeadlessTargeting(engine, player);
            }
            lastTargeting = now;
        }
        PopulateBotAction(static_cast<BYTE*>(source) + SourceActionOffset);
        int applied{};
        applyAction(source, applied);
        UpdateNativeWeaponHit(engine, source);
        LogLocalPlayerState(engine, source, applied);
        if (now - lastSent < SendIntervalMilliseconds) return;
        sendAction(source);
        lastSent = now;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
    }
}
