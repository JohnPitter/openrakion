#include <windows.h>

#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>

#include "engine_runtime.h"

namespace
{
constexpr int UsageError = 2;
constexpr int BootstrapError = 3;
constexpr int ProbeRejected = 4;

struct Options
{
    std::filesystem::path clientRoot;
    std::string worldName;
};

std::string NarrowAscii(std::wstring_view value)
{
    std::string result;
    result.reserve(value.size());
    for (const wchar_t character : value)
    {
        if (character < 0 || character > 127)
            throw std::invalid_argument("O nome do mundo deve usar ASCII.");
        result.push_back(static_cast<char>(character));
    }
    return result;
}

Options ParseOptions(int argc, wchar_t** argv)
{
    Options options{bot_engine::ResolveExecutableClientRoot(), {}};
    for (int index = 1; index < argc; ++index)
    {
        const std::wstring_view argument(argv[index]);
        if (index + 1 >= argc)
            throw std::invalid_argument("Argumento sem valor.");
        if (argument == L"--client-root")
            options.clientRoot = argv[++index];
        else if (argument == L"--world")
            options.worldName = NarrowAscii(argv[++index]);
        else
            throw std::invalid_argument("Argumento desconhecido.");
    }
    return options;
}

void PrintProbe(const bot_engine::EngineProbe& probe)
{
    std::wcout
        << L"{\"engineLoaded\":" << probe.engineLoaded
        << L",\"engineInitialized\":" << probe.engineInitialized
        << L",\"networkReady\":" << probe.networkReady
        << L",\"timerReady\":" << probe.timerReady
        << L",\"entitiesLoaded\":" << probe.entitiesLoaded
        << L",\"forbiddenModules\":[";
    for (size_t index = 0; index < probe.forbiddenModules.size(); ++index)
    {
        if (index)
            std::wcout << L',';
        std::wcout << L'"' << probe.forbiddenModules[index] << L'"';
    }
    std::wcout << L"]}\n";
}

bool IsAccepted(const bot_engine::EngineProbe& probe)
{
    return probe.engineLoaded &&
        probe.engineInitialized &&
        probe.networkReady &&
        probe.timerReady &&
        probe.forbiddenModules.empty();
}

void PrintWorldProbe(const bot_engine::WorldProbe& probe)
{
    std::cout
        << "{\"worldLoaded\":" << probe.worldLoaded
        << ",\"world\":\"" << probe.worldName
        << "\",\"entitiesLoaded\":" << probe.engine.entitiesLoaded
        << "}\n";
}
}

int wmain(int argc, wchar_t** argv)
{
    SetErrorMode(
        SEM_FAILCRITICALERRORS |
        SEM_NOGPFAULTERRORBOX |
        SEM_NOOPENFILEERRORBOX);

    try
    {
        const auto options = ParseOptions(argc, argv);
        bot_engine::EngineRuntime runtime(options.clientRoot);
        const auto probe = runtime.Initialize();
        PrintProbe(probe);
        if (!IsAccepted(probe))
            return ProbeRejected;
        if (options.worldName.empty())
            return EXIT_SUCCESS;

        const auto worldProbe = runtime.LoadWorld(options.worldName);
        PrintWorldProbe(worldProbe);
        return worldProbe.worldLoaded &&
            worldProbe.engine.entitiesLoaded
            ? EXIT_SUCCESS
            : ProbeRejected;
    }
    catch (const std::invalid_argument& error)
    {
        std::cerr << error.what() << '\n';
        return UsageError;
    }
    catch (const std::exception& error)
    {
        std::cerr << "bootstrap da engine falhou: " << error.what() << '\n';
        return BootstrapError;
    }
    catch (char* error)
    {
        std::cerr << "engine recusou o bootstrap: "
                  << (error ? error : "<sem mensagem>") << '\n';
        return BootstrapError;
    }
}
