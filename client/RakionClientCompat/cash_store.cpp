#include <windows.h>
#include <shellapi.h>
#include <process.h>

#include <cstdint>
#include <cstring>
#include <array>
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
constexpr uint32_t ComponentSendCommandRva = 0x0001d310;
constexpr uint32_t ButtonPressRva = 0x0001c070;
constexpr BYTE ExpectedPowerUserCallback[] = { 0x6a, 0xff, 0x68, 0x2d, 0xc5, 0x4c, 0x00 };
constexpr BYTE ExpectedButtonConstructor[] = { 0x6a, 0xff, 0x68, 0xd8, 0x68, 0x4c, 0x00 };
constexpr BYTE ExpectedLegacyCashShellExecute[] = { 0xff, 0x15, 0x0c, 0xb7, 0x5d, 0x00 };
constexpr BYTE ExpectedComponentSendCommand[] = { 0x55, 0x8b, 0xec, 0x6a, 0xff };
constexpr BYTE ExpectedButtonPress[] = {
    0x56, 0x8b, 0xf1, 0x8a, 0x86, 0x7c, 0x01, 0x00, 0x00
};
constexpr char DefaultCashStoreUrl[] = "http://127.0.0.1/cash";
constexpr char BuyCashLabel[] = "Buy Cash";
constexpr int PotionSlotCommand = 0x18b;
constexpr int BuyCashCommand = 0x7f01;
constexpr long BuyCashOffsetX = 0x6d;
constexpr DWORD CashStoreOpenCooldownMs = 2000;
uintptr_t PowerUserCallbackContinue{};
volatile LONG CashStoreOpenRequested{};
void* PotionSlotButton{};
void* BuyCashButton{};
std::array<void*, 16> BuyCashButtons{};
size_t BuyCashButtonIndex{};

using ButtonConstructor = void* (__thiscall*)(void*, void*, int, uint32_t, uint32_t,
                                              uint32_t, int, uint32_t);
using ButtonAllocator = void* (__cdecl*)(size_t);
using SetBitmap = void (__thiscall*)(void*, void*, void*, void*);
using SetGeometry = void (__thiscall*)(void*, long, long);
using SendCommand = void* (__thiscall*)(void*, int, void*);
using ButtonPress = void (__thiscall*)(void*);

ButtonConstructor OriginalButtonConstructor{};
SetBitmap OriginalSetBitmap{};
SetGeometry OriginalSetPos{};
SetGeometry OriginalSetSize{};
SendCommand OriginalSendCommand{};
ButtonPress OriginalButtonPress{};

bool IsBuyCashButton(void* button)
{
    if (!button) return false;
    for (void* candidate : BuyCashButtons)
        if (candidate == button)
            return *reinterpret_cast<int*>(static_cast<BYTE*>(button) + 0x170) == BuyCashCommand;
    return false;
}

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

unsigned __stdcall OpenCashStore(void*)
{
    const std::string url = LoadCashStoreUrl();
    auto result = reinterpret_cast<INT_PTR>(
        ShellExecuteA(nullptr, "open", url.c_str(), nullptr, nullptr, SW_SHOWNORMAL));
    if (result <= 32)
        result = reinterpret_cast<INT_PTR>(
            ShellExecuteA(nullptr, "open", "explorer.exe", url.c_str(), nullptr, SW_SHOWNORMAL));
    CompatLog(result > 32
        ? "pagina de recarga aberta no navegador"
        : "falha ao abrir pagina de recarga no navegador");
    Sleep(CashStoreOpenCooldownMs);
    InterlockedExchange(&CashStoreOpenRequested, 0);
    return result > 32 ? 0 : 1;
}

void RequestOpenCashStore()
{
    if (InterlockedCompareExchange(&CashStoreOpenRequested, 1, 0) != 0) return;
    const uintptr_t thread = _beginthreadex(nullptr, 0, OpenCashStore, nullptr, 0, nullptr);
    if (thread != 0)
    {
        CloseHandle(reinterpret_cast<HANDLE>(thread));
        return;
    }
    InterlockedExchange(&CashStoreOpenRequested, 0);
    CompatLog("falha ao criar worker da pagina de recarga");
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
    BuyCashButtons[BuyCashButtonIndex++ % BuyCashButtons.size()] = BuyCashButton;
    CompatLog("botao nativo Buy Cash criado");
    PotionSlotButton = nullptr;
    BuyCashButton = nullptr;
}

void* __fastcall SendCommandHook(void* self, void*, int command, void* sender)
{
    if (!IsBuyCashButton(sender) && command != BuyCashCommand)
        return OriginalSendCommand(self, command, sender);
    CompatLog("clique no botao Buy Cash interceptado");
    RequestOpenCashStore();
    return nullptr;
}

void __fastcall ButtonPressHook(void* self, void*)
{
    if (!IsBuyCashButton(self))
    {
        OriginalButtonPress(self);
        return;
    }
    CompatLog("clique nativo no botao Buy Cash interceptado");
    RequestOpenCashStore();
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

bool InstallSendCommandHook()
{
    auto* toolkit = reinterpret_cast<BYTE*>(GetModuleHandleW(L"uitoolkit.dll"));
    if (!toolkit) return false;
    BYTE* sendCommand = toolkit + ComponentSendCommandRva;
    auto* trampoline = static_cast<BYTE*>(VirtualAlloc(
        nullptr, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!trampoline) return false;
    std::memcpy(trampoline, ExpectedComponentSendCommand,
                sizeof(ExpectedComponentSendCommand));
    trampoline[sizeof(ExpectedComponentSendCommand)] = 0xe9;
    *reinterpret_cast<int32_t*>(trampoline + sizeof(ExpectedComponentSendCommand) + 1) =
        static_cast<int32_t>(sendCommand + sizeof(ExpectedComponentSendCommand) -
            (trampoline + sizeof(ExpectedComponentSendCommand) + 5));
    OriginalSendCommand = reinterpret_cast<SendCommand>(trampoline);
    return WriteJump(sendCommand, ExpectedComponentSendCommand,
        sizeof(ExpectedComponentSendCommand), &SendCommandHook, 0xe9);
}

bool InstallButtonPressHook()
{
    auto* toolkit = reinterpret_cast<BYTE*>(GetModuleHandleW(L"uitoolkit.dll"));
    if (!toolkit) return false;
    BYTE* press = toolkit + ButtonPressRva;
    auto* trampoline = static_cast<BYTE*>(VirtualAlloc(
        nullptr, 16, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!trampoline) return false;
    std::memcpy(trampoline, ExpectedButtonPress, sizeof(ExpectedButtonPress));
    trampoline[sizeof(ExpectedButtonPress)] = 0xe9;
    *reinterpret_cast<int32_t*>(trampoline + sizeof(ExpectedButtonPress) + 1) =
        static_cast<int32_t>(press + sizeof(ExpectedButtonPress) -
            (trampoline + sizeof(ExpectedButtonPress) + 5));
    OriginalButtonPress = reinterpret_cast<ButtonPress>(trampoline);
    return WriteJump(press, ExpectedButtonPress, sizeof(ExpectedButtonPress),
        &ButtonPressHook, 0xe9);
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

    if (!InstallSendCommandHook() || !InstallButtonPressHook() ||
        !ReplaceIat(image + ButtonSetBitmapIatRva,
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
