#include <windows.h>

#include <cstdint>
#include <cstring>
#include <fstream>
#include <string>

#include "baked_patches.h"
#include "client_patches.h"
#include "compat_log.h"

namespace
{
constexpr uint32_t WindowedRva = 0x00d46d;
constexpr uint32_t NoDisplayResetRvas[] = { 0x00dbc2, 0x00dc1e, 0x00dc4f };
constexpr uint32_t MultiInstanceRva = 0x002c96;
constexpr uint32_t LegacyGameGuardUrlRva = 0x000d4028;
constexpr uint32_t CharacterCreationCleanupRva = 0x000685b8;
constexpr char LegacyGameGuardUrl[] = "http://218.145.66.176:10200";
constexpr char DisabledGameGuardUrl[] = "http://127.0.0.1:1";
constexpr BYTE CharacterCreationCleanupExpected[] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
constexpr BYTE CharacterCreationCleanupPatch[] = { 0x8b, 0x01, 0xff, 0x50, 0x0c, 0x90, 0x90 };

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

bool ApplyBlockPatch(BYTE* image, uint32_t rva, const BYTE* expected,
                     const BYTE* replacement, SIZE_T length)
{
    BYTE* address = image + rva;
    if (std::memcmp(address, replacement, length) == 0) return true;
    if (std::memcmp(address, expected, length) != 0) return false;
    DWORD oldProtection{};
    if (!VirtualProtect(address, length, PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    std::memcpy(address, replacement, length);
    VirtualProtect(address, length, oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), address, length);
    return true;
}

bool DisableLegacyGameGuardUrl(BYTE* image)
{
    char* address = reinterpret_cast<char*>(image + LegacyGameGuardUrlRva);
    if (std::memcmp(address, DisabledGameGuardUrl, sizeof(DisabledGameGuardUrl)) == 0)
        return true;
    if (std::memcmp(address, LegacyGameGuardUrl, sizeof(LegacyGameGuardUrl)) != 0)
        return false;
    DWORD oldProtection{};
    if (!VirtualProtect(address, sizeof(LegacyGameGuardUrl), PAGE_READWRITE, &oldProtection))
        return false;
    std::memset(address, 0, sizeof(LegacyGameGuardUrl));
    std::memcpy(address, DisabledGameGuardUrl, sizeof(DisabledGameGuardUrl));
    VirtualProtect(address, sizeof(LegacyGameGuardUrl), oldProtection, &oldProtection);
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

bool ApplyCharacterCreationUiFix()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    const bool applied = ApplyBlockPatch(
        image, CharacterCreationCleanupRva, CharacterCreationCleanupExpected,
        CharacterCreationCleanupPatch, sizeof(CharacterCreationCleanupPatch));
    CompatLog(applied
        ? "fechamento da criacao apos criar personagem instalado"
        : "fechamento da criacao indisponivel para esta build");
    return applied;
}

bool ApplyLauncherPatches()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image || !ApplyBytePatch(image, MultiInstanceRva, 0xb7, 0xff) ||
        !DisableLegacyGameGuardUrl(image))
        return false;
    CompatLog("URL residual do GameGuard neutralizada");
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
    if (!module) { CompatLog("keyhook.dll não carregada"); return; }
    auto* image = reinterpret_cast<BYTE*>(module);
    const bool first = ApplyBytePatch(image, 0x106e, 0x01, 0x00);
    const bool second = ApplyBytePatch(image, 0x10a3, 0x01, 0x00);
    CompatLog(first && second ? "Alt+Tab liberado pela DLL" : "patch de Alt+Tab incompatível");
}
