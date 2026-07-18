#include <windows.h>

#include <array>

int wmain()
{
    constexpr std::array names = {
        "GetFileVersionInfoA", "GetFileVersionInfoByHandle", "GetFileVersionInfoExA",
        "GetFileVersionInfoExW", "GetFileVersionInfoSizeA", "GetFileVersionInfoSizeExA",
        "GetFileVersionInfoSizeExW", "GetFileVersionInfoSizeW", "GetFileVersionInfoW",
        "VerFindFileA", "VerFindFileW", "VerInstallFileA", "VerInstallFileW",
        "VerLanguageNameA", "VerLanguageNameW", "VerQueryValueA", "VerQueryValueW"
    };
    HMODULE proxy = LoadLibraryW(L"version.dll");
    if (!proxy) return 1;
    if (!GetModuleHandleW(L"RakionClientPatch.dll")) return 2;
    for (const char* name : names)
        if (!GetProcAddress(proxy, name)) return 3;

    using SizeFn = DWORD(WINAPI*)(LPCWSTR, LPDWORD);
    auto size = reinterpret_cast<SizeFn>(GetProcAddress(proxy, "GetFileVersionInfoSizeW"));
    DWORD ignored{};
    wchar_t systemFile[MAX_PATH]{};
    const UINT length = GetSystemDirectoryW(systemFile, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return 4;
    if (wcscat_s(systemFile, L"\\kernel32.dll") != 0) return 4;
    const DWORD result = size(systemFile, &ignored);
    FreeLibrary(proxy);
    return result == 0 ? 5 : 0;
}
