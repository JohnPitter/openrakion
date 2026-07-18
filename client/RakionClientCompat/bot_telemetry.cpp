#include <winsock2.h>
#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <string>

#include "bot_telemetry.h"
#include "compat_log.h"

namespace
{
constexpr uint16_t BotTelemetryType = 0xb07a;
constexpr uint32_t SendToOtherClientRva = 0x00100780;
constexpr BYTE ExpectedSendToOtherClient[] = { 0x83, 0xec, 0x0c, 0x55, 0x8b, 0xe9 };
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
            CompatLog("primeiro ataque humano espelhado ao World antes do loop P2P");
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

    BYTE detour[sizeof(ExpectedSendToOtherClient)]{ 0xe9 };
    const int32_t displacement = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(&SendToOtherClientHook) -
        (reinterpret_cast<uintptr_t>(patch) + 5));
    std::memcpy(detour + 1, &displacement, sizeof(displacement));
    detour[5] = 0x90;
    std::memcpy(patch, detour, sizeof(detour));
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

bool InstallInitialServerRedirect()
{
    HMODULE ws2 = GetModuleHandleW(L"ws2_32.dll");
    if (ws2) SystemConnect = reinterpret_cast<ConnectFn>(GetProcAddress(ws2, "connect"));
    return SystemConnect && InstallConnectHook(GetModuleHandleW(nullptr), "ws2_32.dll");
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
