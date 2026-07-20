#include <windows.h>

#include <array>
#include <cstdint>
#include <cstring>

#include "compat_log.h"
#include "ui_lifecycle_patch.h"

namespace
{
constexpr uint32_t ToolkitDestructorHookRva = 0x020ba3;
constexpr uint32_t ToolkitCodeCaveRva = 0x05059b;
constexpr BYTE ToolkitDestructorOriginal[] = { 0x8b, 0x86, 0xd8, 0x00, 0x00, 0x00 };
constexpr BYTE ToolkitDestructorHook[] = { 0xe9, 0xf3, 0xf9, 0x02, 0x00, 0x90 };
constexpr BYTE CharacterUiNops[] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
constexpr BYTE VirtualCloseViaEdx[] = { 0x8b, 0x11, 0x6a, 0x01, 0xff, 0x52, 0x0c };
constexpr BYTE VirtualCloseViaEax[] = { 0x8b, 0x01, 0x6a, 0x01, 0xff, 0x50, 0x0c };
constexpr BYTE Enabled = 0x01;

constexpr std::array<BYTE, 132> ToolkitUnlinkCode = {
    0x60, 0x8b, 0x86, 0x44, 0x01, 0x00, 0x00, 0x8b, 0x96, 0x40, 0x01, 0x00, 0x00, 0x85, 0xc0, 0x0f,
    0x84, 0x63, 0x00, 0x00, 0x00, 0x85, 0xd2, 0x0f, 0x84, 0x5b, 0x00, 0x00, 0x00, 0x8b, 0xd8, 0x0b,
    0xda, 0xf6, 0xc3, 0x03, 0x0f, 0x85, 0x4e, 0x00, 0x00, 0x00, 0x89, 0x90, 0x40, 0x01, 0x00, 0x00,
    0x89, 0x82, 0x44, 0x01, 0x00, 0x00, 0x8b, 0x8e, 0x48, 0x01, 0x00, 0x00, 0x85, 0xc9, 0x74, 0x24,
    0x8b, 0xfa, 0x3b, 0xd6, 0x75, 0x02, 0x33, 0xff, 0x39, 0xb1, 0x38, 0x01, 0x00, 0x00, 0x75, 0x06,
    0x89, 0xb9, 0x38, 0x01, 0x00, 0x00, 0x39, 0xb1, 0x3c, 0x01, 0x00, 0x00, 0x75, 0x06, 0x89, 0xb9,
    0x3c, 0x01, 0x00, 0x00, 0xc7, 0x86, 0x40, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xc7, 0x86,
    0x44, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x61, 0x8b, 0x86, 0xd8, 0x00, 0x00, 0x00, 0xe9,
    0x8a, 0x05, 0xfd, 0xff
};

struct CharacterUiRestore
{
    uint32_t Rva;
    const BYTE* Bytes;
    SIZE_T Length;
};

constexpr CharacterUiRestore CharacterUiRestores[] = {
    { 0x0685b8, VirtualCloseViaEdx, sizeof(VirtualCloseViaEdx) },
    { 0x06861f, VirtualCloseViaEax, sizeof(VirtualCloseViaEax) },
    { 0x068738, VirtualCloseViaEax, sizeof(VirtualCloseViaEax) },
    { 0x068776, &Enabled, 1 },
    { 0x0687de, VirtualCloseViaEax, sizeof(VirtualCloseViaEax) },
    { 0x0688fc, VirtualCloseViaEdx, sizeof(VirtualCloseViaEdx) }
};

bool WriteValidated(BYTE* address, const BYTE* expected, const BYTE* replacement, SIZE_T length)
{
    if (std::memcmp(address, replacement, length) == 0) return true;
    if (std::memcmp(address, expected, length) != 0) return false;
    DWORD oldProtection{};
    if (!VirtualProtect(address, length, PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    std::memcpy(address, replacement, length);
    VirtualProtect(address, length, oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), address, length);
    return true;
}

bool MatchesEither(const BYTE* address, const BYTE* expected,
                   const BYTE* replacement, SIZE_T length)
{
    return std::memcmp(address, expected, length) == 0 ||
           std::memcmp(address, replacement, length) == 0;
}

HMODULE WaitForUiToolkit()
{
    for (int attempt = 0; attempt < 300; ++attempt)
    {
        if (HMODULE module = GetModuleHandleW(L"uitoolkit.dll")) return module;
        Sleep(100);
    }
    return nullptr;
}

bool ApplyToolkitUnlinkFix(BYTE* toolkit)
{
    BYTE* cave = toolkit + ToolkitCodeCaveRva;
    std::array<BYTE, ToolkitUnlinkCode.size()> emptyCave{};
    if (!MatchesEither(cave, emptyCave.data(), ToolkitUnlinkCode.data(), ToolkitUnlinkCode.size()) ||
        !MatchesEither(toolkit + ToolkitDestructorHookRva, ToolkitDestructorOriginal,
                       ToolkitDestructorHook, sizeof(ToolkitDestructorHook)))
        return false;
    if (!WriteValidated(cave, emptyCave.data(), ToolkitUnlinkCode.data(), ToolkitUnlinkCode.size()))
        return false;
    return WriteValidated(toolkit + ToolkitDestructorHookRva, ToolkitDestructorOriginal,
                          ToolkitDestructorHook, sizeof(ToolkitDestructorHook));
}

bool CanRestoreCharacterUi(const BYTE* image)
{
    constexpr BYTE Zero = 0x00;
    for (const auto& restore : CharacterUiRestores)
    {
        const BYTE* expected = restore.Length == 1 ? &Zero : CharacterUiNops;
        if (!MatchesEither(image + restore.Rva, expected, restore.Bytes, restore.Length))
            return false;
    }
    return true;
}

bool RestoreCharacterUi(BYTE* image)
{
    constexpr BYTE Zero = 0x00;
    for (const auto& restore : CharacterUiRestores)
    {
        const BYTE* expected = restore.Length == 1 ? &Zero : CharacterUiNops;
        if (!WriteValidated(image + restore.Rva, expected, restore.Bytes, restore.Length))
            return false;
    }
    return true;
}
}

bool ApplyCharacterUiLifecycleFix()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    auto* toolkit = reinterpret_cast<BYTE*>(WaitForUiToolkit());
    if (!image || !toolkit || !CanRestoreCharacterUi(image) ||
        !ApplyToolkitUnlinkFix(toolkit) || !RestoreCharacterUi(image))
    {
        CompatLog("lifecycle da UI de personagens incompatível com esta build");
        return false;
    }
    CompatLog("lifecycle original da UI de personagens restaurado");
    return true;
}
