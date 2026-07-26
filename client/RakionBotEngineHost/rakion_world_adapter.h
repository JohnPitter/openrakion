#pragma once

#include <windows.h>

#include <array>
#include <cstdint>
#include <string>
#include <vector>

#include "engine_abi.h"

namespace bot_engine
{
class RakionWorldAdapter final
{
public:
    explicit RakionWorldAdapter(HMODULE engine);
    ~RakionWorldAdapter();

    RakionWorldAdapter(const RakionWorldAdapter&) = delete;
    RakionWorldAdapter& operator=(const RakionWorldAdapter&) = delete;

    void Initialize();
    void ConfigureField(std::uint8_t mapId, std::uint8_t mode);
    void SelectCharacter(std::uint32_t characterId, const std::string& name);
    void* FieldInfo() const noexcept;
    void* SelectedCharacter() const noexcept;

private:
    FARPROC Resolve(const char* symbol) const;
    void Shutdown() noexcept;

    HMODULE engine_{};
    void* worldNetwork_{};
    void* fieldInfo_{};
    WorldNetworkDestructor worldNetworkDestructor_{};
    FieldInfoDestructor fieldInfoDestructor_{};
    DestroyWorldNetwork destroyWorldNetwork_{};
    std::vector<std::uint8_t> worldNetworkStorage_;
    std::vector<std::uint8_t> fieldInfoStorage_;
    std::vector<std::uint8_t> selectedCharacterStorage_;
    std::array<void*, 192> worldNetworkVtable_{};
};
}
