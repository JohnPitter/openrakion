using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class BotNavigationPlannerTests
    {
        private static readonly BotVector Target = new(0, 0, -1000);

        [Fact]
        public void Update_WhenStuck_UsesRealMovementAndJumpControls()
        {
            var planner = new BotNavigationPlanner();

            BotNavigationAction approach = Update(planner, 0);
            BotNavigationAction bypass = Update(planner, 1700);
            BotNavigationAction jumpWindowEnded = Update(planner, 1900);

            Assert.Equal(BotControls.W, approach.Controls);
            Assert.Equal(BotNavigationMode.BypassDiagonal, bypass.Mode);
            Assert.True(bypass.Controls.HasFlag(BotControls.W));
            Assert.True(bypass.Controls.HasFlag(BotControls.A));
            Assert.True(bypass.Controls.HasFlag(BotControls.Space));
            Assert.False(jumpWindowEnded.Controls.HasFlag(BotControls.Space));
        }

        [Fact]
        public void Update_FailedBypass_FlipsWorldDirection()
        {
            var planner = new BotNavigationPlanner();
            Update(planner, 0);
            BotNavigationAction first = Update(planner, 1700);
            Assert.Equal(BotControls.A,
                first.Controls & (BotControls.A | BotControls.D));

            BotNavigationAction approach = Update(planner, 7800);
            BotNavigationAction second = Update(planner, 9501);

            Assert.Equal(BotNavigationMode.Approach, approach.Mode);
            Assert.Equal(BotControls.D,
                second.Controls & (BotControls.A | BotControls.D));
        }

        [Fact]
        public void Update_WithinRange_AttacksWithoutMovement()
        {
            var planner = new BotNavigationPlanner();

            BotNavigationAction action = planner.Update(new BotNavigationInput(
                0,
                1,
                new BotVector(0, 0, -800),
                Target,
                250,
                100,
                true));

            Assert.Equal(BotNavigationMode.Attack, action.Mode);
            Assert.Equal(BotControls.Attack, action.Controls);
        }

        [Fact]
        public void Update_WhenCollisionBlocksMovement_BypassesImmediately()
        {
            var planner = new BotNavigationPlanner();
            Update(planner, 0);

            BotNavigationAction action = planner.Update(new BotNavigationInput(
                150,
                1,
                BotVector.Zero,
                Target,
                250,
                100,
                true,
                MovementBlocked: true));

            Assert.Equal(BotNavigationMode.BypassDiagonal, action.Mode);
            Assert.True(action.Controls.HasFlag(BotControls.Space));
        }

        private static BotNavigationAction Update(
            BotNavigationPlanner planner,
            long nowMs)
        {
            return planner.Update(new BotNavigationInput(
                nowMs,
                1,
                BotVector.Zero,
                Target,
                250,
                100,
                true));
        }
    }
}
