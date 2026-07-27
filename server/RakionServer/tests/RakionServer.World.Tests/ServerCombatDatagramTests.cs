using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class ServerCombatDatagramTests
{
    [Fact]
    public void DamageEventMatchesTypedEntityContract()
    {
        byte[] packet = ServerCombatDatagrams.Damage(
            new ServerDamageEvent(
                10, 0, 77, 25, new BotVector(0, 0, 1)));

        Assert.True(GameplayPeerDatagramCodec.TryParsePlayerDamage(
            packet, out GameplayPlayerDamage damage));
        Assert.Equal(77u, damage.Envelope.Sequence);
        Assert.Equal((byte)10, damage.Envelope.SenderSeat);
        Assert.Equal((byte)0, damage.Envelope.PrimaryEntitySeat);
        Assert.Equal(25f, damage.FirstDamageValue);
    }

    [Fact]
    public void VitalsEventCarriesAuthoritativeHpAndArmor()
    {
        PlayerCombatVitals vitals = new();
        vitals.Initialize(116, 114);
        vitals.ApplyDamage(20, 10);
        byte[] packet = ServerCombatDatagrams.Vitals(
            new ServerVitalsEvent(10, 0, 78),
            vitals);

        Assert.True(GameplayPeerDatagramCodec.TryParsePlayerVitals(
            packet, out GameplayPlayerVitals value));
        Assert.Equal(116f, value.Hp);
        Assert.Equal(94f, value.Ap);
    }

    [Fact]
    public void DeathAndRespawnUseExactEntityPayloadSizes()
    {
        byte[] death = ServerCombatDatagrams.Death(
            new ServerDeathEvent(
                10, 0, 79, new BotVector(0, 0, 1)));
        byte[] respawn = ServerCombatDatagrams.Respawn(0, 80);

        Assert.True(GameplayPeerDatagramCodec.TryParsePlayerDeath(
            death, out _));
        Assert.True(GameplayPeerDatagramCodec.TryParseEntityEvent(
            respawn, out GameplayEntityEvent envelope));
        Assert.Equal(GameplayPeerDatagramCodec.RespawnEventId, envelope.EventId);
        Assert.Equal(0, envelope.PayloadLength);
    }
}
