using System;
using RakionServer.World.Domain;
using RakionServer.World.Navigation;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CageBotNavigationTests
    {
        [Fact]
        public void Surface_BlocksCentralObstacleAndSlidesAlongItsEdge()
        {
            BattleMapNavigationSurface surface = BattleMapNavigationSurface.Instance;
            var above = new BotVector(2000, 0, -400);

            BotMoveResolution blocked = surface.Resolve(
                BattleMapNavigationSurface.CageMapId,
                above,
                new BotVector(2100, 0, -700));
            BotMoveResolution slide = surface.Resolve(
                BattleMapNavigationSurface.CageMapId,
                new BotVector(2000, 0, -470),
                new BotVector(2200, 0, -470));

            Assert.True(blocked.Blocked);
            Assert.Equal(-470, blocked.Position.Z);
            Assert.False(slide.Blocked);
            Assert.Equal(2200, slide.Position.X);
            Assert.Equal(-470, slide.Position.Z);
        }

        [Fact]
        public void Surface_NeverLetsLargeStepTunnelThroughCentralObstacle()
        {
            BattleMapNavigationSurface surface = BattleMapNavigationSurface.Instance;

            BotMoveResolution resolved = surface.Resolve(
                BattleMapNavigationSurface.CageMapId,
                new BotVector(1800, 0, -900),
                new BotVector(3200, 0, -900));

            Assert.True(resolved.Blocked);
            Assert.Equal(1870, resolved.Position.X);
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
            bool clearedObstacle = false;

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
                clearedObstacle |= bot.Position.Z <= -1330 &&
                    (bot.Position.X <= 1870 || bot.Position.X >= 3130);
                if (attacked) break;
            }

            Assert.True(jumped, "o contorno deve pulsar Space");
            Assert.True(strafed, "o contorno deve usar A ou D");
            Assert.True(clearedObstacle,
                "o bot deve sair do retângulo sólido antes de cruzar para o outro lado");
            Assert.True(attacked, "o bot deve reencontrar o alvo após o contorno");
        }
    }
}
