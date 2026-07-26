#include "engine_runtime.h"

#include <array>
#include <stdexcept>
#include <system_error>

namespace bot_engine
{
std::filesystem::path CanonicalPath(const std::filesystem::path& path)
{
    std::error_code error;
    auto canonical = std::filesystem::weakly_canonical(path, error);
    if (error)
        throw std::runtime_error(
            "Não foi possível resolver o caminho do cliente: " +
            error.message());
    return canonical;
}

bool PathsEqualInsensitive(
    const std::filesystem::path& left,
    const std::filesystem::path& right)
{
    return _wcsicmp(left.c_str(), right.c_str()) == 0;
}

std::filesystem::path ResolveExecutableClientRoot()
{
    std::array<wchar_t, 32768> buffer{};
    DWORD length = GetModuleFileNameW(
        nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size())
        throw std::runtime_error(
            "GetModuleFileNameW falhou: " +
            FormatWindowsError(GetLastError()));

    auto executable = CanonicalPath(
        std::filesystem::path(buffer.data(), buffer.data() + length));
    auto bin = executable.parent_path();
    if (_wcsicmp(bin.filename().c_str(), L"Bin") != 0)
        throw std::runtime_error(
            "BotEngineHost.exe precisa estar no diretório Bin do cliente.");
    return CanonicalPath(bin.parent_path());
}

std::string FormatWindowsError(DWORD error)
{
    return std::system_category().message(static_cast<int>(error));
}
}
