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
constexpr uint32_t CharacterCreationOwnerResetRva = 0x000685bf;
constexpr uintptr_t CharacterCreationCancelAddress = 0x00468b45;
constexpr char LegacyGameGuardUrl[] = "http://218.145.66.176:10200";
constexpr char DisabledGameGuardUrl[] = "http://127.0.0.1:1";
constexpr BYTE CharacterCreationOwnerReset[] = { 0x89, 0xae, 0x00, 0x0a, 0x00, 0x00 };
constexpr BYTE PreserveCharacterCreationOwner[] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
constexpr BYTE CharacterCreationCancelExpected[] = { 0x8b, 0x8e, 0x00, 0x0a, 0x00, 0x00 };

__declspec(naked) void CharacterCreationCancelHook()
{
    __asm
    {
        mov ecx, dword ptr [esi + 0a00h]
        test ecx, ecx
        jz no_owner
        movzx ebx, byte ptr [ecx + 022ch]
        mov edx, dword ptr [ecx]
        push 1
        call dword ptr [edx + 0ch]
        mov dword ptr [esi + 0a00h], 0
        push ebx
        mov ecx, esi
        mov eax, 00466fa0h
        call eax
    no_owner:
        mov eax, 00468e10h
        jmp eax
    }
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

bool ApplyRelativeJump(BYTE* address, const BYTE* expected, SIZE_T length, uintptr_t target)
{
    if (length < 5) return false;
    BYTE replacement[6]{ 0xe9 };
    const auto source = reinterpret_cast<uintptr_t>(address);
    const auto displacement = static_cast<uint32_t>(target - (source + 5));
    std::memcpy(replacement + 1, &displacement, sizeof(displacement));
    replacement[5] = 0x90;
    return ApplyBlockPatch(address, 0, expected, replacement, length);
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

bool ApplyCharacterCreationCancelRecovery()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    const bool ownerPreserved = ApplyBlockPatch(
        image, CharacterCreationOwnerResetRva, CharacterCreationOwnerReset,
        PreserveCharacterCreationOwner, sizeof(PreserveCharacterCreationOwner));
    const bool previewRestored = ApplyRelativeJump(
        reinterpret_cast<BYTE*>(CharacterCreationCancelAddress),
        CharacterCreationCancelExpected, sizeof(CharacterCreationCancelExpected),
        reinterpret_cast<uintptr_t>(&CharacterCreationCancelHook));
    const bool applied = ownerPreserved && previewRestored;
    CompatLog(applied
        ? "Cancel da criacao e restauracao do preview instalados"
        : "recuperacao do Cancel indisponivel para esta build");
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
