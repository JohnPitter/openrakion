#include <windows.h>

#include <cstdint>
#include <cstring>

#include "buddy_refresh.h"
#include "compat_log.h"

namespace
{
constexpr uintptr_t BuddyLoginCallbackAddress = 0x0048a5d0;
constexpr uintptr_t CharacterSelectCallbackAddress = 0x0047cb40;
constexpr uintptr_t RakionApplicationAddress = 0x004feed0;
constexpr BYTE ExpectedBuddyLoginCallback[] = { 0x6a, 0xff, 0x68, 0xc3, 0xe3, 0x4c, 0x00 };
constexpr BYTE ExpectedCharacterSelectCallback[] = { 0x64, 0xa1, 0x00, 0x00, 0x00, 0x00 };
constexpr size_t BuddyInterfaceOffset = 0x20;
constexpr size_t BuddyLoginStateOffset = 0x24;
constexpr size_t BuddyNicknameOffset = 0x13b30;
constexpr size_t MessengerHostOffset = 0x4a60;
constexpr size_t SetNicknameVtableIndex = 3;
constexpr size_t MaxNicknameLength = 20;

using BuddyLoginCallback = void (__thiscall*)(void*, uint32_t, uint32_t);
using CharacterSelectCallback = void (__thiscall*)(void*, uint32_t);
using SetNickname = void (__thiscall*)(void*, const wchar_t*);

BuddyLoginCallback OriginalBuddyLogin{};
CharacterSelectCallback OriginalCharacterSelect{};
PVOID volatile PendingRefreshHost{};
void* ActiveSessionHost{};
wchar_t LastRequestedNickname[MaxNicknameLength + 1]{};

bool IsReadable(const void* pointer, size_t length)
{
    MEMORY_BASIC_INFORMATION information{};
    if (!pointer || VirtualQuery(pointer, &information, sizeof(information)) == 0) return false;
    if (information.State != MEM_COMMIT ||
        (information.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0)
        return false;
    const auto start = reinterpret_cast<uintptr_t>(pointer);
    const auto end = reinterpret_cast<uintptr_t>(information.BaseAddress) + information.RegionSize;
    return start < end && length <= end - start;
}

bool BelongsToModule(const void* pointer, HMODULE module)
{
    if (!pointer || !module) return false;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(module);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(
        reinterpret_cast<const BYTE*>(module) + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto address = reinterpret_cast<uintptr_t>(pointer);
    const auto base = reinterpret_cast<uintptr_t>(module);
    return address >= base && address < base + nt->OptionalHeader.SizeOfImage;
}

void* CurrentMessengerHost()
{
    auto** application = reinterpret_cast<void**>(RakionApplicationAddress);
    if (!IsReadable(application, sizeof(void*)) || !*application) return nullptr;
    auto** host = reinterpret_cast<void**>(
        static_cast<BYTE*>(*application) + MessengerHostOffset);
    return IsReadable(host, sizeof(void*)) ? *host : nullptr;
}

const wchar_t* CurrentBuddyNickname(void* ui, void** buddyResult)
{
    if (!IsReadable(ui, BuddyInterfaceOffset + sizeof(void*))) return nullptr;
    auto* buddy = *reinterpret_cast<void**>(static_cast<BYTE*>(ui) + BuddyInterfaceOffset);
    if (!IsReadable(buddy, BuddyNicknameOffset + MaxNicknameLength * sizeof(wchar_t))) return nullptr;
    *buddyResult = buddy;
    auto* nickname = reinterpret_cast<const wchar_t*>(
        static_cast<BYTE*>(buddy) + BuddyNicknameOffset);
    return wcsnlen_s(nickname, MaxNicknameLength) > 0 ? nickname : nullptr;
}

bool RefreshBuddyNickname(void* ui)
{
    void* buddy{};
    const wchar_t* nickname = CurrentBuddyNickname(ui, &buddy);
    if (!nickname) return false;
    if (ActiveSessionHost == ui &&
        wcsncmp(LastRequestedNickname, nickname, MaxNicknameLength) == 0)
        return true;
    auto** vtable = *reinterpret_cast<void***>(buddy);
    if (!IsReadable(vtable, (SetNicknameVtableIndex + 1) * sizeof(void*))) return false;

    void* method = vtable[SetNicknameVtableIndex];
    if (!BelongsToModule(method, GetModuleHandleW(L"Buddy2.dll"))) return false;
    reinterpret_cast<SetNickname>(method)(buddy, nickname);
    ActiveSessionHost = ui;
    wcsncpy_s(LastRequestedNickname, nickname, MaxNicknameLength);
    CompatLog("SetNick do Messenger solicitado");
    return true;
}

bool IsBuddyLoggedIn(void* ui)
{
    return IsReadable(ui, BuddyLoginStateOffset + 1) &&
        *(static_cast<BYTE*>(ui) + BuddyLoginStateOffset) != 0;
}

void RefreshAfterCharacterSelection(void* ui)
{
    if (!ui) return;
    if (!IsBuddyLoggedIn(ui))
    {
        InterlockedExchangePointer(&PendingRefreshHost, ui);
        CompatLog("SetNick do Messenger aguardando login Buddy");
        return;
    }

    InterlockedCompareExchangePointer(&PendingRefreshHost, nullptr, ui);
    RefreshBuddyNickname(ui);
}

void __fastcall BuddyLoginCallbackHook(void* self, void*, uint32_t result, uint32_t context)
{
    OriginalBuddyLogin(self, result, context);
    if ((result & 0xffff) != 0) return;
    ActiveSessionHost = self;
    LastRequestedNickname[0] = L'\0';
    if (InterlockedCompareExchangePointer(&PendingRefreshHost, nullptr, self) == self)
        RefreshBuddyNickname(self);
}

void __fastcall CharacterSelectCallbackHook(void* self, void*, uint32_t result)
{
    OriginalCharacterSelect(self, result);
    if ((result & 0xff) != 0) return;
    RefreshAfterCharacterSelection(CurrentMessengerHost());
}

bool InstallHook(uintptr_t address, const BYTE* expected, size_t length,
                 void* replacement, void** trampoline)
{
    auto* target = reinterpret_cast<BYTE*>(address);
    if (length < 5 || std::memcmp(target, expected, length) != 0) return false;
    auto* gateway = static_cast<BYTE*>(VirtualAlloc(
        nullptr, length + 5, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!gateway) return false;
    std::memcpy(gateway, target, length);
    gateway[length] = 0xe9;
    *reinterpret_cast<int32_t*>(gateway + length + 1) = static_cast<int32_t>(
        address + length - reinterpret_cast<uintptr_t>(gateway + length + 5));

    DWORD oldProtection{};
    if (!VirtualProtect(target, length, PAGE_EXECUTE_READWRITE, &oldProtection))
    {
        VirtualFree(gateway, 0, MEM_RELEASE);
        return false;
    }
    target[0] = 0xe9;
    *reinterpret_cast<int32_t*>(target + 1) = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(replacement) - (address + 5));
    std::memset(target + 5, 0x90, length - 5);
    VirtualProtect(target, length, oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), target, length);
    *trampoline = gateway;
    return true;
}
}

bool InstallBuddyRefreshHooks()
{
    if (std::memcmp(reinterpret_cast<const void*>(BuddyLoginCallbackAddress),
            ExpectedBuddyLoginCallback, sizeof(ExpectedBuddyLoginCallback)) != 0 ||
        std::memcmp(reinterpret_cast<const void*>(CharacterSelectCallbackAddress),
            ExpectedCharacterSelectCallback, sizeof(ExpectedCharacterSelectCallback)) != 0)
        return false;

    void* loginTrampoline{};
    if (!InstallHook(BuddyLoginCallbackAddress, ExpectedBuddyLoginCallback,
            sizeof(ExpectedBuddyLoginCallback), reinterpret_cast<void*>(&BuddyLoginCallbackHook),
            &loginTrampoline))
        return false;
    OriginalBuddyLogin = reinterpret_cast<BuddyLoginCallback>(loginTrampoline);

    void* selectionTrampoline{};
    if (!InstallHook(CharacterSelectCallbackAddress, ExpectedCharacterSelectCallback,
            sizeof(ExpectedCharacterSelectCallback),
            reinterpret_cast<void*>(&CharacterSelectCallbackHook), &selectionTrampoline))
        return false;
    OriginalCharacterSelect = reinterpret_cast<CharacterSelectCallback>(selectionTrampoline);
    return true;
}
