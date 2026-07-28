using System;
using System.Buffers.Binary;
using RakionServer.World.Domain;

namespace RakionServer.World.Network;

public readonly record struct ServerDamageEvent(
    byte AttackerSeat,
    byte VictimSeat,
    uint Sequence,
    int Damage,
    BotVector Direction);

public readonly record struct ServerVitalsEvent(
    byte SourceSeat,
    byte VictimSeat,
    uint Sequence);

public readonly record struct ServerDeathEvent(
    byte AttackerSeat,
    byte VictimSeat,
    uint Sequence,
    BotVector Direction);

public static class ServerCombatDatagrams
{
    private const byte PlayerRoute = 1;
    private const byte MeleeDamageType = 11;
    private const byte KnockdownMotionType = 4;

    public static byte[] Damage(ServerDamageEvent value)
    {
        byte[] payload = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, value.VictimSeat);
        payload[4] = MeleeDamageType;
        payload[5] = KnockdownMotionType;
        BinaryPrimitives.WriteSingleLittleEndian(
            payload.AsSpan(8), value.Damage);
        WriteVector(payload.AsSpan(16), value.Direction);
        WriteVector(payload.AsSpan(28), value.Direction);
        return BuildEvent(
            new CombatEventEnvelope(
                value.AttackerSeat,
                value.VictimSeat,
                value.Sequence,
                GameplayPeerDatagramCodec.PlayerDamageEventId),
            payload);
    }

    public static byte[] Vitals(
        ServerVitalsEvent value,
        PlayerCombatVitals vitals) =>
        Vitals(value, vitals.Hp, vitals.Ap);

    /// <summary>
    /// Vitais por valor: o bot guarda HP no domínio dele (<see cref="Domain.BotPlayer"/>), não em
    /// <see cref="PlayerCombatVitals"/>, e o cliente precisa do mesmo evento para baixar a barra.
    /// </summary>
    public static byte[] Vitals(ServerVitalsEvent value, int hp, int ap)
    {
        byte[] payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, value.VictimSeat);
        BinaryPrimitives.WriteSingleLittleEndian(
            payload.AsSpan(4), hp);
        BinaryPrimitives.WriteSingleLittleEndian(
            payload.AsSpan(8), ap);
        return BuildEvent(
            new CombatEventEnvelope(
                value.SourceSeat,
                value.VictimSeat,
                value.Sequence,
                GameplayPeerDatagramCodec.PlayerRemainHpEventId),
            payload);
    }

    public static byte[] Death(ServerDeathEvent value)
    {
        byte[] payload = new byte[12];
        WriteVector(payload, value.Direction);
        return BuildEvent(
            new CombatEventEnvelope(
                value.AttackerSeat,
                value.VictimSeat,
                value.Sequence,
                GameplayPeerDatagramCodec.PlayerDeathEventId),
            payload);
    }

    public static byte[] Respawn(
        byte victimSeat,
        uint sequence) =>
        BuildEvent(
            new CombatEventEnvelope(
                victimSeat,
                victimSeat,
                sequence,
                GameplayPeerDatagramCodec.RespawnEventId),
            []);

    private static byte[] BuildEvent(
        CombatEventEnvelope envelope,
        ReadOnlySpan<byte> payload)
    {
        byte[] packet = new byte[19 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(
            packet, GameplayPeerDatagramCodec.EntityEventType);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(2), envelope.Sequence);
        packet[6] = envelope.SourceSeat;
        packet[7] = envelope.SourceSeat;
        packet[8] = PlayerRoute;
        packet[9] = envelope.TargetSeat;
        packet[10] = envelope.SourceSeat;
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(11), envelope.EventId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            packet.AsSpan(15), (uint)payload.Length);
        payload.CopyTo(packet.AsSpan(19));
        return packet;
    }

    private static void WriteVector(
        Span<byte> destination,
        BotVector vector)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, vector.X);
        BinaryPrimitives.WriteSingleLittleEndian(
            destination[sizeof(float)..], vector.Y);
        BinaryPrimitives.WriteSingleLittleEndian(
            destination[(sizeof(float) * 2)..], vector.Z);
    }

    private readonly record struct CombatEventEnvelope(
        byte SourceSeat,
        byte TargetSeat,
        uint Sequence,
        uint EventId);
}
