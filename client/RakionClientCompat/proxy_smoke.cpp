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
    for (const char* name : names)
        if (!GetProcAddress(proxy, name)) return 2;

    using SizeFn = DWORD(WINAPI*)(LPCWSTR, LPDWORD);
    auto size = reinterpret_cast<SizeFn>(GetProcAddress(proxy, "GetFileVersionInfoSizeW"));
    DWORD ignored{};
    const DWORD result = size(L"verorig.dll", &ignored);
    FreeLibrary(proxy);
    return result == 0 ? 3 : 0;
}
