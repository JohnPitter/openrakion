using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BotAuthoritativeDeathPolicyTests
{
    [Fact]
    public void AcceptsSentinelAfterServerConfirmedBotDeath()
    {
        (Field field, PlayerRec victim) = MatchWithBot();
        victim.Vitals.Initialize(10, 0);
        Assert.True(victim.Vitals.ApplyDamage(10, 10).Died);
        victim.Dead = true;

        Assert.True(BotAuthoritativeDeathPolicy.IsClientEcho(
            field, victim, Field.NoSeat));
    }

    [Fact]
    public void AcceptsLegacyInvalidKillerAfterServerConfirmedBotDeath()
    {
        (Field field, PlayerRec victim) = MatchWithBot();
        victim.Vitals.Initialize(10, 0);
        victim.Vitals.ApplyDamage(10, 10);
        victim.Dead = true;

        Assert.True(BotAuthoritativeDeathPolicy.IsClientEcho(
            field, victim, 0x8D));
    }

    [Fact]
    public void RejectsEchoWithoutAuthoritativeBotDeath()
    {
        (Field field, PlayerRec victim) = MatchWithBot();
        victim.Vitals.Initialize(10, 0);
        victim.Dead = true;

        Assert.False(BotAuthoritativeDeathPolicy.IsClientEcho(
            field, victim, 0x8D));
    }

    private static (Field Field, PlayerRec Victim) MatchWithBot()
    {
        Field field = new(1)
        {
            Mode = (byte)GameMode.Deathmatch,
            State = 2,
            Phase = MatchPhase.Playing
        };
        PlayerRec victim = field.Slots[0];
        victim.State = 4;
        BotPlayer bot = new() { Name = "Bot", Team = 1 };
        bot.InitHealth(1);
        field.AddBot(bot, 1);
        field.Slots[10].State = 4;
        return (field, victim);
    }
}
