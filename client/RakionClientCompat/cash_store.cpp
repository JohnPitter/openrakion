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
constexpr uint32_t ButtonConstructorRva = 0x00037680;
constexpr uint32_t ButtonAllocatorRva = 0x000bf8c2;
constexpr uint32_t ButtonSetBitmapIatRva = 0x000d1074;
constexpr uint32_t ComponentSetPosIatRva = 0x000d10b8;
constexpr uint32_t ComponentSetSizeIatRva = 0x000d10bc;
constexpr uint32_t LegacyCashShellExecuteRva = 0x0004c852;
constexpr BYTE ExpectedPowerUserCallback[] = { 0x6a, 0xff, 0x68, 0x2d, 0xc5, 0x4c, 0x00 };
constexpr BYTE ExpectedButtonConstructor[] = { 0x6a, 0xff, 0x68, 0xd8, 0x68, 0x4c, 0x00 };
constexpr BYTE ExpectedLegacyCashShellExecute[] = { 0xff, 0x15, 0x0c, 0xb7, 0x5d, 0x00 };
constexpr char DefaultCashStoreUrl[] = "http://127.0.0.1/cash";
constexpr char BuyCashLabel[] = "Buy Cash";
constexpr int PotionSlotCommand = 0x20;
constexpr int BuyCashCommand = 0x15;
constexpr long BuyCashOffsetX = 0x6d;
uintptr_t PowerUserCallbackContinue{};
volatile LONG LastOpenTick{};
void* PotionSlotButton{};
void* BuyCashButton{};

using ButtonConstructor = void* (__thiscall*)(void*, void*, int, uint32_t, uint32_t,
                                              uint32_t, int, uint32_t);
using ButtonAllocator = void* (__cdecl*)(size_t);
using SetBitmap = void (__thiscall*)(void*, void*, void*, void*);
using SetGeometry = void (__thiscall*)(void*, long, long);

ButtonConstructor OriginalButtonConstructor{};
SetBitmap OriginalSetBitmap{};
SetGeometry OriginalSetPos{};
SetGeometry OriginalSetSize{};

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

void RequestOpenCashStore()
{
    const LONG now = static_cast<LONG>(GetTickCount());
    const LONG previous = InterlockedExchange(&LastOpenTick, now);
    if (previous != 0 && static_cast<DWORD>(now - previous) < 5000) return;
    HANDLE thread = CreateThread(nullptr, 0, OpenCashStore, nullptr, 0, nullptr);
    if (thread) CloseHandle(thread);
}

void SetButtonLabel(void* button)
{
    if (!button) return;
    const auto vtable = *reinterpret_cast<uintptr_t**>(button);
    const auto setter = reinterpret_cast<void (__thiscall*)(void*, const char*)>(vtable[0x34 / 4]);
    setter(button, BuyCashLabel);
}

void* __fastcall ButtonConstructorHook(void* self, void*, void* parent, int command,
                                       uint32_t color, uint32_t hoverColor,
                                       uint32_t pressedColor, int flags, uint32_t style)
{
    void* result = OriginalButtonConstructor(
        self, parent, command, color, hoverColor, pressedColor, flags, style);
    if (command != PotionSlotCommand || PotionSlotButton) return result;

    auto* image = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    const auto allocate = reinterpret_cast<ButtonAllocator>(image + ButtonAllocatorRva);
    void* cash = allocate(0x1b4);
    if (!cash)
    {
        CompatLog("falha ao alocar botao Buy Cash");
        return result;
    }
    PotionSlotButton = result;
    BuyCashButton = OriginalButtonConstructor(
        cash, parent, BuyCashCommand, color, hoverColor, pressedColor, flags, style);
    return result;
}

void __fastcall SetBitmapHook(void* self, void*, void* normal, void* hover, void* pressed)
{
    OriginalSetBitmap(self, normal, hover, pressed);
    if (self == PotionSlotButton && BuyCashButton)
        OriginalSetBitmap(BuyCashButton, normal, hover, pressed);
}

void __fastcall SetPosHook(void* self, void*, long x, long y)
{
    OriginalSetPos(self, x, y);
    if (self != PotionSlotButton || !BuyCashButton) return;
    auto** source = reinterpret_cast<void**>(PotionSlotButton);
    auto** target = reinterpret_cast<void**>(BuyCashButton);
    target[99] = source[99];
    target[100] = source[100];
    SetButtonLabel(BuyCashButton);
    OriginalSetPos(BuyCashButton, x + BuyCashOffsetX, y);
}

void __fastcall SetSizeHook(void* self, void*, long width, long height)
{
    OriginalSetSize(self, width, height);
    if (self != PotionSlotButton || !BuyCashButton) return;
    OriginalSetSize(BuyCashButton, width, height);
    CompatLog("botao nativo Buy Cash criado");
    PotionSlotButton = nullptr;
    BuyCashButton = nullptr;
}

HINSTANCE WINAPI CashShellExecuteHook(HWND, LPCSTR, LPCSTR, LPCSTR, LPCSTR, INT)
{
    RequestOpenCashStore();
    return reinterpret_cast<HINSTANCE>(33);
}

bool WriteJump(BYTE* address, const BYTE* expected, SIZE_T length, const void* target, BYTE opcode)
{
    if (std::memcmp(address, expected, length) != 0) return false;
    DWORD oldProtection{};
    if (!VirtualProtect(address, length, PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
    address[0] = opcode;
    *reinterpret_cast<int32_t*>(address + 1) = static_cast<int32_t>(
        reinterpret_cast<const BYTE*>(target) - (address + 5));
    if (length > 5) std::memset(address + 5, 0x90, length - 5);
    VirtualProtect(address, length, oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), address, length);
    return true;
}

template<typename T>
bool ReplaceIat(BYTE* cell, T replacement, T& original)
{
    original = *reinterpret_cast<T*>(cell);
    HMODULE owner{};
    if (!original || !GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(original), &owner))
        return false;
    wchar_t path[MAX_PATH]{};
    if (!GetModuleFileNameW(owner, path, MAX_PATH)) return false;
    const wchar_t* name = std::wcsrchr(path, L'\\');
    if (_wcsicmp(name ? name + 1 : path, L"uitoolkit.dll") != 0) return false;
    DWORD oldProtection{};
    if (!VirtualProtect(cell, sizeof(T), PAGE_READWRITE, &oldProtection)) return false;
    *reinterpret_cast<T*>(cell) = replacement;
    VirtualProtect(cell, sizeof(T), oldProtection, &oldProtection);
    return true;
}

bool InstallBuyCashButton(BYTE* image)
{
    BYTE* constructor = image + ButtonConstructorRva;
    if (std::memcmp(constructor, ExpectedButtonConstructor,
                    sizeof(ExpectedButtonConstructor)) != 0)
        return false;

    auto* trampoline = static_cast<BYTE*>(VirtualAlloc(
        nullptr, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!trampoline) return false;
    std::memcpy(trampoline, ExpectedButtonConstructor, sizeof(ExpectedButtonConstructor));
    trampoline[7] = 0xe9;
    *reinterpret_cast<int32_t*>(trampoline + 8) = static_cast<int32_t>(
        constructor + sizeof(ExpectedButtonConstructor) - (trampoline + 12));
    OriginalButtonConstructor = reinterpret_cast<ButtonConstructor>(trampoline);

    if (!ReplaceIat(image + ButtonSetBitmapIatRva,
                    reinterpret_cast<SetBitmap>(&SetBitmapHook), OriginalSetBitmap) ||
        !ReplaceIat(image + ComponentSetPosIatRva,
                    reinterpret_cast<SetGeometry>(&SetPosHook), OriginalSetPos) ||
        !ReplaceIat(image + ComponentSetSizeIatRva,
                    reinterpret_cast<SetGeometry>(&SetSizeHook), OriginalSetSize) ||
        !WriteJump(constructor, ExpectedButtonConstructor, sizeof(ExpectedButtonConstructor),
                   &ButtonConstructorHook, 0xe9) ||
        !WriteJump(image + LegacyCashShellExecuteRva, ExpectedLegacyCashShellExecute,
                   sizeof(ExpectedLegacyCashShellExecute), &CashShellExecuteHook, 0xe8))
        return false;
    return true;
}

__declspec(naked) void PowerUserCallbackHook()
{
    __asm
    {
        cmp byte ptr [esp + 4], 3
        jne original
        pushfd
        pushad
        call RequestOpenCashStore
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
    return InstallBuyCashButton(image);
}
