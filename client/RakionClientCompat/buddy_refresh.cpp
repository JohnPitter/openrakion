#include <windows.h>

#include <cstdint>
#include <cstring>

#include "buddy_refresh.h"
#include "compat_log.h"

namespace
{
constexpr uintptr_t BuddyLoginCallbackAddress = 0x0048a5d0;
constexpr uintptr_t BuddyNameCallbackAddress = 0x004785b0;
constexpr BYTE ExpectedBuddyLoginCallback[] = { 0x6a, 0xff, 0x68, 0xc3, 0xe3, 0x4c, 0x00 };
constexpr BYTE ExpectedBuddyNameCallback[] = { 0x6a, 0xff, 0x68, 0x84, 0xc9, 0x4c, 0x00 };
constexpr size_t BuddyInterfaceOffset = 0x20;
constexpr size_t BuddyNicknameOffset = 0x13b30;
constexpr size_t SetNicknameVtableIndex = 3;
constexpr size_t MaxNicknameLength = 20;

void* volatile BuddyUi{};
void* BuddyLoginTrampoline{};
void* BuddyNameTrampoline{};

bool IsReadable(const void* pointer, size_t length)
{
    MEMORY_BASIC_INFORMATION information{};
    if (!pointer || VirtualQuery(pointer, &information, sizeof(information)) == 0) return false;
    if (information.State != MEM_COMMIT || (information.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0)
        return false;
    const auto start = reinterpret_cast<uintptr_t>(pointer);
    const auto end = reinterpret_cast<uintptr_t>(information.BaseAddress) + information.RegionSize;
    return length <= end - start;
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

void __stdcall CaptureBuddyUi(void* ui)
{
    if (!IsReadable(ui, BuddyInterfaceOffset + sizeof(void*))) return;
    InterlockedExchangePointer(&BuddyUi, ui);
}

void __stdcall RefreshBuddyAfterNameChange()
{
    auto* ui = InterlockedCompareExchangePointer(&BuddyUi, nullptr, nullptr);
    if (!IsReadable(ui, BuddyInterfaceOffset + sizeof(void*))) return;

    auto* buddy = *reinterpret_cast<void**>(static_cast<BYTE*>(ui) + BuddyInterfaceOffset);
    if (!IsReadable(buddy, BuddyNicknameOffset + MaxNicknameLength * sizeof(wchar_t))) return;
    auto** vtable = *reinterpret_cast<void***>(buddy);
    if (!IsReadable(vtable, (SetNicknameVtableIndex + 1) * sizeof(void*))) return;

    HMODULE module = GetModuleHandleW(L"Buddy2.dll");
    void* method = vtable[SetNicknameVtableIndex];
    if (!BelongsToModule(method, module)) return;

    auto* nickname = reinterpret_cast<const wchar_t*>(
        static_cast<BYTE*>(buddy) + BuddyNicknameOffset);
    if (wcsnlen_s(nickname, MaxNicknameLength) == 0) return;
    using SetNickname = void(__thiscall*)(void*, const wchar_t*);
    reinterpret_cast<SetNickname>(method)(buddy, nickname);
    CompatLog("buddy refresh solicitado apos troca de nome confirmada");
}

__declspec(naked) void BuddyLoginCallbackHook()
{
    __asm
    {
        pushfd
        pushad
        push ecx
        call CaptureBuddyUi
        popad
        popfd
        jmp dword ptr [BuddyLoginTrampoline]
    }
}

__declspec(naked) void BuddyNameCallbackHook()
{
    __asm
    {
        movzx eax, byte ptr [esp + 4]
        test eax, eax
        sete al
        movzx eax, al
        push eax
        mov edx, dword ptr [esp + 12]
        push edx
        mov edx, dword ptr [esp + 12]
        push edx
        call dword ptr [BuddyNameTrampoline]
        pop eax
        test eax, eax
        jz done
        pushfd
        pushad
        call RefreshBuddyAfterNameChange
        popad
        popfd
    done:
        ret 8
    }
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
    *reinterpret_cast<int32_t*>(gateway + length + 1) =
        static_cast<int32_t>((address + length) - reinterpret_cast<uintptr_t>(gateway + length + 5));

    DWORD oldProtection{};
    if (!VirtualProtect(target, length, PAGE_EXECUTE_READWRITE, &oldProtection))
    {
        VirtualFree(gateway, 0, MEM_RELEASE);
        return false;
    }
    target[0] = 0xe9;
    *reinterpret_cast<int32_t*>(target + 1) =
        static_cast<int32_t>(reinterpret_cast<uintptr_t>(replacement) - (address + 5));
    std::memset(target + 5, 0x90, length - 5);
    VirtualProtect(target, length, oldProtection, &oldProtection);
    FlushInstructionCache(GetCurrentProcess(), target, length);
    *trampoline = gateway;
    return true;
}
}

bool InstallBuddyRefreshHooks()
{
    if (!InstallHook(BuddyLoginCallbackAddress, ExpectedBuddyLoginCallback,
            sizeof(ExpectedBuddyLoginCallback), reinterpret_cast<void*>(&BuddyLoginCallbackHook),
            &BuddyLoginTrampoline))
        return false;
    if (!InstallHook(BuddyNameCallbackAddress, ExpectedBuddyNameCallback,
            sizeof(ExpectedBuddyNameCallback), reinterpret_cast<void*>(&BuddyNameCallbackHook),
            &BuddyNameTrampoline))
        return false;
    return true;
}
