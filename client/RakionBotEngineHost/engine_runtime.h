#pragma once

#include <windows.h>

#include <filesystem>
#include <string>
#include <vector>

#include "engine_abi.h"

namespace bot_engine
{
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

class EngineRuntime final
{
public:
    explicit EngineRuntime(std::filesystem::path clientRoot);
    ~EngineRuntime();

    EngineRuntime(const EngineRuntime&) = delete;
    EngineRuntime& operator=(const EngineRuntime&) = delete;

    EngineProbe Initialize();
    WorldProbe LoadWorld(const std::string& worldName);

private:
    void ConfigureDllSearch();
    HMODULE LoadRequiredModule(const wchar_t* moduleName);
    FARPROC ResolveRequired(const char* symbol) const;
    void LoadGameplayModules();
    void RegisterEntityPackage();
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
};

std::filesystem::path ResolveExecutableClientRoot();
std::filesystem::path CanonicalPath(const std::filesystem::path& path);
bool PathsEqualInsensitive(
    const std::filesystem::path& left,
    const std::filesystem::path& right);
std::string FormatWindowsError(DWORD error);
}
