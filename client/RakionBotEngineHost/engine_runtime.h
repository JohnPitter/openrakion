#pragma once

#include <windows.h>

#include <filesystem>
#include <cstdint>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

#include "engine_abi.h"

namespace bot_engine
{
class RakionWorldAdapter;

struct EngineProbe
{
    bool engineLoaded{};
    bool engineInitialized{};
    bool networkReady{};
    bool timerReady{};
    bool entitiesLoaded{};
    std::vector<std::wstring> forbiddenModules;
};

struct WorldProbe
{
    EngineProbe engine;
    bool worldLoaded{};
    std::string worldName;
};

struct LocalPlayerProbe
{
    std::uint32_t botId{};
    std::uint32_t activePlayers{};
    std::uint32_t capacity{};
};

class EngineRuntime final
{
public:
    explicit EngineRuntime(std::filesystem::path clientRoot);
    ~EngineRuntime();

    EngineRuntime(const EngineRuntime&) = delete;
    EngineRuntime& operator=(const EngineRuntime&) = delete;

    EngineProbe Initialize();
    WorldProbe LoadWorld(
        const std::string& worldName,
        std::uint8_t mapId,
        std::uint8_t mode);
    LocalPlayerProbe AddLocalPlayer(
        std::uint32_t botId,
        const std::string& name,
        const std::string& species);
    std::uint32_t LocalPlayerCount() const;
    std::uint32_t LocalPlayerCapacity() const;

private:
    void ConfigureDllSearch();
    HMODULE LoadRequiredModule(const wchar_t* moduleName);
    FARPROC ResolveRequired(const char* symbol) const;
    void LoadGameplayModules();
    void RegisterEntityPackage();
    void InitializeGameShell();
    LegacyString CreateEngineString(const char* value) const;
    EngineProbe Inspect() const;
    void DestroyEngineString(LegacyString& value) const noexcept;
    void Shutdown() noexcept;

    std::filesystem::path clientRoot_;
    std::filesystem::path binPath_;
    HMODULE engine_{};
    HMODULE entities_{};
    HMODULE gameModule_{};
    void* game_{};
    DestroyGame destroyGame_{};
    EndEngine endEngine_{};
    bool initialized_{};
    bool worldLoaded_{};
    bool streamsEnabled_{};
    std::unique_ptr<RakionWorldAdapter> worldAdapter_;
    std::unordered_map<std::uint32_t, void*> botSources_;
};

std::filesystem::path ResolveExecutableClientRoot();
std::filesystem::path CanonicalPath(const std::filesystem::path& path);
bool PathsEqualInsensitive(
    const std::filesystem::path& left,
    const std::filesystem::path& right);
std::string FormatWindowsError(DWORD error);
}
