#include <winsock2.h>
#include <windows.h>

#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

#include "baked_patches.h"

FARPROC g_versionExports[17]{};

#define VERSION_FORWARD(name, index) \
    extern "C" __declspec(naked) void name() \
    { \
        __asm jmp dword ptr [g_versionExports + index * 4] \
    }

VERSION_FORWARD(Forward01, 0)
VERSION_FORWARD(Forward02, 1)
VERSION_FORWARD(Forward03, 2)
VERSION_FORWARD(Forward04, 3)
VERSION_FORWARD(Forward05, 4)
VERSION_FORWARD(Forward06, 5)
VERSION_FORWARD(Forward07, 6)
VERSION_FORWARD(Forward08, 7)
VERSION_FORWARD(Forward09, 8)
VERSION_FORWARD(Forward10, 9)
VERSION_FORWARD(Forward11, 10)
VERSION_FORWARD(Forward12, 11)
VERSION_FORWARD(Forward13, 12)
VERSION_FORWARD(Forward14, 13)
VERSION_FORWARD(Forward15, 14)
VERSION_FORWARD(Forward16, 15)
VERSION_FORWARD(Forward17, 16)

namespace
{
constexpr uintptr_t PatchAddress = 0x351533e9;
constexpr uintptr_t ContinueAddress = PatchAddress + 5;
constexpr uintptr_t RangedDamageReturnAddress = 0x3519f5ad;
constexpr uintptr_t PlayerUpdateAddress = 0x35165170;
constexpr uintptr_t PlayerUpdateContinueAddress = PlayerUpdateAddress + 8;
constexpr uintptr_t SetAliveAddress = 0x35130b70;
constexpr uintptr_t SetDeadAddress = 0x35135810;
// CEntity::FallDownToFloor @ engine.dll (base 0x36000000). void __thiscall(this): casta 4 raios p/ baixo
// dos cantos da collision-box, acha o chão mais alto e ajusta SÓ o Y ao piso real (mantém X/Z) via
// SetPlacement — sem tocar velocidade/eventos. É a geometry query do invariante #7 da golden capture,
// feita pela própria engine (nada de struct de raio montado à mão). Aterra o avatar-fantasma do bot.
constexpr uintptr_t FallDownToFloorAddress = 0x36124ce0;
constexpr BYTE ExpectedFallDownToFloor[] = { 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00, 0x6a, 0xff };
constexpr uint32_t ReceiveDamageStackReturnOffset = 0x4d4;
constexpr BYTE Expected[] = { 0x68, 0x30, 0xa6, 0x2b, 0x35 };
constexpr BYTE ExpectedPlayerUpdate[] = { 0x6a, 0xff, 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00 };
constexpr int MaxPlayerSeats = 20;
volatile LONG GroundSnapEnabled = 0;   // 1 só após verificar o prólogo de FallDownToFloor (fail-closed)
constexpr const char* LifecyclePath = "C:\\temp\\bot_lifecycle.txt";
volatile LONG DesiredLifecycleSequence[MaxPlayerSeats]{};
volatile LONG DesiredDeadState[MaxPlayerSeats]{};
volatile LONG AppliedLifecycleSequence[MaxPlayerSeats]{};
volatile LONG LoggedLifecycleSequence[MaxPlayerSeats]{};
uintptr_t PlayerUpdateContinue = PlayerUpdateContinueAddress;
constexpr uint16_t BotTelemetryType = 0xb07a;
constexpr uint32_t SendToOtherClientRva = 0x00100780;
constexpr BYTE ExpectedSendToOtherClient[] = { 0x83, 0xec, 0x0c, 0x55, 0x8b, 0xe9 };
constexpr uint32_t WindowedRva = 0x00d46d;
constexpr uint32_t NoDisplayResetRvas[] = { 0x00dbc2, 0x00dc1e, 0x00dc4f };
constexpr uint32_t MultiInstanceRva = 0x002c96;
using SendToFn = int(WSAAPI*)(SOCKET, const char*, int, int, const sockaddr*, int);
using ConnectFn = int(WSAAPI*)(SOCKET, const sockaddr*, int);
SendToFn OriginalSendTo{};
ConnectFn SystemConnect{};
volatile LONG WorldEndpointFamilyPort{};
volatile LONG WorldEndpointAddress{};
volatile LONG WorldSocketValue{-1};
volatile LONG ServerAddress{};
volatile LONG TelemetrySequence{};
volatile LONG LocalActionHookEnabled{};
volatile LONG LocalAttackLogged{};
alignas(8) volatile LONG64 LastTelemetryKey{};
uintptr_t SendToOtherClientContinue{};
void Log(const char* message);
void EmitJump(std::vector<BYTE>& code, uintptr_t source, uintptr_t target);
constexpr const char* VersionExports[] = {
    "GetFileVersionInfoA", "GetFileVersionInfoByHandle", "GetFileVersionInfoExA",
    "GetFileVersionInfoExW", "GetFileVersionInfoSizeA", "GetFileVersionInfoSizeExA",
    "GetFileVersionInfoSizeExW", "GetFileVersionInfoSizeW", "GetFileVersionInfoW",
    "VerFindFileA", "VerFindFileW", "VerInstallFileA", "VerInstallFileW",
    "VerLanguageNameA", "VerLanguageNameW", "VerQueryValueA", "VerQueryValueW"
};

bool LoadVersionExports(HINSTANCE instance)
{
    wchar_t path[MAX_PATH]{};
    if (GetModuleFileNameW(instance, path, MAX_PATH) == 0) return false;
    wchar_t* name = wcsrchr(path, L'\\');
    if (!name) return false;
    wcscpy_s(name + 1, MAX_PATH - static_cast<size_t>(name + 1 - path), L"verorig.dll");
    HMODULE original = LoadLibraryW(path);
    if (!original) return false;
    for (size_t i = 0; i < std::size(VersionExports); ++i)
    {
        g_versionExports[i] = GetProcAddress(original, VersionExports[i]);
        if (!g_versionExports[i]) return false;
    }
    return true;
}

bool ApplyFinalClientPatches()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    for (int i = 0; i < kBakedPatchCount; ++i)
    {
        const BYTE current = image[kBakedPatches[i].rva];
        if (current != kBakedPatches[i].from && current != kBakedPatches[i].to)
        {
            OutputDebugStringA("RakionClientCompat: build incompatível; patch final abortado\n");
            return false;
        }
    }

    int first = 0;
    while (first < kBakedPatchCount)
    {
        int last = first + 1;
        while (last < kBakedPatchCount &&
               kBakedPatches[last].rva == kBakedPatches[last - 1].rva + 1)
            ++last;

        BYTE* start = image + kBakedPatches[first].rva;
        const SIZE_T length = static_cast<SIZE_T>(last - first);
        DWORD oldProtection{};
        if (!VirtualProtect(start, length, PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
        for (int i = first; i < last; ++i) image[kBakedPatches[i].rva] = kBakedPatches[i].to;
        VirtualProtect(start, length, oldProtection, &oldProtection);
        first = last;
    }
    FlushInstructionCache(GetCurrentProcess(), nullptr, 0);
    OutputDebugStringA("RakionClientCompat: patches do rakion-final aplicados\n");
    return true;
}

bool ApplyBytePatch(BYTE* image, uint32_t rva, BYTE expected, BYTE replacement)
{
    BYTE* address = image + rva;
    if (*address == replacement) return true;
    if (*address != expected) return false;
    DWORD oldProtection{};
    if (!VirtualProtect(address, 1, PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    *address = replacement;
    VirtualProtect(address, 1, oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), address, 1);
    return true;
}

bool UsesWindowedDisplayMode()
{
    char path[MAX_PATH]{};
    if (GetModuleFileNameA(nullptr, path, MAX_PATH) == 0) return false;
    char* name = std::strrchr(path, '\\');
    if (!name) return false;
    strcpy_s(name + 1, MAX_PATH - static_cast<size_t>(name + 1 - path), "..\\display.mode");
    std::ifstream input(path);
    std::string mode;
    input >> mode;
    return mode == "windowed" || mode == "borderless";
}

bool LoadServerAddress()
{
    char path[MAX_PATH]{};
    if (GetModuleFileNameA(nullptr, path, MAX_PATH) == 0) return false;
    char* name = std::strrchr(path, '\\');
    if (!name) return false;
    strcpy_s(name + 1, MAX_PATH - static_cast<size_t>(name + 1 - path), "..\\server.host");
    std::ifstream input(path);
    std::string host;
    input >> host;
    unsigned int a{}, b{}, c{}, d{};
    char tail{};
    if (sscanf_s(host.c_str(), "%u.%u.%u.%u%c", &a, &b, &c, &d, &tail, 1) != 4 ||
        a > 255 || b > 255 || c > 255 || d > 255)
        return false;
    const uint32_t address = a | b << 8 | c << 16 | d << 24;
    InterlockedExchange(&ServerAddress, static_cast<LONG>(address));
    return true;
}

bool IsRakionServerPort(u_short networkPort)
{
    const auto* bytes = reinterpret_cast<const BYTE*>(&networkPort);
    const uint16_t port = static_cast<uint16_t>(bytes[0] << 8 | bytes[1]);
    return port == 40706 || port == 40708 || port == 40709;
}

const sockaddr* RedirectServerEndpoint(
    const sockaddr* target, int targetLength, sockaddr_in& redirected)
{
    if (!target || targetLength < static_cast<int>(sizeof(sockaddr_in)) ||
        target->sa_family != AF_INET)
        return target;
    redirected = *reinterpret_cast<const sockaddr_in*>(target);
    const LONG address = InterlockedCompareExchange(&ServerAddress, 0, 0);
    if (address != 0 && IsRakionServerPort(redirected.sin_port))
    {
        redirected.sin_addr.s_addr = static_cast<u_long>(address);
        return reinterpret_cast<const sockaddr*>(&redirected);
    }
    return target;
}

int WSAAPI ConnectHook(SOCKET socket, const sockaddr* target, int targetLength)
{
    sockaddr_in redirected{};
    const sockaddr* destination = RedirectServerEndpoint(target, targetLength, redirected);
    return SystemConnect(socket, destination, targetLength);
}

bool ApplyLauncherPatches()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image || !ApplyBytePatch(image, MultiInstanceRva, 0xb7, 0xff)) return false;
    if (!UsesWindowedDisplayMode()) return true;
    if (!ApplyBytePatch(image, WindowedRva, 0x74, 0xeb)) return false;
    for (uint32_t rva : NoDisplayResetRvas)
        if (!ApplyBytePatch(image, rva, 0x74, 0xeb)) return false;
    return true;
}

void PatchKeyHook()
{
    HMODULE module{};
    for (int attempt = 0; attempt < 240; ++attempt)
    {
        module = GetModuleHandleW(L"keyhook.dll");
        if (module) break;
        Sleep(250);
    }
    if (!module) { Log("keyhook.dll não carregada"); return; }
    auto* image = reinterpret_cast<BYTE*>(module);
    const bool first = ApplyBytePatch(image, 0x106e, 0x01, 0x00);
    const bool second = ApplyBytePatch(image, 0x10a3, 0x01, 0x00);
    Log(first && second ? "Alt+Tab liberado pela DLL" : "patch de Alt+Tab incompatível");
}

bool SameEndpoint(const sockaddr_in& left, const sockaddr_in& right)
{
    return left.sin_family == right.sin_family && left.sin_port == right.sin_port &&
           left.sin_addr.s_addr == right.sin_addr.s_addr;
}

void RememberWorldEndpoint(const sockaddr* target, int targetLength)
{
    if (!target || targetLength < static_cast<int>(sizeof(sockaddr_in)) ||
        target->sa_family != AF_INET)
        return;
    const auto& endpoint = *reinterpret_cast<const sockaddr_in*>(target);
    InterlockedExchange(&WorldEndpointAddress, static_cast<LONG>(endpoint.sin_addr.s_addr));
    const LONG familyPort = static_cast<LONG>(endpoint.sin_family) |
        (static_cast<LONG>(static_cast<uint16_t>(endpoint.sin_port)) << 16);
    InterlockedExchange(&WorldEndpointFamilyPort, familyPort);
}

bool ReadWorldEndpoint(sockaddr_in& endpoint)
{
    const LONG familyPort = InterlockedCompareExchange(&WorldEndpointFamilyPort, 0, 0);
    if ((familyPort & 0xffff) != AF_INET) return false;
    endpoint = {};
    endpoint.sin_family = AF_INET;
    endpoint.sin_port = static_cast<u_short>((static_cast<ULONG>(familyPort) >> 16) & 0xffff);
    endpoint.sin_addr.s_addr = static_cast<u_long>(
        InterlockedCompareExchange(&WorldEndpointAddress, 0, 0));
    return true;
}

bool IsBotTelemetryInput(const char* buffer, int length, uint16_t& type, uint32_t& sequence)
{
    if (!buffer || length < 10) return false;
    type = static_cast<uint16_t>(static_cast<BYTE>(buffer[0]) |
        static_cast<uint16_t>(static_cast<BYTE>(buffer[1])) << 8);
    const bool movement = type == 0x030a && length == 26;
    const bool attack = type == 0x0311 && length == 10 && static_cast<BYTE>(buffer[8]) == 1;
    if (InterlockedCompareExchange(&LocalActionHookEnabled, 0, 0) != 0 ||
        (!movement && !attack))
        return false;
    std::memcpy(&sequence, buffer + 2, sizeof(sequence));
    return true;
}

int WSAAPI SendToHook(
    SOCKET socket, const char* buffer, int length, int flags,
    const sockaddr* target, int targetLength)
{
    sockaddr_in redirected{};
    const sockaddr* destination = RedirectServerEndpoint(target, targetLength, redirected);
    if (buffer && length >= 2 && static_cast<BYTE>(buffer[0]) == 0x02 &&
        static_cast<BYTE>(buffer[1]) == 0x02)
    {
        RememberWorldEndpoint(destination, targetLength);
        InterlockedExchange(&WorldSocketValue, static_cast<LONG>(socket));
    }

    sockaddr_in world{};
    uint16_t type{};
    uint32_t sequence{};
    if (ReadWorldEndpoint(world) && IsBotTelemetryInput(buffer, length, type, sequence) &&
        destination && destination->sa_family == AF_INET &&
        !SameEndpoint(*reinterpret_cast<const sockaddr_in*>(destination), world))
    {
        const LONG64 key = (static_cast<LONG64>(type) << 32) | sequence;
        if (InterlockedExchange64(&LastTelemetryKey, key) != key)
        {
            BYTE packet[1200]{};
            packet[0] = static_cast<BYTE>(BotTelemetryType);
            packet[1] = static_cast<BYTE>(BotTelemetryType >> 8);
            packet[2] = static_cast<BYTE>(length);
            packet[3] = static_cast<BYTE>(length >> 8);
            std::memcpy(packet + 4, buffer, static_cast<size_t>(length));
            OriginalSendTo(socket, reinterpret_cast<const char*>(packet), length + 4, flags,
                reinterpret_cast<const sockaddr*>(&world), sizeof(world));
        }
    }
    return OriginalSendTo(socket, buffer, length, flags, destination, targetLength);
}

void __stdcall MirrorLocalAction(uint32_t type, const void* message)
{
    if ((type != 0x030a && type != 0x0311) || !message || !OriginalSendTo) return;
    __try
    {
        const auto* bytes = static_cast<const BYTE*>(message);
        const uint16_t payloadLength = *reinterpret_cast<const uint16_t*>(bytes + 0x3ea);
        const BYTE* payload = bytes + 2;
        const bool movement = type == 0x030a && payloadLength == 19;
        const bool attack = type == 0x0311 && payloadLength == 3 && payload[1] == 1;
        if (!movement && !attack) return;

        sockaddr_in world{};
        const SOCKET socket = static_cast<SOCKET>(
            InterlockedCompareExchange(&WorldSocketValue, -1, -1));
        if (socket == INVALID_SOCKET || !ReadWorldEndpoint(world)) return;

        BYTE packet[30]{};
        const uint16_t innerLength = static_cast<uint16_t>(7 + payloadLength);
        packet[0] = static_cast<BYTE>(BotTelemetryType);
        packet[1] = static_cast<BYTE>(BotTelemetryType >> 8);
        std::memcpy(packet + 2, &innerLength, sizeof(innerLength));
        packet[4] = static_cast<BYTE>(type);
        packet[5] = static_cast<BYTE>(type >> 8);
        const uint32_t sequence = static_cast<uint32_t>(InterlockedIncrement(&TelemetrySequence));
        std::memcpy(packet + 6, &sequence, sizeof(sequence));
        packet[10] = movement ? static_cast<BYTE>(payload[2] & 0x1f) : payload[0];
        std::memcpy(packet + 11, payload, payloadLength);
        OriginalSendTo(socket, reinterpret_cast<const char*>(packet), innerLength + 4, 0,
            reinterpret_cast<const sockaddr*>(&world), sizeof(world));
        if (attack && InterlockedExchange(&LocalAttackLogged, 1) == 0)
            Log("primeiro ataque humano espelhado ao World antes do loop P2P");
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
    }
}

__declspec(naked) void SendToOtherClientHook()
{
    __asm
    {
        pushfd
        pushad
        push dword ptr [esp + 44]
        push dword ptr [esp + 44]
        call MirrorLocalAction
        popad
        popfd
        sub esp, 0x0c
        push ebp
        mov ebp, ecx
        jmp dword ptr [SendToOtherClientContinue]
    }
}

bool InstallLocalActionHook(HMODULE engine)
{
    auto* patch = reinterpret_cast<BYTE*>(engine) + SendToOtherClientRva;
    if (std::memcmp(patch, ExpectedSendToOtherClient, sizeof(ExpectedSendToOtherClient)) != 0)
        return false;
    SendToOtherClientContinue = reinterpret_cast<uintptr_t>(patch + sizeof(ExpectedSendToOtherClient));
    DWORD oldProtection{};
    if (!VirtualProtect(patch, sizeof(ExpectedSendToOtherClient), PAGE_EXECUTE_READWRITE,
                        &oldProtection))
        return false;
    std::vector<BYTE> detour;
    EmitJump(detour, reinterpret_cast<uintptr_t>(patch),
             reinterpret_cast<uintptr_t>(&SendToOtherClientHook));
    std::memcpy(patch, detour.data(), detour.size());
    std::memset(patch + detour.size(), 0x90, sizeof(ExpectedSendToOtherClient) - detour.size());
    VirtualProtect(patch, sizeof(ExpectedSendToOtherClient), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(ExpectedSendToOtherClient));
    InterlockedExchange(&LocalActionHookEnabled, 1);
    return true;
}

bool InstallSendToHook(HMODULE module)
{
    auto* base = reinterpret_cast<BYTE*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (directory.VirtualAddress == 0) return false;

    auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress);
    for (; descriptor->Name != 0; ++descriptor)
    {
        const char* library = reinterpret_cast<const char*>(base + descriptor->Name);
        if (_stricmp(library, "wsock32.dll") != 0) continue;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->OriginalFirstThunk);
        auto* imports = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData != 0; ++names, ++imports)
        {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto* name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            if (std::strcmp(reinterpret_cast<const char*>(name->Name), "sendto") != 0) continue;
            OriginalSendTo = reinterpret_cast<SendToFn>(imports->u1.Function);
            DWORD oldProtection{};
            if (!VirtualProtect(&imports->u1.Function, sizeof(imports->u1.Function),
                                PAGE_READWRITE, &oldProtection))
                return false;
            imports->u1.Function = reinterpret_cast<ULONG_PTR>(&SendToHook);
            VirtualProtect(&imports->u1.Function, sizeof(imports->u1.Function),
                           oldProtection, &oldProtection);
            FlushInstructionCache(GetCurrentProcess(), &imports->u1.Function,
                                  sizeof(imports->u1.Function));
            return true;
        }
    }
    return false;
}

bool InstallConnectHook(HMODULE module, const char* expectedLibrary)
{
    auto* base = reinterpret_cast<BYTE*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (directory.VirtualAddress == 0) return false;
    auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress);
    for (; descriptor->Name != 0; ++descriptor)
    {
        const char* library = reinterpret_cast<const char*>(base + descriptor->Name);
        if (_stricmp(library, expectedLibrary) != 0) continue;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->OriginalFirstThunk);
        auto* imports = reinterpret_cast<IMAGE_THUNK_DATA*>(base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData != 0; ++names, ++imports)
        {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto* name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            if (std::strcmp(reinterpret_cast<const char*>(name->Name), "connect") != 0) continue;
            DWORD oldProtection{};
            if (!VirtualProtect(&imports->u1.Function, sizeof(imports->u1.Function),
                                PAGE_READWRITE, &oldProtection))
                return false;
            imports->u1.Function = reinterpret_cast<ULONG_PTR>(&ConnectHook);
            VirtualProtect(&imports->u1.Function, sizeof(imports->u1.Function),
                           oldProtection, &oldProtection);
            return true;
        }
    }
    return false;
}

bool InstallBotTelemetryHook()
{
    for (int attempt = 0; attempt < 1200; ++attempt)
    {
        HMODULE engine = GetModuleHandleW(L"engine.dll");
        if (engine)
        {
            if (!InstallConnectHook(engine, "wsock32.dll") || !InstallSendToHook(engine))
                return false;
            return InstallLocalActionHook(engine);
        }
        Sleep(100);
    }
    return false;
}

void Log(const char* message)
{
    char temp[MAX_PATH]{};
    if (GetTempPathA(MAX_PATH, temp) == 0) return;
    std::ofstream out(std::string(temp) + "rakion_client_compat.log", std::ios::app);
    out << message << '\n';
}

bool IsRakionProcess()
{
    wchar_t path[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, path, MAX_PATH) == 0) return false;
    const wchar_t* name = wcsrchr(path, L'\\');
    return _wcsicmp(name ? name + 1 : path, L"rakion.exe") == 0;
}

void Emit(std::vector<BYTE>& code, std::initializer_list<BYTE> bytes)
{
    code.insert(code.end(), bytes.begin(), bytes.end());
}

void Emit32(std::vector<BYTE>& code, uint32_t value)
{
    const auto* bytes = reinterpret_cast<const BYTE*>(&value);
    code.insert(code.end(), bytes, bytes + sizeof(value));
}

size_t ShortBranch(std::vector<BYTE>& code, BYTE opcode)
{
    code.push_back(opcode);
    code.push_back(0);
    return code.size() - 1;
}

void PatchBranch(std::vector<BYTE>& code, size_t index, size_t target)
{
    code[index] = static_cast<BYTE>(static_cast<int8_t>(target - (index + 1)));
}

void EmitJump(std::vector<BYTE>& code, uintptr_t source, uintptr_t target)
{
    code.push_back(0xe9);
    Emit32(code, static_cast<uint32_t>(target - (source + 5)));
}

void LoadLifecycleSnapshot()
{
    std::ifstream input(LifecyclePath);
    int seat{};
    int generation{};
    unsigned long sequence{};
    int dead{};
    while (input >> seat >> generation >> sequence >> dead)
    {
        if (seat < 0 || seat >= MaxPlayerSeats || generation < 0 || sequence == 0) continue;
        InterlockedExchange(&DesiredDeadState[seat], dead == 0 ? 0 : 1);
        InterlockedExchange(&DesiredLifecycleSequence[seat], static_cast<LONG>(sequence));
    }
}

void LogAppliedLifecycles()
{
    for (int seat = 0; seat < MaxPlayerSeats; ++seat)
    {
        LONG applied = InterlockedCompareExchange(&AppliedLifecycleSequence[seat], 0, 0);
        LONG logged = InterlockedCompareExchange(&LoggedLifecycleSequence[seat], 0, 0);
        if (applied == 0 || applied == logged) continue;
        InterlockedExchange(&LoggedLifecycleSequence[seat], applied);
        LONG dead = InterlockedCompareExchange(&DesiredDeadState[seat], 0, 0);
        char message[96]{};
        _snprintf_s(message, _countof(message), _TRUNCATE,
            "lifecycle seat=%d seq=%ld state=%s aplicado", seat, applied, dead != 0 ? "dead" : "alive");
        Log(message);
    }
}

void __stdcall ApplyLifecycleOnGameThread(void* player)
{
    __try
    {
        if (!player) return;
        int seat = *reinterpret_cast<BYTE*>(static_cast<BYTE*>(player) + 0x264);
        if (seat < 0 || seat >= MaxPlayerSeats) return;
        LONG desired = InterlockedCompareExchange(&DesiredLifecycleSequence[seat], 0, 0);
        if (desired == 0) return;   // seat sem lifecycle publicado = não é um bot: não tocar
        LONG dead = InterlockedCompareExchange(&DesiredDeadState[seat], 0, 0);

        // GROUND-SNAP por-frame do bot (invariante #7 da golden capture): o avatar do bot é entidade
        // dirigida-por-rede, sem física local, então flutuava sobre o mapa. FallDownToFloor consulta a
        // geometria do CWorld (4 raios p/ baixo) e ajusta SÓ o Y ao chão real — mantém o X/Z que a rede
        // já pôs. Só p/ BOTS (guard desired!=0) e só VIVOS (o morto está na anim de queda). Roda toda frame
        // do update do player, DEPOIS da rede aplicar a posição. NUNCA em humano real (não tem lifecycle
        // publicado) → não quebra pulos deles. Fail-closed se a build de engine.dll não casou o prólogo.
        if (dead == 0 && InterlockedCompareExchange(&GroundSnapEnabled, 0, 0) != 0)
        {
            using GroundFn = void(__thiscall*)(void*);
            reinterpret_cast<GroundFn>(FallDownToFloorAddress)(player);
        }

        LONG applied = InterlockedCompareExchange(&AppliedLifecycleSequence[seat], 0, 0);
        if (desired == applied) return;
        using LifecycleFn = void(__thiscall*)(void*);
        auto transition = reinterpret_cast<LifecycleFn>(dead != 0 ? SetDeadAddress : SetAliveAddress);
        transition(player);
        InterlockedExchange(&AppliedLifecycleSequence[seat], desired);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
    }
}

__declspec(naked) void PlayerUpdateHook()
{
    __asm
    {
        pushfd
        pushad
        push ecx
        call ApplyLifecycleOnGameThread
        popad
        popfd
        push -1
        mov eax, fs:[0]
        jmp dword ptr [PlayerUpdateContinue]
    }
}

bool InstallPlayerUpdateHook()
{
    auto* patch = reinterpret_cast<BYTE*>(PlayerUpdateAddress);
    if (std::memcmp(patch, ExpectedPlayerUpdate, sizeof(ExpectedPlayerUpdate)) != 0) return false;

    DWORD oldProtection{};
    if (!VirtualProtect(patch, sizeof(ExpectedPlayerUpdate), PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    std::vector<BYTE> detour;
    EmitJump(detour, PlayerUpdateAddress, reinterpret_cast<uintptr_t>(&PlayerUpdateHook));
    std::memcpy(patch, detour.data(), detour.size());
    std::memset(patch + detour.size(), 0x90, sizeof(ExpectedPlayerUpdate) - detour.size());
    VirtualProtect(patch, sizeof(ExpectedPlayerUpdate), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(ExpectedPlayerUpdate));
    return true;
}

std::vector<BYTE> BuildCode(uintptr_t cave)
{
    std::vector<BYTE> code;
    code.reserve(128);
    Emit(code, { 0x9c, 0x60 });
    Emit(code, { 0x0f, 0xb6, 0x9e, 0x64, 0x02, 0x00, 0x00, 0x83, 0xfb, 0x14 });
    const auto invalidSeat = ShortBranch(code, 0x73);
    Emit(code, { 0x8b, 0x0d, 0x60, 0xf2, 0x36, 0x36, 0x85, 0xc9 });
    const auto noGame = ShortBranch(code, 0x74);
    Emit(code, { 0x8b, 0x01, 0xff, 0x50, 0x08, 0x69, 0xdb, 0x78, 0x03, 0x00, 0x00 });
    Emit(code, { 0x0f, 0xb7, 0x94, 0x18, 0xec, 0x01, 0x00, 0x00 });
    Emit(code, { 0x66, 0x81, 0xfa, 0x9f, 0x04 });
    const auto firstBotPort = ShortBranch(code, 0x74);
    Emit(code, { 0x66, 0x81, 0xfa, 0x9f, 0x05 });
    const auto otherPort = ShortBranch(code, 0x75);
    const auto botEndpoint = code.size();
    Emit(code, { 0xa1, 0x30, 0x36, 0x2b, 0x35, 0xff, 0xd0, 0x3b, 0xe8 });
    const auto remoteAttacker = ShortBranch(code, 0x75);
    Emit(code, { 0x8b, 0x84, 0x24 });
    Emit32(code, ReceiveDamageStackReturnOffset);
    code.push_back(0x3d);
    Emit32(code, RangedDamageReturnAddress);
    Emit(code, { 0x75, 0x04, 0x6a, 0x0a, 0xeb, 0x02, 0x6a, 0x01, 0x8b, 0xcd });
    Emit(code, { 0xb8, 0xe0, 0x3c, 0x15, 0x35, 0xff, 0xd0 });
    const auto cleanup = code.size();
    PatchBranch(code, invalidSeat, cleanup);
    PatchBranch(code, noGame, cleanup);
    PatchBranch(code, firstBotPort, botEndpoint);
    PatchBranch(code, otherPort, cleanup);
    PatchBranch(code, remoteAttacker, cleanup);
    Emit(code, { 0x61, 0x9d });
    code.insert(code.end(), std::begin(Expected), std::end(Expected));
    EmitJump(code, cave + code.size(), ContinueAddress);
    return code;
}

DWORD WINAPI InstallCompatibility(void*)
{
    if (!IsRakionProcess()) return 0;
    Log(InstallBotTelemetryHook()
        ? "ponte P2P->World para hit de bot instalada"
        : "ponte P2P->World indisponível");
    PatchKeyHook();
    auto* patch = reinterpret_cast<BYTE*>(PatchAddress);
    for (int attempt = 0; attempt < 1200; ++attempt)
    {
        if (std::memcmp(patch, Expected, sizeof(Expected)) == 0) break;
        if (*patch == 0xe9) return 0;
        if (attempt == 1199) { Log("entitiesmp.dll incompatível ou não carregada"); return 1; }
        Sleep(100);
    }

    auto* cave = static_cast<BYTE*>(VirtualAlloc(nullptr, 0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!cave) { Log("VirtualAlloc falhou"); return 2; }
    const auto code = BuildCode(reinterpret_cast<uintptr_t>(cave));
    std::memcpy(cave, code.data(), code.size());

    DWORD oldProtection{};
    if (!VirtualProtect(patch, sizeof(Expected), PAGE_EXECUTE_READWRITE, &oldProtection))
    {
        Log("VirtualProtect falhou");
        return 3;
    }
    std::vector<BYTE> detour;
    EmitJump(detour, PatchAddress, reinterpret_cast<uintptr_t>(cave));
    std::memcpy(patch, detour.data(), detour.size());
    VirtualProtect(patch, sizeof(Expected), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(Expected));
    if (!InstallPlayerUpdateHook())
    {
        Log("hook player-like de lifecycle falhou");
        return 4;
    }
    // Ground-snap: só habilita se a build de engine.dll casar o prólogo de FallDownToFloor (fail-closed —
    // build incompatível não faz o snap, sem patchar endereço desconhecido). Ver critério de não-regressão.
    if (std::memcmp(reinterpret_cast<BYTE*>(FallDownToFloorAddress), ExpectedFallDownToFloor,
                    sizeof(ExpectedFallDownToFloor)) == 0)
    {
        InterlockedExchange(&GroundSnapEnabled, 1);
        Log("ground-snap do bot habilitado (FallDownToFloor verificado)");
    }
    else
    {
        Log("ground-snap DESABILITADO: engine.dll nao casou o prologo de FallDownToFloor");
    }
    Log("compatibilidade HIT/SHOT e lifecycle instalada");
    for (;;)
    {
        LoadLifecycleSnapshot();
        LogAppliedLifecycles();
        Sleep(10);
    }
}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    if (!LoadVersionExports(instance)) return FALSE;
    if (!IsRakionProcess()) return TRUE;
    if (!ApplyFinalClientPatches()) return FALSE;
    ApplyLauncherPatches();
    OutputDebugStringA(LoadServerAddress()
        ? "RakionClientCompat: server.host carregado\n"
        : "RakionClientCompat: server.host inválido ou ausente\n");
    HMODULE ws2 = GetModuleHandleW(L"ws2_32.dll");
    if (ws2) SystemConnect = reinterpret_cast<ConnectFn>(GetProcAddress(ws2, "connect"));
    if (!SystemConnect || !InstallConnectHook(GetModuleHandleW(nullptr), "ws2_32.dll"))
        OutputDebugStringA("RakionClientCompat: redirect TCP inicial indisponível\n");
    DisableThreadLibraryCalls(instance);
    HANDLE thread = CreateThread(nullptr, 0, InstallCompatibility, nullptr, 0, nullptr);
    if (thread) CloseHandle(thread);
    return TRUE;
}
