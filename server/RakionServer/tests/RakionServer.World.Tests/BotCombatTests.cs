using System;
using RakionServer.World.Domain;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BotCombatTests
{
    /// <summary>
    /// Segurar o botão de ataque repete a mesma animação com sequência crescente. Sem contar a
    /// borda de subida, cada repetição virava um golpe e o alvo morria durante o carregamento —
    /// defeito visto em jogo.
    /// </summary>
    [Fact]
    public void HeldAttackAnimationOpensASingleSwing()
    {
        var combat = new PlayerCombatState();
        long now = 10_000;

        Assert.True(combat.TryOpenAttack(1, now, animationId: 0x19));
        Assert.False(combat.TryOpenAttack(2, now + 300, animationId: 0x19));
        Assert.False(combat.TryOpenAttack(3, now + 600, animationId: 0x19));

        // Troca de golpe (combo) conta como novo ataque.
        Assert.True(combat.TryOpenAttack(4, now + 900, animationId: 0x18));

        // Soltar o botão (animação que não é ataque) rearma o mesmo golpe.
        combat.ReleaseAttackAnimation();
        Assert.True(combat.TryOpenAttack(5, now + 1_200, animationId: 0x18));
    }

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

    [Fact]
    public void BotAttackUsesWindowHitboxAndArmorFirstDamage()
    {
        Field field = Match().Field;
        PlayerRec target = field.Slots[0];
        target.Position = new BotVector(0, 0, 2);
        target.Vitals.Initialize(100, 20);
        PlayerRec attacker = AddBot(
            field, 1, new BotVector(0, 0, 0));
        attacker.Bot!.TargetSeat = 0;
        Assert.True(attacker.Bot.TryStartAttack(1_000));

        // Janela de bot abre no mesmo tick (impacto imediato).
        Assert.True(BotCombat.TryResolveBotAttack(
            field, attacker, 1_000, 15, out BotHumanCombatHit hit));
        Assert.Equal(100, hit.Damage.RemainingHp);
        Assert.Equal(5, hit.Damage.RemainingAp);
        Assert.False(hit.Damage.Died);
        Assert.False(BotCombat.TryResolveBotAttack(
            field, attacker, 1_001, 15, out _));
    }

    [Fact]
    public void BotAttackKillsHumanExactlyOnce()
    {
        Field field = Match().Field;
        PlayerRec target = field.Slots[0];
        target.Position = new BotVector(0, 0, 2);
        target.Vitals.Initialize(10, 0);
        PlayerRec attacker = AddBot(
            field, 1, new BotVector(0, 0, 0));
        attacker.Bot!.TargetSeat = 0;
        attacker.Bot.TryStartAttack(1_000);

        Assert.True(BotCombat.TryResolveBotAttack(
            field, attacker, 1_000, 20, out BotHumanCombatHit hit));
        Assert.True(hit.Damage.Died);
        Assert.Equal(0, target.Vitals.Hp);
        Assert.False(BotCombat.TryResolveBotAttack(
            field, attacker, 1_001, 20, out _));
    }

    [Fact]
    public void HumanVitalsRespawnAtFullHpAndArmor()
    {
        PlayerCombatVitals vitals = new();
        vitals.Initialize(120, 30);
        Assert.True(vitals.ApplyDamage(200, 10).Died);
        vitals.ScheduleRespawn(1_000, 7_000);

        Assert.False(vitals.TryRespawn(7_999));
        Assert.True(vitals.TryRespawn(8_000));
        Assert.Equal(120, vitals.Hp);
        Assert.Equal(30, vitals.Ap);
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
