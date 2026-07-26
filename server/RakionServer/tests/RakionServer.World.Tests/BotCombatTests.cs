using System;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BotCombatTests
{
    [Fact]
    public void AttackWindowRejectsDuplicateAndRateLimitedSequences()
    {
        PlayerCombatState combat = new();

        Assert.True(combat.TryOpenAttack(10, 1_000));
        Assert.False(combat.TryOpenAttack(10, 1_100));
        Assert.False(combat.TryOpenAttack(11, 1_200));
        Assert.True(combat.TryOpenAttack(12, 1_250));
    }

    [Fact]
    public void AttackResolvesOnlyInsideActiveWindow()
    {
        (Field field, PlayerRec attacker) = Match();
        AddBot(field, 1, new BotVector(0, 0, 2));
        Assert.True(attacker.Combat.TryOpenAttack(1, 1_000));

        Assert.False(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_119, 40, out _));
        Assert.True(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_120, 40, out BotCombatHit hit));
        Assert.Equal(1u, hit.HitSequence);
        Assert.False(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_121, 40, out _));
    }

    [Fact]
    public void ActiveWindowCanAcquireTargetBeforeItCloses()
    {
        (Field field, PlayerRec attacker) = Match();
        PlayerRec target = AddBot(field, 1, new BotVector(0, 0, 4));
        Assert.True(attacker.Combat.TryOpenAttack(1, 1_000));
        Assert.False(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_120, 40, out _));

        target.Position = new BotVector(0, 0, 3);

        Assert.True(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_300, 40, out _));
    }

    [Fact]
    public void AttackDamagesNearestEnemyInFrontUsingEngineScale()
    {
        (Field field, PlayerRec attacker) = Match();
        PlayerRec nearest = AddBot(field, 1, new BotVector(0, 0, 2));
        PlayerRec farther = AddBot(field, 1, new BotVector(0, 0, 3));
        int fartherHealth = farther.Bot!.Health;
        attacker.Combat.TryOpenAttack(1, 1_000);

        Assert.True(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_120, 40, out BotCombatHit hit));
        Assert.Same(nearest, hit.BotRecord);
        Assert.Equal(fartherHealth, farther.Bot.Health);
        Assert.Equal(1u, nearest.Bot!.DamageSequence);
        Assert.Equal((byte)attacker.Slot, nearest.Bot.LastAttackerSeat);
    }

    [Theory]
    [InlineData(1, 0, -2, 0)]
    [InlineData(1, 0, 3.26, 0)]
    [InlineData(0, 0, 2, 0)]
    [InlineData(1, 0, 2, 2.01)]
    public void AttackRejectsInvalidHitbox(
        byte team,
        float x,
        float z,
        float y)
    {
        (Field field, PlayerRec attacker) = Match();
        AddBot(field, team, new BotVector(x, y, z));
        attacker.Combat.TryOpenAttack(1, 1_000);

        Assert.False(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_120, 40, out _));
    }

    [Fact]
    public void AttackKillsBotExactlyOnce()
    {
        (Field field, PlayerRec attacker) = Match();
        PlayerRec target = AddBot(field, 1, new BotVector(0, 0, 2));
        attacker.Combat.TryOpenAttack(1, 1_000);

        Assert.True(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_120, 999, out BotCombatHit hit));
        Assert.True(hit.Died);
        Assert.False(target.Bot!.Alive);
        Assert.Equal(0, target.Bot.Health);
        Assert.False(BotCombat.TryResolveHumanAttack(
            field, attacker, 1_121, 999, out _));
    }

    private static (Field Field, PlayerRec Attacker) Match()
    {
        Field field = new(1)
        {
            Mode = (byte)GameMode.Deathmatch,
            State = 2,
            Phase = MatchPhase.Playing
        };
        PlayerRec attacker = field.Slots[0];
        attacker.State = 4;
        attacker.Position = BotVector.Zero;
        return (field, attacker);
    }

    private static PlayerRec AddBot(
        Field field,
        byte team,
        BotVector position)
    {
        BotPlayer bot = new()
        {
            Name = "B",
            Team = team,
            Position = position
        };
        bot.InitHealth(1);
        bot.AttachEngine();
        int seat = field.AddBot(bot, team);
        PlayerRec record = field.Slots[seat];
        record.State = 4;
        record.Position = position;
        return record;
    }
}
