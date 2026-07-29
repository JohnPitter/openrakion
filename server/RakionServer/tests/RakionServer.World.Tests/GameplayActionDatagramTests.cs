using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class GameplayActionDatagramTests
    {
        [Fact]
        public void Move_ParsesCapturedEngineLayout()
        {
            byte[] packet = System.Convert.FromHexString(
                "0A032700000000650020005E01000092090000A5000000000000");

            Assert.True(GameplayActionDatagram.TryParseMove(packet, out var action));
            Assert.Equal(GameplayActionDatagram.MoveType, action.Header.Type);
            Assert.Equal(0x27u, action.Header.Sequence);
            Assert.Equal((byte)0, action.Header.SourceSlot);
            Assert.Equal((ushort)101, action.DeltaMilliseconds);
            Assert.Equal((byte)0, action.SourceEcho);
            Assert.Equal(PlayerActionState.Attack, action.State);
            Assert.Equal((byte)0, action.ActionCode);
            Assert.Equal((short)350, action.PositionX);
            Assert.Equal((short)0, action.PositionY);
            Assert.Equal((short)2450, action.PositionZ);
            Assert.Equal((byte)0xa5, action.AngleByte);
            Assert.Equal((short)0, action.ViewRotationX);
            Assert.Equal((short)0, action.ViewRotationY);
            Assert.Equal((short)0, action.ViewRotationZ);
        }

        [Fact]
        public void Move_ParsesViewRotationAtFinalOffsets()
        {
            byte[] packet = System.Convert.FromHexString(
                "0A032700000000650020005E01000092090000A50100FEFF3412");

            Assert.True(GameplayActionDatagram.TryParseMove(packet, out var action));
            Assert.Equal((short)1, action.ViewRotationX);
            Assert.Equal((short)-2, action.ViewRotationY);
            Assert.Equal((short)0x1234, action.ViewRotationZ);
        }

        [Theory]
        [InlineData("0F03280000000A0A080001000003", 0x030f)]
        [InlineData("1103630000000A0A0100", 0x0311)]
        [InlineData("1103630000000A0A01000000", 0x0311)]
        public void CompanionStreams_AcceptExactCapturedShapes(string hex, ushort type)
        {
            byte[] packet = System.Convert.FromHexString(hex);

            Assert.True(GameplayActionDatagram.TryParseHeader(packet, out var header));
            Assert.Equal(type, header.Type);
        }

        [Fact]
        public void Sync_ParsesSixBytePlayerSnapshot()
        {
            byte[] packet = System.Convert.FromHexString("0F03280000000A0A080001000003");

            Assert.True(GameplayActionDatagram.TryParseSync(packet, out var action));
            Assert.Equal((byte)0x0a, action.SourceEcho);
            Assert.Equal((byte)0x08, action.LifeState);
            Assert.Equal((byte)0, action.PlayerValueA);
            Assert.Equal((byte)1, action.AnimatorValue);
            Assert.Equal((byte)0, action.PlayerValueB);
            Assert.Equal((byte)0, action.ControlMode);
            Assert.Equal((byte)3, action.ControlDetail);
        }

        [Theory]
        [InlineData("1103630000000A0A0007", PlayerAnimationKind.Normal, false)]
        [InlineData("1103630000000A0A0109", PlayerAnimationKind.Attack, false)]
        [InlineData("1103630000000A0A01090000", PlayerAnimationKind.Attack, true)]
        [InlineData("1103630000000A0A02010203", PlayerAnimationKind.Damage, true)]
        public void Animation_ParsesKindSpecificUnion(
            string hex,
            PlayerAnimationKind kind,
            bool extended)
        {
            Assert.True(GameplayActionDatagram.TryParseAnimation(
                System.Convert.FromHexString(hex), out var action));
            Assert.Equal(kind, action.Kind);
            Assert.Equal(extended, action.HasExtendedPayload);
            if (kind == PlayerAnimationKind.Normal) Assert.Equal((byte)7, action.Argument0);
            if (kind == PlayerAnimationKind.Attack) Assert.Equal((byte)9, action.Argument0);
            if (kind != PlayerAnimationKind.Damage) return;
            Assert.Equal((byte)1, action.Argument0);
            Assert.Equal((byte)2, action.Argument1);
            Assert.Equal((byte)3, action.Argument2);
        }

        [Theory]
        [InlineData("1103630000000A0A0201")]
        [InlineData("1103630000000A0A0301")]
        public void Animation_RejectsTruncatedDamageOrUnknownKind(string hex)
        {
            Assert.False(GameplayActionDatagram.TryParseAnimation(
                System.Convert.FromHexString(hex), out _));
        }

        [Theory]
        [InlineData(
            "0A032700000000650020005E01000092090000A5000000000000",
            "0A03650020005E01000092090000A5000000000000")]
        [InlineData("1103630000000A0A02010203", "11030A02010203")]
        public void BuildTunnelPayload_RemovesPeerTransportHeader(string datagramHex, string expectedHex)
        {
            byte[] payload = GameplayActionDatagram.BuildTunnelPayload(
                System.Convert.FromHexString(datagramHex));

            Assert.Equal(System.Convert.FromHexString(expectedHex), payload);
        }

        [Fact]
        public void TunnelPayload_CarriesServerCombatEvents()
        {
            byte[] datagram = ServerCombatDatagrams.Damage(new ServerDamageEvent(
                AttackerSeat: 3,
                VictimSeat: 10,
                Sequence: 7,
                Damage: 52,
                Direction: new BotVector(0f, 0f, 1f)));

            byte[] payload = GameplayActionDatagram.BuildTunnelPayload(datagram);

            // O túnel transporta tipo + corpo a partir do offset 7; a sequência é reinserida
            // no destino. Sem isso o evento de dano estourava e o combate ficava invisível.
            Assert.Equal(datagram.Length - 5, payload.Length);
            Assert.Equal(datagram[7..], payload[2..]);

            // O 0x8000 é do transporte UDP confiável; no túnel o cliente original usa o tipo
            // lógico (0x030C). Mandar 0x830C aqui entrega um evento que ele não consome.
            Assert.Equal(
                (ushort)(System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt16LittleEndian(datagram) & 0x7FFF),
                System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload));
        }

        [Fact]
        public void TunnelPayload_RejectsCorruptedDatagram()
        {
            Assert.Throws<System.ArgumentException>(
                () => GameplayActionDatagram.BuildTunnelPayload(
                    System.Convert.FromHexString("0C83000000000000")));
        }

        [Theory]
        [InlineData("0A030000000000")]
        [InlineData("0F030000000000000000000000")]
        [InlineData("110300000000000000")]
        [InlineData("1903000000000000")]
        public void InvalidOrUnsupportedShape_IsRejected(string hex)
        {
            Assert.False(GameplayActionDatagram.TryParseHeader(
                System.Convert.FromHexString(hex), out _));
        }
    }
}
