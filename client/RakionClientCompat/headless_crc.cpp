#include <windows.h>

#include <cstdint>
#include <cstring>

#include "compat_log.h"
#include "headless_crc.h"

namespace
{
constexpr char StreamGetPositionSymbol[] = "?GetPos_t@CTStream@@QAEJXZ";
constexpr char StreamSetPositionSymbol[] = "?SetPos_t@CTStream@@QAEXJ@Z";
constexpr char StreamGetSizeSymbol[] = "?GetStreamSize@CTStream@@QAEJXZ";
constexpr char StreamReadSymbol[] = "?Read_t@CTStream@@QAEXPAXJ@Z";
constexpr char FileCommitPageSymbol[] =
    "?FileCommitPage@CTFileStream@@QAEXJ@Z";
constexpr char CrcTableSymbol[] = "?crc_aulCRCTable@@3PAKA";
constexpr uintptr_t StreamCrcReadCallRva = 0x3cc08;
constexpr long StreamPageSize = 0x1000;
volatile LONG SafeStreamCrcInstalled{};

unsigned long __fastcall ReadStreamCrcSafely(void* stream, void*)
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    using GetPositionFn = long(__thiscall*)(void*);
    using SetPositionFn = void(__thiscall*)(void*, long);
    using GetSizeFn = long(__thiscall*)(void*);
    using ReadFn = void(__thiscall*)(void*, void*, long);
    using CommitPageFn = void(__thiscall*)(void*, long);
    auto getPosition = reinterpret_cast<GetPositionFn>(
        GetProcAddress(engine, StreamGetPositionSymbol));
    auto setPosition = reinterpret_cast<SetPositionFn>(
        GetProcAddress(engine, StreamSetPositionSymbol));
    auto getSize = reinterpret_cast<GetSizeFn>(
        GetProcAddress(engine, StreamGetSizeSymbol));
    auto read = reinterpret_cast<ReadFn>(
        GetProcAddress(engine, StreamReadSymbol));
    auto commitPage = reinterpret_cast<CommitPageFn>(
        GetProcAddress(engine, FileCommitPageSymbol));
    auto* table = reinterpret_cast<unsigned long*>(
        GetProcAddress(engine, CrcTableSymbol));

    const long originalPosition = getPosition(stream);
    long remaining = getSize(stream);
    unsigned long crc = 0xffffffff;
    BYTE buffer[4096]{};
    for (long page = 0;
        page < (remaining + StreamPageSize - 1) / StreamPageSize; ++page)
        commitPage(stream, page);
    setPosition(stream, 0);
    while (remaining > 0)
    {
        const long count = remaining < static_cast<long>(sizeof(buffer))
            ? remaining : static_cast<long>(sizeof(buffer));
        read(stream, buffer, count);
        for (long index = 0; index < count; ++index)
            crc = table[(crc ^ buffer[index]) & 0xff] ^ (crc >> 8);
        remaining -= count;
    }
    setPosition(stream, originalPosition);
    return ~crc;
}
}

bool InstallSafeStreamCrc(HMODULE engine)
{
    if (InterlockedCompareExchange(&SafeStreamCrcInstalled, 0, 0) != 0)
        return true;
    if (!engine ||
        !GetProcAddress(engine, StreamGetPositionSymbol) ||
        !GetProcAddress(engine, StreamSetPositionSymbol) ||
        !GetProcAddress(engine, StreamGetSizeSymbol) ||
        !GetProcAddress(engine, StreamReadSymbol) ||
        !GetProcAddress(engine, FileCommitPageSymbol) ||
        !GetProcAddress(engine, CrcTableSymbol))
        return false;

    constexpr BYTE expected[]{0xe8, 0x53, 0xfa, 0xff, 0xff};
    auto* call = reinterpret_cast<BYTE*>(engine) + StreamCrcReadCallRva;
    if (memcmp(call, expected, sizeof(expected)) != 0) return false;
    const intptr_t relative = reinterpret_cast<BYTE*>(&ReadStreamCrcSafely) -
        (call + sizeof(expected));
    const auto displacement = static_cast<int32_t>(relative);
    if (static_cast<intptr_t>(displacement) != relative) return false;

    BYTE patch[sizeof(expected)]{0xe8};
    memcpy(patch + 1, &displacement, sizeof(displacement));
    DWORD protection{};
    if (!VirtualProtect(call, sizeof(patch), PAGE_EXECUTE_READWRITE, &protection))
        return false;
    memcpy(call, patch, sizeof(patch));
    FlushInstructionCache(GetCurrentProcess(), call, sizeof(patch));
    DWORD ignored{};
    VirtualProtect(call, sizeof(patch), protection, &ignored);
    InterlockedExchange(&SafeStreamCrcInstalled, 1);
    CompatLog("headless engine: CRC de XFS usa CTStream::Read_t");
    return true;
}
