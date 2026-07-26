#include <windows.h>

#include <cstdint>
#include <cstring>
#include <intrin.h>

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
constexpr char FileDecommitPageSymbol[] =
    "?FileDecommitPage@CTFileStream@@QAEXJ@Z";
constexpr char FileOpenSymbol[] =
    "?Open_t@CTFileStream@@QAEXABVCTFileName@@W4OpenMode@CTStream@@@Z";
constexpr char TextureReadSymbol[] =
    "?Read_t@CTextureData@@UAEXPAVCTStream@@@Z";
constexpr char StreamExpectIdSymbol[] =
    "?ExpectID_t@CTStream@@QAEXABVCChunkID@@@Z";
constexpr char StreamExpectKeywordSymbol[] =
    "?ExpectKeyword_t@CTStream@@QAEXABVCTString@@@Z";
constexpr char CrcTableSymbol[] = "?crc_aulCRCTable@@3PAKA";
constexpr uintptr_t StreamCrcReadCallRva = 0x3cc08;
constexpr uintptr_t FilePageSizeRva = 0x2acae0;
constexpr uintptr_t FileHandleAccessRva = 0x3d360;
constexpr uintptr_t FileHandleAccessEndRva = 0x3d478;
constexpr long CrcBlockSize = 0x1000;
volatile LONG SafeStreamCrcInstalled{};
void* OriginalExpectId{};
void* OriginalExpectKeyword{};
void* OriginalFileDecommitPage{};
void* OriginalFileOpen{};
void* OriginalTextureRead{};

void MaterializeReadStream(void* stream)
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    using GetSizeFn = long(__thiscall*)(void*);
    using CommitPageFn = void(__thiscall*)(void*, long);
    auto getSize = reinterpret_cast<GetSizeFn>(
        GetProcAddress(engine, StreamGetSizeSymbol));
    auto commitPage = reinterpret_cast<CommitPageFn>(
        GetProcAddress(engine, FileCommitPageSymbol));
    const long pageSize = *reinterpret_cast<const long*>(
        reinterpret_cast<const BYTE*>(engine) + FilePageSizeRva);
    const long size = getSize(stream);
    auto* base = *reinterpret_cast<BYTE**>(
        static_cast<BYTE*>(stream) + 0x0c);
    for (long page = 0; page < (size + pageSize - 1) / pageSize; ++page)
    {
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(
                base + page * pageSize, &memory, sizeof(memory)) == 0 ||
            memory.State != MEM_COMMIT)
            commitPage(stream, page);
    }
}

bool PrepareStreamRead(void* stream)
{
    if (!stream) return false;
    MaterializeReadStream(stream);
    return true;
}

void __fastcall ExpectIdWithAccess(
    void* stream, void*, const void* expected)
{
    using ExpectIdFn = void(__thiscall*)(void*, const void*);
    PrepareStreamRead(stream);
    reinterpret_cast<ExpectIdFn>(OriginalExpectId)(stream, expected);
}

void __fastcall ExpectKeywordWithAccess(
    void* stream, void*, const void* expected)
{
    using ExpectKeywordFn = void(__thiscall*)(void*, const void*);
    PrepareStreamRead(stream);
    reinterpret_cast<ExpectKeywordFn>(OriginalExpectKeyword)(stream, expected);
}

void __fastcall DecommitPageOutsideAccessWindow(
    void* stream, void*, long page)
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    const auto* returnAddress = static_cast<const BYTE*>(_ReturnAddress());
    const auto* handleAccess = reinterpret_cast<const BYTE*>(engine) +
        FileHandleAccessRva;
    if (returnAddress >= handleAccess &&
        returnAddress < reinterpret_cast<const BYTE*>(engine) +
            FileHandleAccessEndRva)
        return;

    using DecommitPageFn = void(__thiscall*)(void*, long);
    reinterpret_cast<DecommitPageFn>(OriginalFileDecommitPage)(stream, page);
}

void __fastcall OpenMaterializedReadStream(
    void* stream, void*, const void* fileName, int mode)
{
    using FileOpenFn = void(__thiscall*)(void*, const void*, int);
    reinterpret_cast<FileOpenFn>(OriginalFileOpen)(stream, fileName, mode);
    if (mode != 1) return;
    MaterializeReadStream(stream);
}

void __fastcall ReadTextureFromMaterializedStream(
    void* texture, void*, void* stream)
{
    using TextureReadFn = void(__thiscall*)(void*, void*);
    MaterializeReadStream(stream);
    reinterpret_cast<TextureReadFn>(OriginalTextureRead)(texture, stream);
}

bool InstallStreamReadHook(
    BYTE* address, void* replacement, void*& original)
{
    constexpr size_t PrologueSize = 7;
    constexpr BYTE ProloguePrefix[]{0x6a, 0xff, 0x68};
    if (memcmp(address, ProloguePrefix, sizeof(ProloguePrefix)) != 0 ||
        address[PrologueSize] != 0x64 ||
        address[PrologueSize + 1] != 0xa1)
        return false;

    auto* trampoline = static_cast<BYTE*>(VirtualAlloc(
        nullptr, PrologueSize + 5, MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE));
    if (!trampoline) return false;
    memcpy(trampoline, address, PrologueSize);
    trampoline[PrologueSize] = 0xe9;
    *reinterpret_cast<int32_t*>(trampoline + PrologueSize + 1) =
        static_cast<int32_t>(
            address + PrologueSize - (trampoline + PrologueSize + 5));

    BYTE patch[PrologueSize]{0xe9};
    *reinterpret_cast<int32_t*>(patch + 1) = static_cast<int32_t>(
        static_cast<BYTE*>(replacement) - (address + 5));
    patch[5] = patch[6] = 0x90;
    DWORD protection{};
    if (!VirtualProtect(
            address, sizeof(patch), PAGE_EXECUTE_READWRITE, &protection))
    {
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return false;
    }
    memcpy(address, patch, sizeof(patch));
    FlushInstructionCache(GetCurrentProcess(), address, sizeof(patch));
    DWORD ignored{};
    VirtualProtect(address, sizeof(patch), protection, &ignored);
    original = trampoline;
    return true;
}

bool InstallStreamReadHooks(HMODULE engine)
{
    auto* expectId = reinterpret_cast<BYTE*>(
        GetProcAddress(engine, StreamExpectIdSymbol));
    auto* expectKeyword = reinterpret_cast<BYTE*>(
        GetProcAddress(engine, StreamExpectKeywordSymbol));
    auto* decommitPage = reinterpret_cast<BYTE*>(
        GetProcAddress(engine, FileDecommitPageSymbol));
    auto* fileOpen = reinterpret_cast<BYTE*>(
        GetProcAddress(engine, FileOpenSymbol));
    auto* textureRead = reinterpret_cast<BYTE*>(
        GetProcAddress(engine, TextureReadSymbol));
    return expectId && expectKeyword && decommitPage && fileOpen &&
        textureRead &&
        InstallStreamReadHook(
            expectId, reinterpret_cast<void*>(&ExpectIdWithAccess),
            OriginalExpectId) &&
        InstallStreamReadHook(
            expectKeyword, reinterpret_cast<void*>(&ExpectKeywordWithAccess),
            OriginalExpectKeyword) &&
        InstallStreamReadHook(
            decommitPage,
            reinterpret_cast<void*>(&DecommitPageOutsideAccessWindow),
            OriginalFileDecommitPage) &&
        InstallStreamReadHook(
            fileOpen, reinterpret_cast<void*>(&OpenMaterializedReadStream),
            OriginalFileOpen) &&
        InstallStreamReadHook(
            textureRead,
            reinterpret_cast<void*>(&ReadTextureFromMaterializedStream),
            OriginalTextureRead);
}

unsigned long __fastcall ReadStreamCrcSafely(void* stream, void*)
{
    HMODULE engine = GetModuleHandleW(L"engine.dll");
    using GetPositionFn = long(__thiscall*)(void*);
    using SetPositionFn = void(__thiscall*)(void*, long);
    using GetSizeFn = long(__thiscall*)(void*);
    using ReadFn = void(__thiscall*)(void*, void*, long);
    auto getPosition = reinterpret_cast<GetPositionFn>(
        GetProcAddress(engine, StreamGetPositionSymbol));
    auto setPosition = reinterpret_cast<SetPositionFn>(
        GetProcAddress(engine, StreamSetPositionSymbol));
    auto getSize = reinterpret_cast<GetSizeFn>(
        GetProcAddress(engine, StreamGetSizeSymbol));
    auto read = reinterpret_cast<ReadFn>(
        GetProcAddress(engine, StreamReadSymbol));
    auto* table = reinterpret_cast<unsigned long*>(
        GetProcAddress(engine, CrcTableSymbol));

    const long originalPosition = getPosition(stream);
    long remaining = getSize(stream);
    unsigned long crc = 0xffffffff;
    BYTE buffer[CrcBlockSize]{};
    MaterializeReadStream(stream);
    setPosition(stream, 0);
    while (remaining > 0)
    {
        const long count = min(
            remaining, static_cast<long>(sizeof(buffer)));
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
        !GetProcAddress(engine, FileDecommitPageSymbol) ||
        !GetProcAddress(engine, FileOpenSymbol) ||
        !GetProcAddress(engine, TextureReadSymbol) ||
        !GetProcAddress(engine, StreamExpectIdSymbol) ||
        !GetProcAddress(engine, StreamExpectKeywordSymbol) ||
        !GetProcAddress(engine, CrcTableSymbol))
        return false;
    const long pageSize = *reinterpret_cast<const long*>(
        reinterpret_cast<const BYTE*>(engine) + FilePageSizeRva);
    if (pageSize <= 0 || (pageSize & (pageSize - 1)) != 0)
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
    if (!InstallStreamReadHooks(engine)) return false;
    InterlockedExchange(&SafeStreamCrcInstalled, 1);
    CompatLog(
        "headless engine: janela XFS renovada no CRC, ID e keyword");
    return true;
}
