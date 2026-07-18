#include <windows.h>

#include <iterator>

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
constexpr const char* VersionExports[] = {
    "GetFileVersionInfoA", "GetFileVersionInfoByHandle", "GetFileVersionInfoExA",
    "GetFileVersionInfoExW", "GetFileVersionInfoSizeA", "GetFileVersionInfoSizeExA",
    "GetFileVersionInfoSizeExW", "GetFileVersionInfoSizeW", "GetFileVersionInfoW",
    "VerFindFileA", "VerFindFileW", "VerInstallFileA", "VerInstallFileW",
    "VerLanguageNameA", "VerLanguageNameW", "VerQueryValueA", "VerQueryValueW"
};

bool LoadVersionExports()
{
    wchar_t path[MAX_PATH]{};
    const UINT length = GetSystemDirectoryW(path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return false;
    if (wcscat_s(path, L"\\version.dll") != 0) return false;
    HMODULE original = LoadLibraryW(path);
    if (!original) return false;
    for (size_t i = 0; i < std::size(VersionExports); ++i)
    {
        g_versionExports[i] = GetProcAddress(original, VersionExports[i]);
        if (!g_versionExports[i]) return false;
    }
    return true;
}

bool LoadClientPatch()
{
    wchar_t path[MAX_PATH]{};
    const DWORD length = GetModuleFileNameW(nullptr, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return false;
    wchar_t* separator = wcsrchr(path, L'\\');
    if (!separator) return false;
    separator[1] = L'\0';
    if (wcscat_s(path, L"RakionClientPatch.dll") != 0) return false;
    return LoadLibraryW(path) != nullptr;
}
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    if (!LoadVersionExports()) return FALSE;
    DisableThreadLibraryCalls(instance);
    return LoadClientPatch();
}
