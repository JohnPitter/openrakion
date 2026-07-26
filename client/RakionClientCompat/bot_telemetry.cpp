#include <winsock2.h>
#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <string>

#include "action_capture.h"
#include "bot_telemetry.h"
#include "compat_log.h"
#include "headless_bot_driver.h"

namespace
{
constexpr uint16_t BotTelemetryType = 0xb07a;
constexpr uint16_t HeadlessBotTelemetryType = 0xb07b;
constexpr uint32_t SendToOtherClientRva = 0x00100780;
constexpr BYTE ExpectedSendToOtherClient[] = { 0x83, 0xec, 0x0c, 0x55, 0x8b, 0xe9 };
constexpr uint32_t SetActionRva = 0x00102fa0;
constexpr BYTE ExpectedSetAction[] = { 0x83, 0xec, 0x0c, 0x56, 0x57, 0x8b, 0xf1 };
constexpr uint32_t RemoteActionRva = 0x0010dc48;
constexpr BYTE ExpectedRemoteAction[] = {
    0x8b, 0x44, 0x24, 0x48, 0x3a, 0x85, 0x46, 0x29, 0x00, 0x00
};
using SendToFn = int(WSAAPI*)(SOCKET, const char*, int, int, const sockaddr*, int);
using SendFn = int(WSAAPI*)(SOCKET, const char*, int, int);
using RecvFromFn = int(WSAAPI*)(SOCKET, char*, int, int, sockaddr*, int*);
using ConnectFn = int(WSAAPI*)(SOCKET, const sockaddr*, int);
using BindFn = int(WSAAPI*)(SOCKET, const sockaddr*, int);
SendToFn OriginalSendTo{};
SendFn OriginalSend{};
RecvFromFn OriginalRecvFrom{};
ConnectFn SystemConnect{};
BindFn OriginalBind{};
volatile LONG WorldEndpointFamilyPort{};
volatile LONG WorldEndpointAddress{};
volatile LONG WorldSocketValue{-1};
volatile LONG PeerToPeerSocketValue{-1};
volatile LONG ServerAddress{};
volatile LONG PeerToPeerPort{};
volatile LONG TelemetrySequence{};
volatile LONG LocalActionHookEnabled{};
volatile LONG LocalAttackLogged{};
volatile LONG GameplayHandshakeComplete{};
alignas(8) volatile LONG64 LastTelemetryKey{};
uintptr_t SendToOtherClientContinue{};
uintptr_t SetActionContinue{};
uintptr_t RemoteActionContinue{};
SOCKET GameplaySockets[2]{INVALID_SOCKET, INVALID_SOCKET};
sockaddr_in HeadlessPeerEndpoints[32]{};
volatile LONG HeadlessPeerEndpointMask{};

int WSAAPI SendToHook(
    SOCKET socket, const char* buffer, int length, int flags,
    const sockaddr* target, int targetLength);

bool IsRakionServerPort(u_short networkPort)
{
    const auto* bytes = reinterpret_cast<const BYTE*>(&networkPort);
    const uint16_t port = static_cast<uint16_t>(bytes[0] << 8 | bytes[1]);
    return port == 40706 || port == 40708 || port == 40709;
}

void LogConnectEndpoint(const sockaddr* original, const sockaddr* destination, int targetLength)
{
    if (!original || !destination || targetLength < static_cast<int>(sizeof(sockaddr_in)) ||
        original->sa_family != AF_INET || destination->sa_family != AF_INET)
        return;
    const auto& source = *reinterpret_cast<const sockaddr_in*>(original);
    const auto& target = *reinterpret_cast<const sockaddr_in*>(destination);
    const auto* sourceAddress = reinterpret_cast<const BYTE*>(&source.sin_addr.s_addr);
    const auto* targetAddress = reinterpret_cast<const BYTE*>(&target.sin_addr.s_addr);
    const auto* sourcePort = reinterpret_cast<const BYTE*>(&source.sin_port);
    const auto* targetPort = reinterpret_cast<const BYTE*>(&target.sin_port);
    char message[160]{};
    _snprintf_s(message, _countof(message), _TRUNCATE,
        "connect %u.%u.%u.%u:%u -> %u.%u.%u.%u:%u",
        sourceAddress[0], sourceAddress[1], sourceAddress[2], sourceAddress[3],
        static_cast<unsigned>(sourcePort[0] << 8 | sourcePort[1]),
        targetAddress[0], targetAddress[1], targetAddress[2], targetAddress[3],
        static_cast<unsigned>(targetPort[0] << 8 | targetPort[1]));
    CompatLog(message);
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
    LogConnectEndpoint(target, destination, targetLength);
    return SystemConnect(socket, destination, targetLength);
}

int WSAAPI BindHook(SOCKET socket, const sockaddr* endpoint, int endpointLength)
{
    const int result = OriginalBind(socket, endpoint, endpointLength);
    if (result != SOCKET_ERROR && endpoint &&
        endpointLength >= static_cast<int>(sizeof(sockaddr_in)) &&
        endpoint->sa_family == AF_INET)
    {
        const auto& ipv4 = *reinterpret_cast<const sockaddr_in*>(endpoint);
        const auto* bytes = reinterpret_cast<const BYTE*>(&ipv4.sin_port);
        const uint16_t port = static_cast<uint16_t>(bytes[0] << 8 | bytes[1]);
        if (port >= 2300 && port <= 2399)
        {
            InterlockedExchange(&PeerToPeerPort, port);
            InterlockedExchange(
                &PeerToPeerSocketValue, static_cast<LONG>(socket));
        }
    }
    return result;
}

bool SameEndpoint(const sockaddr_in& left, const sockaddr_in& right)
{
    return left.sin_family == right.sin_family && left.sin_port == right.sin_port &&
           left.sin_addr.s_addr == right.sin_addr.s_addr;
}

void RewriteHeadlessRelaySource(
    char* buffer, int received, sockaddr* source, int sourceLength)
{
    if (!IsHeadlessBotDriverEnabled() || !buffer || received < 7 || !source ||
        sourceLength < static_cast<int>(sizeof(sockaddr_in)) ||
        source->sa_family != AF_INET)
        return;

    auto& endpoint = *reinterpret_cast<sockaddr_in*>(source);
    const uint16_t type = *reinterpret_cast<const uint16_t*>(buffer);
    const BYTE seat = static_cast<BYTE>(buffer[6]) & 0x1F;
    const uint16_t sourcePort = ntohs(endpoint.sin_port);
    if (type == 0x030F && sourcePort >= 2300 && sourcePort <= 2399)
    {
        HeadlessPeerEndpoints[seat] = endpoint;
        InterlockedOr(
            &HeadlessPeerEndpointMask, static_cast<LONG>(1UL << seat));
        return;
    }
    if ((type != 0x030A && type != 0x0311) || sourcePort != 40709 ||
        (InterlockedCompareExchange(&HeadlessPeerEndpointMask, 0, 0) &
         static_cast<LONG>(1UL << seat)) == 0)
        return;

    endpoint = HeadlessPeerEndpoints[seat];
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

void CloseGameplaySockets()
{
    for (SOCKET& socket : GameplaySockets)
    {
        if (socket != INVALID_SOCKET) closesocket(socket);
        socket = INVALID_SOCKET;
    }
}

bool ResolveLocalAddress(const sockaddr_in& server, in_addr& localAddress)
{
    SOCKET probe = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (probe == INVALID_SOCKET) return false;
    const int connected = connect(
        probe, reinterpret_cast<const sockaddr*>(&server), sizeof(server));
    sockaddr_in local{};
    int length = sizeof(local);
    const bool resolved = connected != SOCKET_ERROR &&
        getsockname(probe, reinterpret_cast<sockaddr*>(&local), &length) != SOCKET_ERROR &&
        local.sin_family == AF_INET && local.sin_addr.s_addr != INADDR_ANY;
    closesocket(probe);
    if (resolved) localAddress = local.sin_addr;
    return resolved;
}

bool SendGameplayHandshake(
    size_t index, const sockaddr_in& server, const in_addr& localAddress,
    uint16_t networkSlot, uint32_t sessionKey, uint16_t advertisedPort)
{
    SOCKET socket = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (socket == INVALID_SOCKET) return false;

    sockaddr_in local{};
    local.sin_family = AF_INET;
    local.sin_addr = localAddress;
    if (bind(socket, reinterpret_cast<const sockaddr*>(&local), sizeof(local)) ==
        SOCKET_ERROR)
    {
        closesocket(socket);
        return false;
    }

    DWORD timeout = 1000;
    setsockopt(
        socket, SOL_SOCKET, SO_RCVTIMEO,
        reinterpret_cast<const char*>(&timeout), sizeof(timeout));

    BYTE packet[23]{};
    const uint16_t type = static_cast<uint16_t>(0x0201 + index);
    const uint32_t echoData =
        0x48444c53u + static_cast<uint32_t>(networkSlot) + static_cast<uint32_t>(index);
    std::memcpy(packet, &type, sizeof(type));
    std::memcpy(packet + 7, &networkSlot, sizeof(networkSlot));
    std::memcpy(packet + 9, &sessionKey, sizeof(sessionKey));
    std::memcpy(packet + 13, &localAddress.s_addr, sizeof(localAddress.s_addr));
    const uint16_t networkAdvertisedPort = htons(advertisedPort);
    std::memcpy(packet + 17, &networkAdvertisedPort, sizeof(networkAdvertisedPort));
    std::memcpy(packet + 19, &echoData, sizeof(echoData));

    if (SendToHook(
            socket, reinterpret_cast<const char*>(packet), sizeof(packet), 0,
            reinterpret_cast<const sockaddr*>(&server), sizeof(server)) == SOCKET_ERROR)
    {
        closesocket(socket);
        return false;
    }

    BYTE echo[12]{};
    const int received = recv(socket, reinterpret_cast<char*>(echo), sizeof(echo), 0);
    uint32_t echoed{};
    if (received == sizeof(echo)) std::memcpy(&echoed, echo + 2, sizeof(echoed));
    if (received != sizeof(echo) || echo[0] != 0x01 || echo[1] != 0x02 ||
        echo[6] != index || echo[7] != index || echoed != echoData)
    {
        closesocket(socket);
        return false;
    }

    GameplaySockets[index] = socket;
    return true;
}

bool IsBotTelemetryInput(const char* buffer, int length, uint16_t& type, uint32_t& sequence)
{
    if (!buffer || length < 10) return false;
    type = static_cast<uint16_t>(static_cast<BYTE>(buffer[0]) |
        static_cast<uint16_t>(static_cast<BYTE>(buffer[1])) << 8);
    const bool movement = type == 0x030a && length == 26;
    const bool attack = type == 0x0311 && length == 10 &&
        static_cast<BYTE>(buffer[8]) == 1;
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
    if (destination && destination->sa_family == AF_INET &&
        targetLength >= static_cast<int>(sizeof(sockaddr_in)) &&
        length > 0 && length <= 0xffff)
    {
        const auto& endpoint = *reinterpret_cast<const sockaddr_in*>(destination);
        CaptureProviderPacket(
            ntohs(endpoint.sin_port), buffer, static_cast<uint16_t>(length));
    }
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

int WSAAPI SendHook(SOCKET socket, const char* buffer, int length, int flags)
{
    sockaddr_in endpoint{};
    int endpointLength = sizeof(endpoint);
    if (getpeername(
            socket, reinterpret_cast<sockaddr*>(&endpoint), &endpointLength) != SOCKET_ERROR &&
        endpoint.sin_family == AF_INET && length > 0 && length <= 0xffff)
    {
        CaptureProviderPacket(
            ntohs(endpoint.sin_port), buffer, static_cast<uint16_t>(length));
    }
    return OriginalSend(socket, buffer, length, flags);
}

int WSAAPI RecvFromHook(
    SOCKET socket, char* buffer, int length, int flags,
    sockaddr* source, int* sourceLength)
{
    const int received = OriginalRecvFrom(
        socket, buffer, length, flags, source, sourceLength);
    if (received > 0 && received <= 0xffff && source && sourceLength &&
        *sourceLength >= static_cast<int>(sizeof(sockaddr_in)) &&
        source->sa_family == AF_INET)
    {
        const auto& endpoint = *reinterpret_cast<const sockaddr_in*>(source);
        CaptureInboundPacket(
            ntohs(endpoint.sin_port), buffer, static_cast<uint16_t>(received));
        RewriteHeadlessRelaySource(
            buffer, received, source, *sourceLength);
        const uint16_t sourcePort = ntohs(endpoint.sin_port);
        if (sourcePort >= 2300 && sourcePort <= 2399)
            QueueHeadlessPeerPacket(buffer, static_cast<uint16_t>(received));
    }
    return received;
}

int __stdcall MirrorLocalAction(uint32_t type, const void* message)
{
    if (!message) return 0;
    __try
    {
        const auto* bytes = static_cast<const BYTE*>(message);
        const uint16_t payloadLength = *reinterpret_cast<const uint16_t*>(bytes + 0x3ea);
        const BYTE* payload = bytes + 2;
        CapturePeerAction(static_cast<uint16_t>(type), payload, payloadLength);
        const bool gameplayAction = type == 0x030a || type == 0x0311;
        if (IsHeadlessBotDriverEnabled() && gameplayAction &&
            !IsHeadlessGameplayReady())
            return 1;
        if (!gameplayAction || !OriginalSendTo) return 0;
        const bool movement = type == 0x030a && payloadLength == 19;
        const bool attack = type == 0x0311 && payloadLength == 3 && payload[1] == 1;
        if (!movement && !attack) return 0;

        sockaddr_in world{};
        const SOCKET socket = static_cast<SOCKET>(
            InterlockedCompareExchange(&WorldSocketValue, -1, -1));
        if (socket == INVALID_SOCKET || !ReadWorldEndpoint(world)) return 0;

        BYTE gameplay[26]{};
        const uint16_t innerLength = static_cast<uint16_t>(7 + payloadLength);
        gameplay[0] = static_cast<BYTE>(type);
        gameplay[1] = static_cast<BYTE>(type >> 8);
        const uint32_t sequence = static_cast<uint32_t>(InterlockedIncrement(&TelemetrySequence));
        std::memcpy(gameplay + 2, &sequence, sizeof(sequence));
        gameplay[6] = movement ? static_cast<BYTE>(payload[2] & 0x1f) : payload[0];
        std::memcpy(gameplay + 7, payload, payloadLength);

        bool sentDirect = false;
        if (IsHeadlessBotDriverEnabled())
        {
            const SOCKET peerSocket = static_cast<SOCKET>(
                InterlockedCompareExchange(&PeerToPeerSocketValue, -1, -1));
            const ULONG mask = static_cast<ULONG>(
                InterlockedCompareExchange(&HeadlessPeerEndpointMask, 0, 0));
            if (peerSocket != INVALID_SOCKET)
            {
                for (BYTE seat = 0; seat < 32; ++seat)
                {
                    if ((mask & (1UL << seat)) == 0) continue;
                    const sockaddr_in& peer = HeadlessPeerEndpoints[seat];
                    if (OriginalSendTo(
                            peerSocket,
                            reinterpret_cast<const char*>(gameplay),
                            innerLength,
                            0,
                            reinterpret_cast<const sockaddr*>(&peer),
                            sizeof(peer)) != SOCKET_ERROR)
                        sentDirect = true;
                }
            }
        }

        BYTE packet[30]{};
        const uint16_t envelopeType =
            IsHeadlessBotDriverEnabled() && !sentDirect
            ? HeadlessBotTelemetryType
            : BotTelemetryType;
        packet[0] = static_cast<BYTE>(envelopeType);
        packet[1] = static_cast<BYTE>(envelopeType >> 8);
        std::memcpy(packet + 2, &innerLength, sizeof(innerLength));
        std::memcpy(packet + 4, gameplay, innerLength);
        OriginalSendTo(socket, reinterpret_cast<const char*>(packet), innerLength + 4, 0,
            reinterpret_cast<const sockaddr*>(&world), sizeof(world));
        if (attack && InterlockedExchange(&LocalAttackLogged, 1) == 0)
            CompatLog("primeiro ataque humano espelhado ao World para validar hit no bot");
        return 0;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return 0;
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
        mov dword ptr [esp + 28], eax
        popad
        popfd
        test eax, eax
        jne skipOriginalAction
        sub esp, 0x0c
        push ebp
        mov ebp, ecx
        jmp dword ptr [SendToOtherClientContinue]
    skipOriginalAction:
        ret 0x08
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

void __stdcall PrepareSetAction(const void* source, void* action)
{
    ApplyHeadlessBotAction(source, action);
    CapturePlayerAction(source, action);
}

__declspec(naked) void SetActionHook()
{
    __asm
    {
        pushfd
        pushad
        mov eax, dword ptr [esp + 24]
        mov edx, dword ptr [esp + 40]
        push edx
        push eax
        call PrepareSetAction
        popad
        popfd
        sub esp, 0x0c
        push esi
        push edi
        mov esi, ecx
        jmp dword ptr [SetActionContinue]
    }
}

bool InstallSetActionHook(HMODULE engine)
{
    if (!IsActionCaptureEnabled() && !IsHeadlessBotDriverEnabled()) return true;
    auto* patch = reinterpret_cast<BYTE*>(engine) + SetActionRva;
    if (std::memcmp(patch, ExpectedSetAction, sizeof(ExpectedSetAction)) != 0)
        return false;
    SetActionContinue = reinterpret_cast<uintptr_t>(patch + sizeof(ExpectedSetAction));
    DWORD oldProtection{};
    if (!VirtualProtect(
            patch, sizeof(ExpectedSetAction), PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;

    BYTE detour[sizeof(ExpectedSetAction)]{ 0xe9 };
    const int32_t displacement = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(&SetActionHook) -
        (reinterpret_cast<uintptr_t>(patch) + 5));
    std::memcpy(detour + 1, &displacement, sizeof(displacement));
    detour[5] = 0x90;
    detour[6] = 0x90;
    std::memcpy(patch, detour, sizeof(detour));
    VirtualProtect(patch, sizeof(ExpectedSetAction), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(ExpectedSetAction));
    return true;
}

void __stdcall CaptureRemoteAction(const void* action)
{
    CaptureRemotePlayerAction(action);
}

__declspec(naked) void RemoteActionHook()
{
    __asm
    {
        pushfd
        pushad
        lea eax, dword ptr [esp + 0x6c]
        push eax
        call CaptureRemoteAction
        popad
        popfd
        mov eax, dword ptr [esp + 0x48]
        cmp al, byte ptr [ebp + 0x2946]
        jmp dword ptr [RemoteActionContinue]
    }
}

bool InstallRemoteActionHook(HMODULE engine)
{
    if (!IsActionCaptureEnabled()) return true;
    auto* patch = reinterpret_cast<BYTE*>(engine) + RemoteActionRva;
    if (std::memcmp(patch, ExpectedRemoteAction, sizeof(ExpectedRemoteAction)) != 0)
        return false;
    RemoteActionContinue = reinterpret_cast<uintptr_t>(
        patch + sizeof(ExpectedRemoteAction));
    DWORD oldProtection{};
    if (!VirtualProtect(
            patch, sizeof(ExpectedRemoteAction), PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;

    BYTE detour[sizeof(ExpectedRemoteAction)]{ 0xe9 };
    const int32_t displacement = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(&RemoteActionHook) -
        (reinterpret_cast<uintptr_t>(patch) + 5));
    std::memcpy(detour + 1, &displacement, sizeof(displacement));
    std::memset(detour + 5, 0x90, sizeof(detour) - 5);
    std::memcpy(patch, detour, sizeof(detour));
    VirtualProtect(patch, sizeof(ExpectedRemoteAction), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(ExpectedRemoteAction));
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

bool InstallSendHook(HMODULE module)
{
    auto* base = reinterpret_cast<BYTE*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (directory.VirtualAddress == 0) return false;

    auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(
        base + directory.VirtualAddress);
    for (; descriptor->Name != 0; ++descriptor)
    {
        const char* library = reinterpret_cast<const char*>(base + descriptor->Name);
        if (_stricmp(library, "wsock32.dll") != 0) continue;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA*>(
            base + descriptor->OriginalFirstThunk);
        auto* imports = reinterpret_cast<IMAGE_THUNK_DATA*>(
            base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData != 0; ++names, ++imports)
        {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto* name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(
                base + names->u1.AddressOfData);
            if (std::strcmp(reinterpret_cast<const char*>(name->Name), "send") != 0)
                continue;
            OriginalSend = reinterpret_cast<SendFn>(imports->u1.Function);
            DWORD oldProtection{};
            if (!VirtualProtect(
                    &imports->u1.Function, sizeof(imports->u1.Function),
                    PAGE_READWRITE, &oldProtection))
                return false;
            imports->u1.Function = reinterpret_cast<ULONG_PTR>(&SendHook);
            VirtualProtect(
                &imports->u1.Function, sizeof(imports->u1.Function),
                oldProtection, &oldProtection);
            FlushInstructionCache(
                GetCurrentProcess(), &imports->u1.Function,
                sizeof(imports->u1.Function));
            return true;
        }
    }
    return false;
}

bool InstallRecvFromHook(HMODULE module)
{
    auto* base = reinterpret_cast<BYTE*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (directory.VirtualAddress == 0) return false;

    auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(
        base + directory.VirtualAddress);
    for (; descriptor->Name != 0; ++descriptor)
    {
        const char* library = reinterpret_cast<const char*>(base + descriptor->Name);
        if (_stricmp(library, "wsock32.dll") != 0) continue;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA*>(
            base + descriptor->OriginalFirstThunk);
        auto* imports = reinterpret_cast<IMAGE_THUNK_DATA*>(
            base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData != 0; ++names, ++imports)
        {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto* name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(
                base + names->u1.AddressOfData);
            if (std::strcmp(
                    reinterpret_cast<const char*>(name->Name), "recvfrom") != 0)
                continue;
            OriginalRecvFrom = reinterpret_cast<RecvFromFn>(imports->u1.Function);
            DWORD oldProtection{};
            if (!VirtualProtect(
                    &imports->u1.Function, sizeof(imports->u1.Function),
                    PAGE_READWRITE, &oldProtection))
                return false;
            imports->u1.Function = reinterpret_cast<ULONG_PTR>(&RecvFromHook);
            VirtualProtect(
                &imports->u1.Function, sizeof(imports->u1.Function),
                oldProtection, &oldProtection);
            FlushInstructionCache(
                GetCurrentProcess(), &imports->u1.Function,
                sizeof(imports->u1.Function));
            return true;
        }
    }
    return false;
}

bool InstallBindHook(HMODULE module)
{
    auto* base = reinterpret_cast<BYTE*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (directory.VirtualAddress == 0) return false;

    auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(
        base + directory.VirtualAddress);
    for (; descriptor->Name != 0; ++descriptor)
    {
        const char* library = reinterpret_cast<const char*>(base + descriptor->Name);
        if (_stricmp(library, "wsock32.dll") != 0) continue;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA*>(
            base + descriptor->OriginalFirstThunk);
        auto* imports = reinterpret_cast<IMAGE_THUNK_DATA*>(
            base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData != 0; ++names, ++imports)
        {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto* name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(
                base + names->u1.AddressOfData);
            if (std::strcmp(
                    reinterpret_cast<const char*>(name->Name), "bind") != 0)
                continue;
            OriginalBind = reinterpret_cast<BindFn>(imports->u1.Function);
            DWORD oldProtection{};
            if (!VirtualProtect(
                    &imports->u1.Function, sizeof(imports->u1.Function),
                    PAGE_READWRITE, &oldProtection))
                return false;
            imports->u1.Function = reinterpret_cast<ULONG_PTR>(&BindHook);
            VirtualProtect(
                &imports->u1.Function, sizeof(imports->u1.Function),
                oldProtection, &oldProtection);
            FlushInstructionCache(
                GetCurrentProcess(), &imports->u1.Function,
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

bool TryGetWorldLocalPort(uint16_t& port)
{
    port = 0;
    const SOCKET socket = static_cast<SOCKET>(
        InterlockedCompareExchange(&WorldSocketValue, -1, -1));
    if (socket == INVALID_SOCKET) return false;
    HMODULE ws2 = GetModuleHandleW(L"ws2_32.dll");
    using GetSockNameFn = int(WSAAPI*)(SOCKET, sockaddr*, int*);
    auto getSocketName = ws2
        ? reinterpret_cast<GetSockNameFn>(GetProcAddress(ws2, "getsockname"))
        : nullptr;
    if (!getSocketName) return false;
    sockaddr_in endpoint{};
    int length = sizeof(endpoint);
    if (getSocketName(socket, reinterpret_cast<sockaddr*>(&endpoint), &length) == SOCKET_ERROR ||
        endpoint.sin_family != AF_INET)
        return false;
    const auto* bytes = reinterpret_cast<const BYTE*>(&endpoint.sin_port);
    port = static_cast<uint16_t>(bytes[0] << 8 | bytes[1]);
    return port != 0;
}

bool TryGetPeerToPeerPort(uint16_t& port)
{
    const LONG value = InterlockedCompareExchange(&PeerToPeerPort, 0, 0);
    if (value <= 0 || value > 0xffff) return false;
    port = static_cast<uint16_t>(value);
    return true;
}

bool EnsureWorldUdpHandshake(
    uint16_t networkSlot, uint32_t sessionKey, uint16_t advertisedPort)
{
    if (InterlockedCompareExchange(&GameplayHandshakeComplete, 0, 0) != 0)
        return true;
    if (!OriginalSendTo || sessionKey == 0 || advertisedPort == 0)
        return false;

    uint32_t address{};
    if (!TryGetServerAddress(address)) return false;
    sockaddr_in server{};
    server.sin_family = AF_INET;
    server.sin_addr.s_addr = address;
    server.sin_port = htons(40708);

    in_addr localAddress{};
    if (!ResolveLocalAddress(server, localAddress)) return false;

    CloseGameplaySockets();
    for (size_t index = 0; index < _countof(GameplaySockets); ++index)
    {
        server.sin_port = htons(static_cast<u_short>(40708 + index));
        if (!SendGameplayHandshake(
                index, server, localAddress, networkSlot, sessionKey, advertisedPort))
        {
            CloseGameplaySockets();
            return false;
        }
    }

    InterlockedExchange(&GameplayHandshakeComplete, 1);
    CompatLog("handshake UDP headless autenticado nas duas portas do World");
    return true;
}

bool TryGetServerAddress(uint32_t& address)
{
    const LONG value = InterlockedCompareExchange(&ServerAddress, 0, 0);
    if (value == 0) return false;
    address = static_cast<uint32_t>(value);
    return true;
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
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    const bool executableInstalled = SystemConnect &&
        InstallConnectHook(GetModuleHandleW(nullptr), "ws2_32.dll");
    const bool engineInstalled = engine && InstallConnectHook(engine, "wsock32.dll");
    return executableInstalled && engineInstalled;
}

bool InstallBotTelemetryHook()
{
    for (int attempt = 0; attempt < 1200; ++attempt)
    {
        HMODULE engine = GetModuleHandleW(L"engine.dll");
        if (engine)
        {
            if (!InstallConnectHook(engine, "wsock32.dll") ||
                !InstallSendToHook(engine) || !InstallSendHook(engine) ||
                !InstallRecvFromHook(engine) || !InstallBindHook(engine))
                return false;
            return InstallLocalActionHook(engine) && InstallSetActionHook(engine) &&
                InstallRemoteActionHook(engine) &&
                InstallHeadlessRemotePlayerTrace(engine);
        }
        Sleep(100);
    }
    return false;
}
