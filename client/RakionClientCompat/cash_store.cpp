#include <windows.h>
#include <shellapi.h>

#include <cstdint>
#include <cstring>
#include <fstream>
#include <string>

#include "cash_store.h"
#include "compat_log.h"

#pragma comment(lib, "Shell32.lib")

namespace
{
constexpr uint32_t PowerUserCallbackRva = 0x00074f50;
constexpr BYTE ExpectedPowerUserCallback[] = { 0x6a, 0xff, 0x68, 0x2d, 0xc5, 0x4c, 0x00 };
constexpr char DefaultCashStoreUrl[] = "http://127.0.0.1/cash";
uintptr_t PowerUserCallbackContinue{};
volatile LONG LastOpenTick{};

bool IsHttpUrl(const std::string& value)
{
    const bool scheme = value.starts_with("http://") || value.starts_with("https://");
    return scheme && value.size() < 2048 &&
        value.find_first_of("\r\n\t ") == std::string::npos;
}

std::string LoadCashStoreUrl()
{
    char path[MAX_PATH]{};
    if (GetModuleFileNameA(nullptr, path, MAX_PATH) == 0) return DefaultCashStoreUrl;
    char* separator = std::strrchr(path, '\\');
    if (!separator) return DefaultCashStoreUrl;
    strcpy_s(separator + 1, MAX_PATH - static_cast<size_t>(separator + 1 - path),
        "..\\cash-shop.url");
    std::ifstream input(path);
    std::string url;
    input >> url;
    return IsHttpUrl(url) ? url : DefaultCashStoreUrl;
}

DWORD WINAPI OpenCashStore(void*)
{
    const std::string url = LoadCashStoreUrl();
    const auto result = reinterpret_cast<INT_PTR>(
        ShellExecuteA(nullptr, "open", url.c_str(), nullptr, nullptr, SW_SHOWNORMAL));
    CompatLog(result > 32
        ? "pagina de recarga aberta apos saldo insuficiente"
        : "falha ao abrir pagina de recarga");
    return result > 32 ? 0 : 1;
}

void __stdcall TriggerCashStore()
{
    const LONG now = static_cast<LONG>(GetTickCount());
    const LONG previous = InterlockedExchange(&LastOpenTick, now);
    if (previous != 0 && static_cast<DWORD>(now - previous) < 5000) return;
    HANDLE thread = CreateThread(nullptr, 0, OpenCashStore, nullptr, 0, nullptr);
    if (thread) CloseHandle(thread);
}

__declspec(naked) void PowerUserCallbackHook()
{
    __asm
    {
        cmp byte ptr [esp + 4], 3
        jne original
        pushfd
        pushad
        call TriggerCashStore
        popad
        popfd
    original:
        push -1
        push 0x004cc52d
        jmp dword ptr [PowerUserCallbackContinue]
    }
}
}

bool InstallCashStoreRedirectHook()
{
    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    BYTE* patch = image + PowerUserCallbackRva;
    if (std::memcmp(patch, ExpectedPowerUserCallback, sizeof(ExpectedPowerUserCallback)) != 0)
        return false;
    PowerUserCallbackContinue = reinterpret_cast<uintptr_t>(
        patch + sizeof(ExpectedPowerUserCallback));

    DWORD oldProtection{};
    if (!VirtualProtect(patch, sizeof(ExpectedPowerUserCallback),
            PAGE_EXECUTE_READWRITE, &oldProtection))
        return false;
    const intptr_t displacement = reinterpret_cast<BYTE*>(&PowerUserCallbackHook) - (patch + 5);
    patch[0] = 0xe9;
    *reinterpret_cast<int32_t*>(patch + 1) = static_cast<int32_t>(displacement);
    std::memset(patch + 5, 0x90, sizeof(ExpectedPowerUserCallback) - 5);
    VirtualProtect(patch, sizeof(ExpectedPowerUserCallback), oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), patch, sizeof(ExpectedPowerUserCallback));
    return true;
}
