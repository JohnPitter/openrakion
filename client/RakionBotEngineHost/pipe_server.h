#pragma once

#include <windows.h>

#include <cstdint>
#include <string>
#include <vector>

#include "bot_engine_protocol.h"
#include "engine_runtime.h"

namespace bot_engine
{
class PipeServer final
{
public:
    explicit PipeServer(std::wstring pipeName);
    int Run(EngineRuntime& runtime);

private:
    struct Request
    {
        protocol::FrameHeader header{};
        std::vector<std::uint8_t> payload;
    };

    struct Response
    {
        protocol::Status status{protocol::Status::Success};
        std::vector<std::uint8_t> payload;
        bool stop{};
    };

    HANDLE CreatePipe() const;
    bool ReadRequest(HANDLE pipe, Request& request) const;
    bool WriteResponse(
        HANDLE pipe,
        const Request& request,
        const Response& response) const;
    Response Dispatch(const Request& request, EngineRuntime& runtime);
    Response HandleLoadField(
        const Request& request,
        EngineRuntime& runtime);
    Response HandleHello() const;
    Response HandlePing() const;
    Response HandleAddBot(
        const Request& request,
        EngineRuntime& runtime);
    Response HandleTick(
        const Request& request,
        EngineRuntime& runtime);
    Response HandleSnapshot(
        const Request& request,
        EngineRuntime& runtime);
    Response HandleInput(
        const Request& request,
        EngineRuntime& runtime);
    Response HandleAim(
        const Request& request,
        EngineRuntime& runtime);
    Response HandleLifecycle(
        const Request& request,
        EngineRuntime& runtime);
    Response HandleDamageReaction(
        const Request& request,
        EngineRuntime& runtime);

    template<typename T>
    static std::vector<std::uint8_t> Encode(const T& value)
    {
        const auto* begin =
            reinterpret_cast<const std::uint8_t*>(&value);
        return {begin, begin + sizeof(T)};
    }

    std::wstring pipeName_;
    std::uint32_t fieldId_{};
    std::uint32_t maximumBots_{};
    std::uint32_t botCount_{};
};
}
