#include <cmath>

#include "headless_navigation.h"

namespace
{
constexpr float AttackRange = 3.25f;
constexpr float MinimumDistance = 1.25f;
constexpr float ProgressThreshold = 0.45f;
constexpr float MovementThreshold = 0.30f;
constexpr float ManeuverMovementThreshold = 0.75f;
constexpr float ManeuverDistanceRegression = 1.0f;
constexpr float ForwardAxis = -6.0f;
constexpr float BypassForwardAxis = -4.0f;
constexpr float BackwardAxis = 3.0f;
constexpr float StrafeAxis = 3.6f;
constexpr std::uint32_t StuckMilliseconds = 1600;
constexpr std::uint32_t DiagonalMilliseconds = 6000;
constexpr std::uint32_t StrafeMilliseconds = 5000;
constexpr std::uint32_t BackstepMilliseconds = 1200;
constexpr std::uint32_t JumpCycleMilliseconds = 900;
constexpr std::uint32_t JumpWindowMilliseconds = 180;

struct NavigationState
{
    std::uintptr_t Target{};
    float BestDistance{};
    float LastX{};
    float LastZ{};
    std::uint32_t LastProgressAt{};
    std::uint32_t ManeuverStartedAt{};
    std::uint32_t ManeuverUntil{};
    float ManeuverStartX{};
    float ManeuverStartZ{};
    float ManeuverStartDistance{};
    float BypassDirectionX{};
    float BypassDirectionZ{};
    unsigned int FailedAttempts{};
    float Side{};
    HeadlessNavigationMode Mode{HeadlessNavigationMode::Idle};
};

NavigationState State{};

void BeginTracking(const HeadlessNavigationInput& input)
{
    State = {};
    State.Target = input.Target;
    State.BestDistance = input.Distance;
    State.LastX = input.LocalX;
    State.LastZ = input.LocalZ;
    State.LastProgressAt = input.Now;
    State.Side = input.PreferLeft ? -1.0f : 1.0f;
    State.Mode = HeadlessNavigationMode::Approach;
}

bool HasMoved(const HeadlessNavigationInput& input)
{
    const float x = input.LocalX - State.LastX;
    const float z = input.LocalZ - State.LastZ;
    return std::sqrt(x * x + z * z) >= MovementThreshold;
}

void StartBypass(const HeadlessNavigationInput& input)
{
    const unsigned int strategy = State.FailedAttempts++ % 4;
    const float targetX = input.TargetX - input.LocalX;
    const float targetZ = input.TargetZ - input.LocalZ;
    const float targetLength = std::sqrt(
        targetX * targetX + targetZ * targetZ);

    State.ManeuverStartedAt = input.Now;
    State.LastProgressAt = input.Now;
    State.BestDistance = input.Distance;
    State.ManeuverStartX = input.LocalX;
    State.ManeuverStartZ = input.LocalZ;
    State.ManeuverStartDistance = input.Distance;
    if (targetLength > 0.001f)
    {
        State.BypassDirectionX = -targetZ / targetLength * State.Side;
        State.BypassDirectionZ = targetX / targetLength * State.Side;
    }
    else
    {
        State.BypassDirectionX = State.Side;
        State.BypassDirectionZ = 0.0f;
    }
    if (strategy < 2)
    {
        State.Mode = HeadlessNavigationMode::BypassDiagonal;
        State.ManeuverUntil = input.Now + DiagonalMilliseconds;
    }
    else if (strategy == 2)
    {
        State.Mode = HeadlessNavigationMode::BypassStrafe;
        State.ManeuverUntil = input.Now + StrafeMilliseconds;
    }
    else
    {
        State.Mode = HeadlessNavigationMode::Backstep;
        State.ManeuverUntil = input.Now + BackstepMilliseconds;
    }
}

void CompleteManeuver(const HeadlessNavigationInput& input)
{
    const float deltaX = input.LocalX - State.ManeuverStartX;
    const float deltaZ = input.LocalZ - State.ManeuverStartZ;
    const float displacement = std::sqrt(
        deltaX * deltaX + deltaZ * deltaZ);
    const bool regressed =
        input.Distance > State.ManeuverStartDistance +
            ManeuverDistanceRegression;
    if (displacement < ManeuverMovementThreshold || regressed)
        State.Side = -State.Side;

    State.Mode = HeadlessNavigationMode::Approach;
    State.LastProgressAt = input.Now;
    State.BestDistance = input.Distance;
}

bool IsManeuverActive(std::uint32_t now)
{
    return static_cast<std::int32_t>(State.ManeuverUntil - now) > 0;
}

bool ShouldJump(std::uint32_t now)
{
    return (now - State.ManeuverStartedAt) % JumpCycleMilliseconds <
        JumpWindowMilliseconds;
}

float ResolveStrafeAxis(const HeadlessNavigationInput& input)
{
    const float targetX = input.TargetX - input.LocalX;
    const float targetZ = input.TargetZ - input.LocalZ;
    const float targetLength = std::sqrt(
        targetX * targetX + targetZ * targetZ);
    if (targetLength <= 0.001f) return State.Side * StrafeAxis;

    const float rightX = -targetZ / targetLength;
    const float rightZ = targetX / targetLength;
    const float alignment =
        rightX * State.BypassDirectionX +
        rightZ * State.BypassDirectionZ;
    return alignment >= 0.0f ? StrafeAxis : -StrafeAxis;
}

HeadlessNavigationAction BuildManeuverAction(
    const HeadlessNavigationInput& input)
{
    HeadlessNavigationAction action{};
    action.Mode = State.Mode;
    action.StrafeAxis = ResolveStrafeAxis(input);
    action.Jump = ShouldJump(input.Now);
    if (State.Mode == HeadlessNavigationMode::BypassDiagonal)
        action.ForwardAxis = BypassForwardAxis;
    else if (State.Mode == HeadlessNavigationMode::Backstep)
        action.ForwardAxis = BackwardAxis;
    return action;
}
}

HeadlessNavigationAction UpdateHeadlessNavigation(
    const HeadlessNavigationInput& input)
{
    if (!input.HasTarget)
    {
        State = {};
        return {};
    }
    if (State.Target != input.Target) BeginTracking(input);

    const bool moved = HasMoved(input);
    State.LastX = input.LocalX;
    State.LastZ = input.LocalZ;
    if (input.Distance <= State.BestDistance - ProgressThreshold)
    {
        State.BestDistance = input.Distance;
        State.LastProgressAt = input.Now;
    }

    if (input.Distance >= MinimumDistance && input.Distance <= AttackRange)
    {
        State.Mode = HeadlessNavigationMode::Attack;
        State.ManeuverUntil = 0;
        State.LastProgressAt = input.Now;
        return {0.0f, 0.0f, false, true, State.Mode};
    }
    if (input.Distance < MinimumDistance)
    {
        State.Mode = HeadlessNavigationMode::Backward;
        return {0.0f, BackwardAxis, false, false, State.Mode};
    }
    if (IsManeuverActive(input.Now))
        return BuildManeuverAction(input);

    if (State.Mode == HeadlessNavigationMode::BypassDiagonal ||
        State.Mode == HeadlessNavigationMode::BypassStrafe ||
        State.Mode == HeadlessNavigationMode::Backstep)
    {
        CompleteManeuver(input);
    }
    else if (!moved &&
             input.Now - State.LastProgressAt >= StuckMilliseconds)
    {
        StartBypass(input);
        return BuildManeuverAction(input);
    }

    State.Mode = HeadlessNavigationMode::Approach;
    return {0.0f, ForwardAxis, false, false, State.Mode};
}

const char* HeadlessNavigationModeName(HeadlessNavigationMode mode)
{
    switch (mode)
    {
    case HeadlessNavigationMode::Idle: return "parado";
    case HeadlessNavigationMode::Approach: return "aproximacao";
    case HeadlessNavigationMode::Attack: return "ataque";
    case HeadlessNavigationMode::Backward: return "recuo";
    case HeadlessNavigationMode::BypassDiagonal: return "desvio W+A/D+Space";
    case HeadlessNavigationMode::BypassStrafe: return "desvio A/D+Space";
    case HeadlessNavigationMode::Backstep: return "desvio S+A/D+Space";
    default: return "desconhecido";
    }
}
