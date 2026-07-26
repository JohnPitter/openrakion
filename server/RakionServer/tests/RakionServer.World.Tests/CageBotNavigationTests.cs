using System;
using RakionServer.World.Domain;
using RakionServer.World.Navigation;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CageBotNavigationTests
    {
        [Fact]
        public void Surface_BlocksCentralBarrierAndLeavesEastOpening()
        {
            BattleMapNavigationSurface surface = BattleMapNavigationSurface.Instance;
            var above = new BotVector(2000, 0, -400);

            BotMoveResolution blocked = surface.Resolve(
                BattleMapNavigationSurface.CageMapId,
                above,
                new BotVector(2100, 0, -700));
            BotMoveResolution opening = surface.Resolve(
                BattleMapNavigationSurface.CageMapId,
                new BotVector(4000, 0, -400),
                new BotVector(4000, 0, -700));

            Assert.True(blocked.Blocked);
            Assert.Equal(-550, blocked.Position.Z);
            Assert.False(opening.Blocked);
            Assert.Equal(-700, opening.Position.Z);
        }

        [Fact]
        public void SyntheticBot_UsesControlsToBypassCageBarrier()
        {
            var bot = new BotPlayer
            {
                Name = "Rok",
                Seat = 10,
                Profile = BotProfile.Normal,
                Position = new BotVector(1950, 0, -400)
            };
            var target = new BotVector(3000, 0, -2200);
            bool jumped = false;
            bool strafed = false;
            bool attacked = false;
            float maximumX = bot.Position.X;

            for (int tick = 0; tick < 400; tick++)
            {
                long now = tick * 150L;
                BotNavigationAction action = bot.TickNavigated(
                    target,
                    0,
                    BattleMapNavigationSurface.CageMapId,
                    now,
                    0.15f,
                    BattleMapNavigationSurface.Instance);
                jumped |= action.IsJumping;
                strafed |= (action.Controls &
                    (BotControls.A | BotControls.D)) != 0;
                attacked |= action.IsAttacking;
                maximumX = MathF.Max(maximumX, bot.Position.X);
                if (attacked) break;
            }

            Assert.True(jumped, "o contorno deve pulsar Space");
            Assert.True(strafed, "o contorno deve usar A ou D");
            Assert.True(maximumX > 3900,
                $"o bot deve alcançar a abertura lateral do Cage; maxX={maximumX}");
            Assert.True(attacked, "o bot deve reencontrar o alvo após o contorno");
        }
    }
}
