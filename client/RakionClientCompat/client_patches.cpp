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
constexpr uint32_t WorldNetAccessorRva = 0x00071b70;
constexpr uint32_t IsBattleMapIatRva = 0x000d0880;
constexpr uint32_t AddBotCreateHookRva = 0x00047329;
constexpr uint32_t AddBotCreateCaveRva = 0x00115207;
constexpr uint32_t AddBotCreateContinueRva = 0x00047330;
constexpr uint32_t AddBotClickHookRva = 0x00047bf5;
constexpr uint32_t AddBotClickCaveRva = 0x00115380;
constexpr uint32_t AddBotClickContinueRva = 0x00047bfb;
constexpr BYTE ExpectedAddBotCreateHook[] = { 0xe9, 0xd9, 0xde, 0x0c, 0x00, 0x90, 0x90 };
constexpr BYTE ExpectedAddBotClickHook[] = { 0xe9, 0x86, 0xd7, 0x0c, 0x00, 0x90 };
constexpr char LegacyGameGuardUrl[] = "http://218.145.66.176:10200";
constexpr char DisabledGameGuardUrl[] = "http://127.0.0.1:1";
constexpr char HeadlessVariable[] = "OPENRAKION_HEADLESS";
uintptr_t AddBotCreateCave{};
uintptr_t AddBotCreateContinue{};
uintptr_t AddBotClickCave{};
uintptr_t AddBotClickContinue{};

using WorldNetAccessor = void* (__cdecl*)();
using GetFieldInfo = void* (__thiscall*)(void*);
using IsBattleMap = int (__cdecl*)(uint32_t);

bool IsCurrentRoomBattle()
{
    char headless[2]{};
    if (GetEnvironmentVariableA(
            HeadlessVariable, headless, static_cast<DWORD>(sizeof(headless))) == 1 &&
        headless[0] == '1')
        return false;
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    void* world = reinterpret_cast<WorldNetAccessor>(image + WorldNetAccessorRva)();
    if (!world) return false;
    const auto vtable = *reinterpret_cast<uintptr_t**>(world);
    void* field = reinterpret_cast<GetFieldInfo>(vtable[2])(world);
    if (!field) return false;
    const uint32_t map = *(static_cast<BYTE*>(field) + 0x1a3);
    const auto isBattleMap = *reinterpret_cast<IsBattleMap*>(image + IsBattleMapIatRva);
    return isBattleMap && isBattleMap(map) != 0;
}

__declspec(naked) void AddBotCreateGate()
{
    __asm
    {
        pushfd
        pushad
        call IsCurrentRoomBattle
        test al, al
        jz hidden
        popad
        popfd
        jmp dword ptr [AddBotCreateCave]
    hidden:
        popad
        popfd
        mov ecx, dword ptr [esp + 0xac]
        jmp dword ptr [AddBotCreateContinue]
    }
}

__declspec(naked) void AddBotClickGate()
{
    __asm
    {
        pushfd
        pushad
        call IsCurrentRoomBattle
        test al, al
        jz disabled
        popad
        popfd
        jmp dword ptr [AddBotClickCave]
    disabled:
        popad
        popfd
        mov ecx, dword ptr ds:[0x004feed0]
        jmp dword ptr [AddBotClickContinue]
    }
}

bool WriteRelativeJump(BYTE* address, const BYTE* expected, SIZE_T length, const void* target)
{
    if (std::memcmp(address, expected, length) != 0) return false;
    DWORD oldProtection{};
    if (!VirtualProtect(address, length, PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    address[0] = 0xe9;
    *reinterpret_cast<int32_t*>(address + 1) = static_cast<int32_t>(
        reinterpret_cast<const BYTE*>(target) - (address + 5));
    if (length > 5) std::memset(address + 5, 0x90, length - 5);
    VirtualProtect(address, length, oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), address, length);
    return true;
}

bool InstallAddBotRoomModeGate(BYTE* image)
{
    AddBotCreateCave = reinterpret_cast<uintptr_t>(image + AddBotCreateCaveRva);
    AddBotCreateContinue = reinterpret_cast<uintptr_t>(image + AddBotCreateContinueRva);
    AddBotClickCave = reinterpret_cast<uintptr_t>(image + AddBotClickCaveRva);
    AddBotClickContinue = reinterpret_cast<uintptr_t>(image + AddBotClickContinueRva);
    return WriteRelativeJump(image + AddBotCreateHookRva, ExpectedAddBotCreateHook,
               sizeof(ExpectedAddBotCreateHook), &AddBotCreateGate) &&
        WriteRelativeJump(image + AddBotClickHookRva, ExpectedAddBotClickHook,
               sizeof(ExpectedAddBotClickHook), &AddBotClickGate);
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
    if (!InstallAddBotRoomModeGate(image))
    {
        CompatLog("gate de Add Bot por modo de sala incompatível");
        return false;
    }
    OutputDebugStringA("RakionClientCompat: patches do rakion-final aplicados\n");
    CompatLog("Add Bot habilitado somente em Battle rooms");
    return true;
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
