#include "pipe_server.h"

#include <algorithm>
#include <cstring>
#include <iostream>
#include <stdexcept>

namespace bot_engine
{
PipeServer::Response PipeServer::HandleTick(
    const Request& request,
    EngineRuntime& runtime)
{
    if (fieldId_ == 0)
        return {protocol::Status::InvalidState};
    if (request.payload.size() != sizeof(protocol::TickRequest))
        return {protocol::Status::BadRequest};

    protocol::TickRequest payload{};
    std::memcpy(&payload, request.payload.data(), sizeof(payload));
    if (payload.frameCount == 0 || payload.frameCount > 100)
        return {protocol::Status::BadRequest};
    try
    {
        runtime.Advance(payload.frameCount);
        return {
            protocol::Status::Success,
            Encode(protocol::TickResponse{
                payload.frameCount,
                runtime.LocalPlayerCount()}),
        };
    }
    catch (const std::exception& error)
    {
        std::cerr << "Tick falhou: " << error.what() << '\n';
        return {protocol::Status::EngineFailure};
    }
}

PipeServer::Response PipeServer::HandleSnapshot(
    const Request& request,
    EngineRuntime& runtime)
{
    if (fieldId_ == 0)
        return {protocol::Status::InvalidState};
    if (request.payload.size() != sizeof(protocol::SnapshotRequest))
        return {protocol::Status::BadRequest};

    protocol::SnapshotRequest payload{};
    std::memcpy(&payload, request.payload.data(), sizeof(payload));
    try
    {
        const auto snapshot = runtime.Snapshot(payload.botId);
        std::uint32_t flags{};
        if (snapshot.ready)
            flags |= protocol::SnapshotReady;
        if (snapshot.alive)
            flags |= protocol::SnapshotAlive;
        protocol::SnapshotResponse response{
            snapshot.botId, flags, {}, {}, snapshot.hp};
        std::copy_n(snapshot.position, 3, response.position);
        std::copy_n(snapshot.rotation, 3, response.rotation);
        return {protocol::Status::Success, Encode(response)};
    }
    catch (const std::invalid_argument&)
    {
        return {protocol::Status::BadRequest};
    }
    catch (const std::exception& error)
    {
        std::cerr << "Snapshot falhou: " << error.what() << '\n';
        return {protocol::Status::EngineFailure};
    }
}

PipeServer::Response PipeServer::HandleInput(
    const Request& request,
    EngineRuntime& runtime)
{
    if (fieldId_ == 0)
        return {protocol::Status::InvalidState};
    if (request.payload.size() != sizeof(protocol::InputRequest))
        return {protocol::Status::BadRequest};

    protocol::InputRequest payload{};
    std::memcpy(&payload, request.payload.data(), sizeof(payload));
    try
    {
        runtime.ApplyInput(payload.botId, payload.flags);
        return {
            protocol::Status::Success,
            Encode(protocol::InputResponse{
                payload.botId, payload.flags}),
        };
    }
    catch (const std::invalid_argument&)
    {
        return {protocol::Status::BadRequest};
    }
    catch (const std::exception& error)
    {
        std::cerr << "Input falhou: " << error.what() << '\n';
        return {protocol::Status::EngineFailure};
    }
}

PipeServer::Response PipeServer::HandleAim(
    const Request& request,
    EngineRuntime& runtime)
{
    if (fieldId_ == 0)
        return {protocol::Status::InvalidState};
    if (request.payload.size() != sizeof(protocol::AimRequest))
        return {protocol::Status::BadRequest};

    protocol::AimRequest payload{};
    std::memcpy(&payload, request.payload.data(), sizeof(payload));
    try
    {
        runtime.Aim(payload.botId, payload.target);
        return {
            protocol::Status::Success,
            Encode(protocol::AimResponse{payload.botId}),
        };
    }
    catch (const std::invalid_argument&)
    {
        return {protocol::Status::BadRequest};
    }
    catch (const std::exception& error)
    {
        std::cerr << "Aim falhou: " << error.what() << '\n';
        return {protocol::Status::EngineFailure};
    }
}

PipeServer::Response PipeServer::HandleLifecycle(
    const Request& request,
    EngineRuntime& runtime)
{
    if (fieldId_ == 0)
        return {protocol::Status::InvalidState};
    if (request.payload.size() != sizeof(protocol::LifecycleRequest))
        return {protocol::Status::BadRequest};

    protocol::LifecycleRequest payload{};
    std::memcpy(&payload, request.payload.data(), sizeof(payload));
    if (payload.state != protocol::LifecycleState::Alive &&
        payload.state != protocol::LifecycleState::Dead)
        return {protocol::Status::BadRequest};
    try
    {
        runtime.SetLifecycle(
            payload.botId,
            payload.state == protocol::LifecycleState::Alive);
        return {
            protocol::Status::Success,
            Encode(protocol::LifecycleResponse{
                payload.botId, payload.state}),
        };
    }
    catch (const std::invalid_argument&)
    {
        return {protocol::Status::BadRequest};
    }
    catch (const std::exception& error)
    {
        std::cerr << "Lifecycle falhou: " << error.what() << '\n';
        return {protocol::Status::EngineFailure};
    }
}
}
