#include "engine_runtime.h"

#include <algorithm>
#include <stdexcept>

#include "engine_invocation.h"
#include "native_player.h"

namespace bot_engine
{
void EngineRuntime::Advance(std::uint32_t frameCount)
{
    if (!worldLoaded_ || frameCount == 0 || frameCount > 100)
        throw std::logic_error("Avanço solicitado em estado inválido.");

    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    auto** timer = reinterpret_cast<void**>(ResolveRequired(TimerSymbol));
    auto mainLoop = reinterpret_cast<EngineStep>(
        ResolveRequired(NetworkMainLoopSymbol));
    auto handleTimerHandlers = reinterpret_cast<EngineStep>(
        ResolveRequired(TimerHandlersSymbol));
    if (!network || !*network || !timer || !*timer)
        throw std::runtime_error("Engine sem network/timer para avançar.");

    for (std::uint32_t frame = 0; frame < frameCount; ++frame)
    {
        if (!AdvanceEngineSafely(
                handleTimerHandlers, *timer, mainLoop, *network))
            throw std::runtime_error("A engine falhou durante o tick.");
    }
}

PlayerSnapshot EngineRuntime::Snapshot(std::uint32_t botId) const
{
    const auto source = botSources_.find(botId);
    if (source == botSources_.end())
        throw std::invalid_argument("Bot não existe neste worker.");
    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    const auto native = InspectNativePlayer(
        engine_, entities_, *network, source->second, botId);
    PlayerSnapshot snapshot{
        native.botId,
        native.ready,
        native.alive,
        {},
        {},
        native.hp};
    std::copy_n(native.position, 3, snapshot.position);
    std::copy_n(native.rotation, 3, snapshot.rotation);
    return snapshot;
}

void EngineRuntime::ApplyInput(
    std::uint32_t botId,
    std::uint32_t inputFlags)
{
    const auto source = botSources_.find(botId);
    if (source == botSources_.end())
        throw std::invalid_argument("Bot não existe neste worker.");
    ApplyNativeInput(engine_, source->second, inputFlags);
}

void EngineRuntime::Aim(std::uint32_t botId, const float* target)
{
    const auto source = botSources_.find(botId);
    if (source == botSources_.end())
        throw std::invalid_argument("Bot não existe neste worker.");
    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    if (!network || !*network)
        throw std::runtime_error("Engine sem network para orientar o bot.");
    AimNativePlayer(engine_, *network, source->second, target);
}

void EngineRuntime::SetLifecycle(std::uint32_t botId, bool alive)
{
    const auto source = botSources_.find(botId);
    if (source == botSources_.end())
        throw std::invalid_argument("Bot não existe neste worker.");
    auto** network = reinterpret_cast<void**>(ResolveRequired(NetworkSymbol));
    if (!network || !*network)
        throw std::runtime_error("Engine sem network para lifecycle.");
    SetNativeLifecycle(
        engine_, entities_, *network, source->second, alive);
}
}
