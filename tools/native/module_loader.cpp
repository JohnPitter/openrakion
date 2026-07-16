#include <windows.h>

#include <cstdio>
#include <filesystem>

int wmain(int argc, wchar_t** argv) {
    if (argc < 2) {
        std::fwprintf(stderr, L"uso: module_loader.exe <dependencia.dll> ... <modulo.dll>\n");
        return 2;
    }

    const std::filesystem::path firstPath = std::filesystem::absolute(argv[1]);
    SetDllDirectoryW(firstPath.parent_path().c_str());
    for (int index = 1; index < argc; ++index) {
        const std::filesystem::path modulePath = std::filesystem::absolute(argv[index]);
        const HMODULE module = LoadLibraryW(modulePath.c_str());
        if (module == nullptr) {
            std::fwprintf(stderr, L"LoadLibrary falhou para %ls: %lu\n", modulePath.c_str(), GetLastError());
            return 1;
        }
        std::wprintf(L"pid=%lu module=%p path=%ls\n", GetCurrentProcessId(), module, modulePath.c_str());
    }

    std::fflush(stdout);
    Sleep(60000);
    return 0;
}
