#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <cstdlib>

#include "action_capture.h"

namespace
{
bool CaptureEnabled()
{
    static const bool enabled = []
    {
        char value[8]{};
        DWORD length = GetEnvironmentVariableA(
            "OPENRAKION_CAPTURE_ACTIONS", value, static_cast<DWORD>(sizeof(value)));
        return length == 1 && value[0] == '1';
    }();
    return enabled;
}

void BuildCapturePath(char (&path)[MAX_PATH])
{
    char temporary[MAX_PATH]{};
    DWORD length = GetTempPathA(static_cast<DWORD>(sizeof(temporary)), temporary);
    if (length == 0 || length >= sizeof(temporary)) strcpy_s(temporary, ".\\");
    _snprintf_s(path, _countof(path), _TRUNCATE,
        "%sopenrakion_action_capture_%lu.csv", temporary, GetCurrentProcessId());
}
}

void CapturePeerAction(uint16_t type, const void* payload, uint16_t payloadLength)
{
    if (!CaptureEnabled() || !payload ||
        (type != 0x030a && type != 0x030f && type != 0x0311))
        return;

    char path[MAX_PATH]{};
    BuildCapturePath(path);
    FILE* file{};
    if (fopen_s(&file, path, "ab") != 0 || !file) return;

    const auto* bytes = static_cast<const BYTE*>(payload);
    std::fprintf(file, "%lu,%04X,%u,", GetTickCount(), type, payloadLength);
    for (uint16_t index = 0; index < payloadLength; ++index)
        std::fprintf(file, "%02X", bytes[index]);
    std::fputs("\r\n", file);
    std::fclose(file);
}
