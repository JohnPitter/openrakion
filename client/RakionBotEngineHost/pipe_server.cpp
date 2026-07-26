#include "pipe_server.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <string_view>

namespace bot_engine
{
namespace
{
constexpr wchar_t PipePrefix[] = L"\\\\.\\pipe\\";

bool IsValidPipeName(const std::wstring& name)
{
    if (name.empty() || name.size() > 80)
        return false;
    return std::all_of(name.begin(), name.end(), [](wchar_t character)
    {
        return (character >= L'a' && character <= L'z') ||
            (character >= L'A' && character <= L'Z') ||
            (character >= L'0' && character <= L'9') ||
            character == L'-' || character == L'_' || character == L'.';
    });
}

bool ReadExact(HANDLE pipe, void* target, std::uint32_t size)
{
    auto* cursor = static_cast<std::uint8_t*>(target);
    std::uint32_t remaining = size;
    while (remaining)
    {
        DWORD read{};
        if (!ReadFile(pipe, cursor, remaining, &read, nullptr) || read == 0)
            return false;
        cursor += read;
        remaining -= read;
    }
    return true;
}

bool WriteExact(HANDLE pipe, const void* source, std::uint32_t size)
{
    const auto* cursor = static_cast<const std::uint8_t*>(source);
    std::uint32_t remaining = size;
    while (remaining)
    {
        DWORD written{};
        if (!WriteFile(pipe, cursor, remaining, &written, nullptr) ||
            written == 0)
            return false;
        cursor += written;
        remaining -= written;
    }
    return true;
}

protocol::MessageType RequestType(const protocol::FrameHeader& header)
{
    return static_cast<protocol::MessageType>(
        header.messageType & ~protocol::ResponseFlag);
}

bool IsValidWorldName(std::string_view name)
{
    constexpr std::string_view prefix = "LevelsSV\\";
    constexpr std::string_view extension = ".wld";
    if (!name.starts_with(prefix) || !name.ends_with(extension) ||
        name.find("..") != std::string_view::npos)
        return false;

    return std::all_of(name.begin(), name.end(), [](char character)
    {
        const auto value = static_cast<unsigned char>(character);
        return (value >= 'a' && value <= 'z') ||
            (value >= 'A' && value <= 'Z') ||
            (value >= '0' && value <= '9') ||
            character == '\\' || character == '_' ||
            character == '-' || character == ' ' || character == '.';
    });
}

bool IsValidPlayerText(std::string_view value)
{
    if (value.empty())
        return false;
    return std::all_of(value.begin(), value.end(), [](char character)
    {
        const auto byte = static_cast<unsigned char>(character);
        return byte >= 0x21 && byte <= 0x7e;
    });
}

template<std::size_t Size>
std::string ReadText(const char (&value)[Size])
{
    const auto length = strnlen(value, Size);
    return length < Size ? std::string(value, length) : std::string{};
}
}

PipeServer::PipeServer(std::wstring pipeName)
    : pipeName_(std::move(pipeName))
{
    if (!IsValidPipeName(pipeName_))
        throw std::invalid_argument("Nome do pipe inválido.");
}

int PipeServer::Run(EngineRuntime& runtime)
{
    HANDLE pipe = CreatePipe();
    const BOOL connected = ConnectNamedPipe(pipe, nullptr) ||
        GetLastError() == ERROR_PIPE_CONNECTED;
    if (!connected)
    {
        const DWORD error = GetLastError();
        CloseHandle(pipe);
        throw std::runtime_error(
            "ConnectNamedPipe falhou: " + FormatWindowsError(error));
    }

    int result = EXIT_SUCCESS;
    Request request{};
    while (ReadRequest(pipe, request))
    {
        const Response response = Dispatch(request, runtime);
        if (!WriteResponse(pipe, request, response))
        {
            result = EXIT_FAILURE;
            break;
        }
        if (response.stop)
            break;
    }
    FlushFileBuffers(pipe);
    DisconnectNamedPipe(pipe);
    CloseHandle(pipe);
    return result;
}

HANDLE PipeServer::CreatePipe() const
{
    const std::wstring path = PipePrefix + pipeName_;
    HANDLE pipe = CreateNamedPipeW(
        path.c_str(),
        PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT |
            PIPE_REJECT_REMOTE_CLIENTS,
        1,
        protocol::MaximumPayloadSize + sizeof(protocol::FrameHeader),
        protocol::MaximumPayloadSize + sizeof(protocol::FrameHeader),
        0,
        nullptr);
    if (pipe == INVALID_HANDLE_VALUE)
        throw std::runtime_error(
            "CreateNamedPipeW falhou: " +
            FormatWindowsError(GetLastError()));
    return pipe;
}

bool PipeServer::ReadRequest(HANDLE pipe, Request& request) const
{
    request = {};
    if (!ReadExact(pipe, &request.header, sizeof(request.header)))
        return false;
    if (request.header.magic != protocol::Magic ||
        request.header.payloadSize > protocol::MaximumPayloadSize ||
        (request.header.messageType & protocol::ResponseFlag) != 0)
        return false;

    request.payload.resize(request.header.payloadSize);
    return request.payload.empty() ||
        ReadExact(
            pipe,
            request.payload.data(),
            static_cast<std::uint32_t>(request.payload.size()));
}

bool PipeServer::WriteResponse(
    HANDLE pipe,
    const Request& request,
    const Response& response) const
{
    protocol::FrameHeader header{
        protocol::Magic,
        protocol::Version,
        static_cast<std::uint16_t>(
            request.header.messageType | protocol::ResponseFlag),
        static_cast<std::uint32_t>(response.payload.size()),
        request.header.correlationId,
        static_cast<std::uint32_t>(response.status),
    };
    return WriteExact(pipe, &header, sizeof(header)) &&
        (response.payload.empty() ||
            WriteExact(
                pipe,
                response.payload.data(),
                static_cast<std::uint32_t>(response.payload.size())));
}

PipeServer::Response PipeServer::Dispatch(
    const Request& request,
    EngineRuntime& runtime)
{
    if (request.header.version != protocol::Version)
        return {protocol::Status::UnsupportedVersion};

    switch (RequestType(request.header))
    {
    case protocol::MessageType::Hello:
        return request.payload.empty()
            ? HandleHello()
            : Response{protocol::Status::BadRequest};
    case protocol::MessageType::LoadField:
        return HandleLoadField(request, runtime);
    case protocol::MessageType::Ping:
        return request.payload.empty()
            ? HandlePing()
            : Response{protocol::Status::BadRequest};
    case protocol::MessageType::Shutdown:
        return request.payload.empty()
            ? Response{protocol::Status::Success, {}, true}
            : Response{protocol::Status::BadRequest};
    case protocol::MessageType::AddBot:
        return HandleAddBot(request, runtime);
    case protocol::MessageType::Tick:
        return HandleTick(request, runtime);
    case protocol::MessageType::Snapshot:
        return HandleSnapshot(request, runtime);
    case protocol::MessageType::Input:
        return HandleInput(request, runtime);
    case protocol::MessageType::Aim:
        return HandleAim(request, runtime);
    case protocol::MessageType::Lifecycle:
        return HandleLifecycle(request, runtime);
    default:
        return {protocol::Status::UnsupportedMessage};
    }
}

PipeServer::Response PipeServer::HandleLoadField(
    const Request& request,
    EngineRuntime& runtime)
{
    if (fieldId_ != 0)
        return {protocol::Status::InvalidState};
    if (request.payload.size() != sizeof(protocol::LoadFieldRequest))
        return {protocol::Status::BadRequest};

    protocol::LoadFieldRequest payload{};
    std::memcpy(&payload, request.payload.data(), sizeof(payload));
    const auto length = strnlen(
        payload.worldName, protocol::WorldNameCapacity);
    const std::string worldName(payload.worldName, length);
    if (payload.fieldId == 0 || payload.maximumBots == 0 ||
        payload.maximumBots > runtime.LocalPlayerCapacity() ||
        payload.mapId < 200 || payload.mapId > 213 ||
        payload.mode < 1 || payload.mode > 4 ||
        length == 0 || length == protocol::WorldNameCapacity ||
        !IsValidWorldName(worldName))
        return {protocol::Status::BadRequest};

    try
    {
        runtime.LoadWorld(worldName, payload.mapId, payload.mode);
    }
    catch (const std::exception& error)
    {
        std::cerr << "LoadField falhou: " << error.what() << '\n';
        return {protocol::Status::EngineFailure};
    }
    catch (char* error)
    {
        std::cerr << "LoadField recusado pela engine: "
                  << (error ? error : "<sem mensagem>") << '\n';
        return {protocol::Status::EngineFailure};
    }

    fieldId_ = payload.fieldId;
    maximumBots_ = payload.maximumBots;
    return {
        protocol::Status::Success,
        Encode(protocol::LoadFieldResponse{fieldId_, maximumBots_}),
    };
}

PipeServer::Response PipeServer::HandleHello() const
{
    const protocol::HelloResponse payload{
        GetCurrentProcessId(),
        protocol::EngineBootstrap | protocol::NativeWorld |
            protocol::NativePlayerSources | protocol::NativeSnapshots |
            protocol::NativeInputs | protocol::NativeTargeting |
            protocol::NativeLifecycle,
        protocol::Version,
        0,
    };
    return {protocol::Status::Success, Encode(payload)};
}

PipeServer::Response PipeServer::HandlePing() const
{
    const auto now = std::chrono::steady_clock::now().time_since_epoch();
    const auto milliseconds =
        std::chrono::duration_cast<std::chrono::milliseconds>(now).count();
    const protocol::PingResponse payload{
        static_cast<std::uint64_t>(milliseconds),
        fieldId_,
        botCount_,
    };
    return {protocol::Status::Success, Encode(payload)};
}

PipeServer::Response PipeServer::HandleAddBot(
    const Request& request,
    EngineRuntime& runtime)
{
    if (fieldId_ == 0)
        return {protocol::Status::InvalidState};
    if (request.payload.size() != sizeof(protocol::AddBotRequest))
        return {protocol::Status::BadRequest};

    protocol::AddBotRequest payload{};
    std::memcpy(&payload, request.payload.data(), sizeof(payload));
    const std::string name = ReadText(payload.name);
    const std::string species = ReadText(payload.species);
    if (payload.botId == 0 || !IsValidPlayerText(name) ||
        !IsValidPlayerText(species) || botCount_ >= maximumBots_)
        return {protocol::Status::BadRequest};

    try
    {
        const auto probe = runtime.AddLocalPlayer(
            payload.botId, name, species);
        botCount_ = probe.activePlayers;
        return {
            protocol::Status::Success,
            Encode(protocol::AddBotResponse{
                probe.botId,
                probe.activePlayers,
                probe.capacity}),
        };
    }
    catch (const std::logic_error& error)
    {
        std::cerr << "AddBot recusado: " << error.what() << '\n';
        return {protocol::Status::InvalidState};
    }
    catch (const std::exception& error)
    {
        std::cerr << "AddBot falhou: " << error.what() << '\n';
        return {protocol::Status::EngineFailure};
    }
    catch (char* error)
    {
        std::cerr << "AddBot recusado pela engine: "
                  << (error ? error : "<sem mensagem>") << '\n';
        return {protocol::Status::EngineFailure};
    }
}

}
