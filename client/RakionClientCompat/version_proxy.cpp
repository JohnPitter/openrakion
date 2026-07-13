#include <windows.h>

#include <cstdint>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

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
constexpr uint32_t ReceiveDamageStackReturnOffset = 0x4d4;
constexpr BYTE Expected[] = { 0x68, 0x30, 0xa6, 0x2b, 0x35 };
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
    Log("compatibilidade HIT/SHOT instalada");
    return 0;
}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    if (!LoadVersionExports(instance)) return FALSE;
    DisableThreadLibraryCalls(instance);
    HANDLE thread = CreateThread(nullptr, 0, InstallCompatibility, nullptr, 0, nullptr);
    if (thread) CloseHandle(thread);
    return TRUE;
}
