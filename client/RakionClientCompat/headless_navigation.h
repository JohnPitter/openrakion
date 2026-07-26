#pragma once

#include <cstdint>

enum class HeadlessNavigationMode
{
    Idle,
    Approach,
    Attack,
    Backward,
    BypassDiagonal,
    BypassStrafe,
    Backstep
};

struct HeadlessNavigationInput
{
    std::uint32_t Now{};
    std::uintptr_t Target{};
    float Distance{};
    float LocalX{};
    float LocalZ{};
    float TargetX{};
    float TargetZ{};
    bool HasTarget{};
    bool PreferLeft{};
};

struct HeadlessNavigationAction
{
    float StrafeAxis{};
    float ForwardAxis{};
    bool Jump{};
    bool Attack{};
    HeadlessNavigationMode Mode{HeadlessNavigationMode::Idle};
};

HeadlessNavigationAction UpdateHeadlessNavigation(
    const HeadlessNavigationInput& input);
const char* HeadlessNavigationModeName(HeadlessNavigationMode mode);
