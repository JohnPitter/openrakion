#include "rakion_world_adapter.h"

#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace bot_engine
{
namespace
{
constexpr std::size_t WorldNetworkStorageSize = 0x8000;
constexpr std::size_t FieldInfoStorageSize = 0x5000;
constexpr std::size_t CharacterInfoSize = 0x424;
constexpr std::size_t FieldMapOffset = 0x1a2;
constexpr std::size_t FieldModeOffset = 0x1a3;
constexpr std::size_t CharacterNameOffset = 0x10;
constexpr std::size_t CharacterNameCapacity = 13;
constexpr std::size_t GetFieldInfoVtableIndex = 2;
constexpr std::size_t GetSelectedCharacterVtableIndex = 3;
RakionWorldAdapter* ActiveAdapter{};

void* __fastcall GetFieldInfo(void*, void*)
{
    return ActiveAdapter ? ActiveAdapter->FieldInfo() : nullptr;
}

void* __fastcall GetSelectedCharacter(void*, void*)
{
    return ActiveAdapter ? ActiveAdapter->SelectedCharacter() : nullptr;
}

bool InitializeWorldNetworkSafely(
    bot_engine::InitializeWorldNetwork initialize,
    void* instance)
{
    __try
    {
        initialize(instance);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}
}

RakionWorldAdapter::RakionWorldAdapter(HMODULE engine)
    : engine_(engine)
{
}

RakionWorldAdapter::~RakionWorldAdapter()
{
    Shutdown();
}

void RakionWorldAdapter::Initialize()
{
    if (ActiveAdapter)
        throw std::logic_error("Já existe um RakionWorldAdapter neste processo.");

    auto constructWorld = reinterpret_cast<WorldNetworkConstructor>(
        Resolve(WorldNetworkConstructorSymbol));
    worldNetworkDestructor_ = reinterpret_cast<WorldNetworkDestructor>(
        Resolve(WorldNetworkDestructorSymbol));
    auto constructField = reinterpret_cast<FieldInfoConstructor>(
        Resolve(FieldInfoConstructorSymbol));
    fieldInfoDestructor_ = reinterpret_cast<FieldInfoDestructor>(
        Resolve(FieldInfoDestructorSymbol));
    auto initialize = reinterpret_cast<bot_engine::InitializeWorldNetwork>(
        Resolve(InitializeWorldNetworkSymbol));
    destroyWorldNetwork_ = reinterpret_cast<DestroyWorldNetwork>(
        Resolve(DestroyWorldNetworkSymbol));
    auto** global = reinterpret_cast<void**>(Resolve(WorldNetworkSymbol));

    worldNetworkStorage_.assign(WorldNetworkStorageSize, 0);
    fieldInfoStorage_.assign(FieldInfoStorageSize, 0);
    fieldInfo_ = constructField(fieldInfoStorage_.data());
    worldNetwork_ = constructWorld(worldNetworkStorage_.data());
    std::copy_n(
        *reinterpret_cast<void***>(worldNetwork_),
        worldNetworkVtable_.size(),
        worldNetworkVtable_.begin());
    worldNetworkVtable_[GetFieldInfoVtableIndex] =
        reinterpret_cast<void*>(&GetFieldInfo);
    worldNetworkVtable_[GetSelectedCharacterVtableIndex] =
        reinterpret_cast<void*>(&GetSelectedCharacter);
    *reinterpret_cast<void***>(worldNetwork_) = worldNetworkVtable_.data();
    ActiveAdapter = this;
    *global = worldNetwork_;
    if (!InitializeWorldNetworkSafely(initialize, worldNetwork_))
        throw std::runtime_error("InitWorldNetLib recusou o adaptador nativo.");
}

void RakionWorldAdapter::ConfigureField(
    std::uint8_t mapId,
    std::uint8_t mode)
{
    auto* field = static_cast<std::uint8_t*>(fieldInfo_);
    field[FieldMapOffset] = mapId;
    field[FieldModeOffset] = mode;
}

void RakionWorldAdapter::SelectCharacter(
    std::uint32_t characterId,
    const std::string& name)
{
    selectedCharacterStorage_.assign(CharacterInfoSize, 0);
    *reinterpret_cast<std::uint32_t*>(
        selectedCharacterStorage_.data()) = characterId;
    std::memcpy(
        selectedCharacterStorage_.data() + CharacterNameOffset,
        name.c_str(),
        (std::min)(name.size(), CharacterNameCapacity - 1));
}

void* RakionWorldAdapter::FieldInfo() const noexcept
{
    return fieldInfo_;
}

void* RakionWorldAdapter::SelectedCharacter() const noexcept
{
    return selectedCharacterStorage_.empty()
        ? nullptr
        : const_cast<std::uint8_t*>(selectedCharacterStorage_.data());
}

FARPROC RakionWorldAdapter::Resolve(const char* symbol) const
{
    FARPROC address = GetProcAddress(engine_, symbol);
    if (!address)
        throw std::runtime_error(
            std::string("Export de WorldNet ausente: ") + symbol);
    return address;
}

void RakionWorldAdapter::Shutdown() noexcept
{
    if (worldNetwork_ && destroyWorldNetwork_)
    {
        __try
        {
            destroyWorldNetwork_();
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }
    if (worldNetwork_ && worldNetworkDestructor_)
    {
        __try
        {
            worldNetworkDestructor_(worldNetwork_);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }
    if (fieldInfo_ && fieldInfoDestructor_)
    {
        __try
        {
            fieldInfoDestructor_(fieldInfo_);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }
    ActiveAdapter = nullptr;
    worldNetwork_ = nullptr;
    fieldInfo_ = nullptr;
}
}
