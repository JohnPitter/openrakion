using System;
using System.Buffers.Binary;
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

    /// <summary>
    /// Layout de `EPlayerDamage 0x0191000B` fechado na RE do cliente (docs/protocol):
    /// `u32 playerId | u8 damageType | u8 damageMotionType | u16 reserved |
    ///  f32 firstDamageValue | f32 secondDamageValue | vec3f first | vec3f second` = 40 bytes.
    /// O consumidor é `CPlayer::ReceiveDamage`, então cada campo tem que cair no offset certo.
    /// </summary>
    [Fact]
    public void DamagePayloadMatchesReversedClientLayout()
    {
        var direction = new BotVector(0.25f, 0f, -0.75f);
        byte[] packet = ServerCombatDatagrams.Damage(
            new ServerDamageEvent(3, 10, 91, 52, direction));

        ReadOnlySpan<byte> payload = packet.AsSpan(19);
        Assert.Equal(40, payload.Length);
        Assert.Equal(10u, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(11, payload[4]);   // damageType: melee
        Assert.Equal(4, payload[5]);    // damageMotionType: knockdown
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]));
        Assert.Equal(52f, BinaryPrimitives.ReadSingleLittleEndian(payload[8..]));
        Assert.Equal(direction.X, BinaryPrimitives.ReadSingleLittleEndian(payload[16..]));
        Assert.Equal(direction.Y, BinaryPrimitives.ReadSingleLittleEndian(payload[20..]));
        Assert.Equal(direction.Z, BinaryPrimitives.ReadSingleLittleEndian(payload[24..]));
        Assert.Equal(direction.X, BinaryPrimitives.ReadSingleLittleEndian(payload[28..]));
        Assert.Equal(direction.Z, BinaryPrimitives.ReadSingleLittleEndian(payload[36..]));
    }

    /// <summary>
    /// Envelope medido no fio do cliente original: `[u8 sender][u8 classe=1][u8 idxA][u8 idxB=0]
    /// [u32 evento][u32 len][payload]`. O segundo índice é sempre zero — com o assento do atacante
    /// ali o cliente não resolve a entidade e não roda a reação de dano (queda e contador).
    /// </summary>
    [Fact]
    public void EventEnvelopeMatchesClientEntityIndexing()
    {
        byte[] packet = ServerCombatDatagrams.Damage(
            new ServerDamageEvent(3, 10, 91, 52, new BotVector(0, 0, 1)));

        Assert.Equal(3, packet[6]);
        Assert.Equal(3, packet[7]);
        Assert.Equal(1, packet[8]);    // classe: player
        Assert.Equal(10, packet[9]);   // índice da entidade alvo
        Assert.Equal(0, packet[10]);   // segundo índice: sempre zero
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
