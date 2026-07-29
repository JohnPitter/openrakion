#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#include "action_capture.h"

namespace
{
bool BuildMarkerPath(char (&path)[MAX_PATH])
{
    if (GetModuleFileNameA(nullptr, path, static_cast<DWORD>(sizeof(path))) == 0)
        return false;
    char* separator = std::strrchr(path, '\\');
    if (!separator)
        return false;
    strcpy_s(separator + 1, MAX_PATH - static_cast<size_t>(separator + 1 - path),
        "action.capture");
    return true;
}

bool HasCaptureMarker()
{
    char path[MAX_PATH]{};
    return BuildMarkerPath(path) &&
        GetFileAttributesA(path) != INVALID_FILE_ATTRIBUTES;
}

bool CaptureEnabled()
{
    static volatile LONG enabled = -1;
    static volatile LONG nextCheck = 0;
    const LONG now = static_cast<LONG>(GetTickCount());
    const LONG current = InterlockedCompareExchange(&enabled, -1, -1);
    if (current >= 0 &&
        static_cast<LONG>(now - InterlockedCompareExchange(
            &nextCheck, 0, 0)) < 0)
        return current != 0;

    char value[8]{};
    DWORD length = GetEnvironmentVariableA(
        "OPENRAKION_CAPTURE_ACTIONS", value, static_cast<DWORD>(sizeof(value)));
    const bool active = (length == 1 && value[0] == '1') ||
        HasCaptureMarker();
    InterlockedExchange(&enabled, active ? 1 : 0);
    InterlockedExchange(&nextCheck, now + 250);
    return active;
}

void ResolveCaptureDirectory(char (&directory)[MAX_PATH])
{
    char marker[MAX_PATH]{};
    if (BuildMarkerPath(marker))
    {
        FILE* file{};
        if (fopen_s(&file, marker, "rb") == 0 && file)
        {
            if (std::fgets(directory, static_cast<int>(sizeof(directory)), file))
            {
                directory[strcspn(directory, "\r\n")] = '\0';
                std::fclose(file);
                if (directory[0] != '\0' &&
                    GetFileAttributesA(directory) != INVALID_FILE_ATTRIBUTES)
                    return;
            }
            else
            {
                std::fclose(file);
            }
        }
    }

    DWORD length = GetTempPathA(
        static_cast<DWORD>(sizeof(directory)), directory);
    if (length == 0 || length >= sizeof(directory))
        strcpy_s(directory, ".\\");
}

void BuildCapturePath(
    char (&path)[MAX_PATH],
    const char* streamName)
{
    char directory[MAX_PATH]{};
    ResolveCaptureDirectory(directory);
    const size_t length = std::strlen(directory);
    const bool hasSeparator = length > 0 &&
        (directory[length - 1] == '\\' || directory[length - 1] == '/');
    _snprintf_s(path, _countof(path), _TRUNCATE,
        "%s%s%s_%lu.csv",
        directory,
        hasSeparator ? "" : "\\",
        streamName,
        GetCurrentProcessId());
}
}

bool IsActionCaptureEnabled()
{
    return CaptureEnabled();
}

void CapturePeerAction(uint16_t type, const void* payload, uint16_t payloadLength)
{
    if (!CaptureEnabled() || !payload ||
        (type != 0x030a && type != 0x030f && type != 0x0311))
        return;

    char path[MAX_PATH]{};
    BuildCapturePath(path, "openrakion_action_capture");
    FILE* file{};
    if (fopen_s(&file, path, "ab") != 0 || !file) return;

    const auto* bytes = static_cast<const BYTE*>(payload);
    std::fprintf(file, "%lu,%04X,%u,", GetTickCount(), type, payloadLength);
    for (uint16_t index = 0; index < payloadLength; ++index)
        std::fprintf(file, "%02X", bytes[index]);
    std::fputs("\r\n", file);
    std::fclose(file);
}

void CapturePlayerAction(const void* source, const void* action)
{
    constexpr size_t ActionSize = 76;
    if (!CaptureEnabled() || !source || !action) return;

    char path[MAX_PATH]{};
    BuildCapturePath(path, "openrakion_player_action");

    FILE* file{};
    if (fopen_s(&file, path, "ab") != 0 || !file) return;
    const auto* bytes = static_cast<const BYTE*>(action);
    std::fprintf(file, "%lu,%p,", GetTickCount(), source);
    for (size_t index = 0; index < ActionSize; ++index)
        std::fprintf(file, "%02X", bytes[index]);
    std::fputs("\r\n", file);
    std::fclose(file);
}

void CaptureRemotePlayerAction(const void* action)
{
    constexpr size_t ActionSize = 72;
    if (!CaptureEnabled() || !action) return;

    char path[MAX_PATH]{};
    BuildCapturePath(path, "openrakion_remote_action");

    FILE* file{};
    if (fopen_s(&file, path, "ab") != 0 || !file) return;
    const auto* bytes = static_cast<const BYTE*>(action);
    std::fprintf(file, "%lu,", GetTickCount());
    for (size_t index = 0; index < ActionSize; ++index)
        std::fprintf(file, "%02X", bytes[index]);
    std::fputs("\r\n", file);
    std::fclose(file);
}

void CaptureProviderPacket(
    uint16_t targetPort, const void* payload, uint16_t payloadLength)
{
    const bool peerToPeer = targetPort >= 2300 && targetPort <= 2399;
    const bool provider = targetPort >= 10000 &&
        targetPort != 40706 && targetPort != 40708;
    if (!CaptureEnabled() || (!peerToPeer && !provider) ||
        !payload || payloadLength == 0)
        return;

    char path[MAX_PATH]{};
    BuildCapturePath(path, "openrakion_provider_send");

    FILE* file{};
    if (fopen_s(&file, path, "ab") != 0 || !file) return;
    const auto* bytes = static_cast<const BYTE*>(payload);
    std::fprintf(file, "%lu,%u,%u,", GetTickCount(), targetPort, payloadLength);
    for (uint16_t index = 0; index < payloadLength; ++index)
        std::fprintf(file, "%02X", bytes[index]);
    std::fputs("\r\n", file);
    std::fclose(file);
}

void CaptureInboundPacket(
    uint16_t sourcePort, const void* payload, uint16_t payloadLength)
{
    const bool peerToPeer = sourcePort >= 2300 && sourcePort <= 2399;
    const bool provider = sourcePort >= 10000 && sourcePort != 40706;
    if (!CaptureEnabled() || (!peerToPeer && !provider) ||
        !payload || payloadLength == 0)
        return;

    char path[MAX_PATH]{};
    BuildCapturePath(path, "openrakion_socket_receive");

    FILE* file{};
    if (fopen_s(&file, path, "ab") != 0 || !file) return;
    const auto* bytes = static_cast<const BYTE*>(payload);
    std::fprintf(file, "%lu,%u,%u,", GetTickCount(), sourcePort, payloadLength);
    for (uint16_t index = 0; index < payloadLength; ++index)
        std::fprintf(file, "%02X", bytes[index]);
    std::fputs("\r\n", file);
    std::fclose(file);
}
