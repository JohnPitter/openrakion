#include <cmath>
#include <cstdlib>

#include "headless_navigation.h"

namespace
{
void Require(bool condition)
{
    if (!condition) std::abort();
}

HeadlessNavigationAction Update(
    std::uint32_t now, float distance, float x = 0.0f, float z = 0.0f)
{
    return UpdateHeadlessNavigation({
        now, 1, distance, x, z, 0.0f, -10.0f, true, true});
}
}

int main()
{
    UpdateHeadlessNavigation({});
    HeadlessNavigationAction action = Update(0, 10.0f);
    Require(action.Mode == HeadlessNavigationMode::Approach);
    Require(action.ForwardAxis < 0.0f);

    action = Update(1700, 10.0f);
    Require(action.Mode == HeadlessNavigationMode::BypassDiagonal);
    Require(action.StrafeAxis < 0.0f);
    Require(action.ForwardAxis < 0.0f);
    Require(action.Jump);

    action = Update(1900, 10.0f);
    Require(action.Mode == HeadlessNavigationMode::BypassDiagonal);
    Require(!action.Jump);

    action = Update(7800, 10.0f);
    Require(action.Mode == HeadlessNavigationMode::Approach);
    action = Update(9501, 10.0f);
    Require(action.Mode == HeadlessNavigationMode::BypassDiagonal);
    Require(action.StrafeAxis > 0.0f);

    action = Update(15600, 10.0f, 4.0f);
    Require(action.Mode == HeadlessNavigationMode::Approach);
    action = Update(17301, 10.0f, 4.0f);
    Require(action.Mode == HeadlessNavigationMode::BypassStrafe);
    Require(action.StrafeAxis > 0.0f);
    Require(std::fabs(action.ForwardAxis) < 0.01f);
    Require(action.Jump);

    action = Update(22400, 10.0f, 8.0f);
    Require(action.Mode == HeadlessNavigationMode::Approach);
    action = Update(24101, 10.0f, 8.0f);
    Require(action.Mode == HeadlessNavigationMode::Backstep);
    Require(action.StrafeAxis > 0.0f);
    Require(action.ForwardAxis > 0.0f);
    Require(action.Jump);

    action = Update(24400, 3.0f);
    Require(action.Mode == HeadlessNavigationMode::Attack);
    Require(action.Attack);
    return EXIT_SUCCESS;
}
