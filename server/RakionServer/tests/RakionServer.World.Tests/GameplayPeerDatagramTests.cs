using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class GameplayPeerDatagramTests
    {
        [Theory]
        [InlineData("040308000000FF00D81FC000", 0x0304, GameplayPeerDatagramKind.ApplicationReliablePush)]
        [InlineData("04030A0000000A005F2EC0000A", 0x0304, GameplayPeerDatagramKind.ApplicationReliablePush)]
        [InlineData("0503050000000A0AAE11DD0000", 0x0305, GameplayPeerDatagramKind.ApplicationReliableAck)]
        [InlineData("19030E0000000000", 0x0319, GameplayPeerDatagramKind.AddressUpdate)]
        [InlineData("0040800000000A82000000", 0x4000, GameplayPeerDatagramKind.TransportAck)]
        [InlineData("0783010000000000010700000000000000000000000000000000000000000000000000", 0x8307, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("0883010000000404010700000000000000000000000000000000000000000000000000", 0x8308, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("0983010000000000020700000000000000000000000000000000000000000000000000", 0x8309, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("0B8301000000000F000200010000000000000000", 0x830b, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("0C839A00000000000100002A0091010400000001000000", 0x830c, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("10830100000000000301", 0x8310, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("128301000000000201010200", 0x8312, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("0C839D00000000000100000C0091010C000000000000000000C2420000C242", 0x830c, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("1583D70000000A03", 0x8315, GameplayPeerDatagramKind.ReliableMessage)]
        [InlineData("1383750400000A0A00", 0x8313, GameplayPeerDatagramKind.BadPingStatus)]
        public void CapturedShapes_AreClassified(string hex, ushort type, GameplayPeerDatagramKind kind)
        {
            Assert.True(GameplayPeerDatagramCodec.TryParse(
                System.Convert.FromHexString(hex), out var packet));
            Assert.Equal(type, packet.Type);
            Assert.Equal(kind, packet.Kind);
        }

        [Theory]
        [InlineData("040308000000FF00D81F")]
        [InlineData("0503050000000A0AAE11DD000000")]
        [InlineData("19030E000000000000")]
        [InlineData("0040800000000A820000")]
        [InlineData("078301000000000001070000")]
        [InlineData("0B8301000000000F000100010000000000000000")]
        [InlineData("12830100000000020101")]
        [InlineData("0D030500000000")]
        [InlineData("1383750400000A0A02")]
        public void InvalidOrConsumedShapes_AreRejected(string hex)
        {
            Assert.False(GameplayPeerDatagramCodec.TryParse(
                System.Convert.FromHexString(hex), out _));
        }

        [Fact]
        public void MapItemSnapshotRejectsMoreThanSixtyFiveEntries()
        {
            byte[] packet = new byte[8 + 66 * 2];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet, 0x8312);
            packet[7] = 66;

            Assert.False(GameplayPeerDatagramCodec.TryParse(packet, out _));
        }

        [Theory]
        [InlineData(
            "07830100000002030134120000803F0000004000004040000080400000A0400000C040AABB",
            GameplayNpcCreationKind.General)]
        [InlineData(
            "0883020000000401017856000080BF0000003F00001040000000410000104100002041CC",
            GameplayNpcCreationKind.MasterGolem)]
        [InlineData(
            "098303000000050702BC9A000000000000000000000000000000000000000000000000",
            GameplayNpcCreationKind.Map)]
        public void NpcCreation_DecodesFixedEnvelopeAndKeepsInitBlobBoundary(
            string hex, GameplayNpcCreationKind expectedKind)
        {
            byte[] packet = System.Convert.FromHexString(hex);

            Assert.True(GameplayPeerDatagramCodec.TryParseNpcCreation(packet, out var creation));
            Assert.Equal(expectedKind, creation.Kind);
            Assert.Equal(packet.Length - 35, creation.InitBlobLength);
            Assert.Equal(35, creation.InitBlobOffset);
        }

        [Fact]
        public void GeneralNpcCreation_DecodesOwnerEntityAndPlacement()
        {
            byte[] packet = System.Convert.FromHexString(
                "07830100000002030134120000803F0000004000004040000080400000A0400000C040AABB");

            Assert.True(GameplayPeerDatagramCodec.TryParseNpcCreation(packet, out var creation));
            Assert.Equal(1u, creation.Sequence);
            Assert.Equal((byte)2, creation.SourceSeat);
            Assert.Equal((byte)3, creation.OwnerOrHostSeat);
            Assert.Equal((byte)1, creation.NpcOrTeamIndex);
            Assert.Equal((ushort)0x1234, creation.EntityField);
            Assert.Equal(new GameplayEntityPlacement(1f, 2f, 3f, 4f, 5f, 6f), creation.Placement);
        }

        [Fact]
        public void EntityState_DecodesCompactPlacement()
        {
            byte[] packet = System.Convert.FromHexString(
                "0B8311000000023412030405FFFF0200FDFF0400");

            Assert.True(GameplayPeerDatagramCodec.TryParseEntityState(packet, out var state));
            Assert.Equal(0x11u, state.Sequence);
            Assert.Equal((byte)2, state.SourceSeat);
            Assert.Equal((ushort)0x1234, state.TimingOrState);
            Assert.Equal((byte)3, state.EntityKind);
            Assert.Equal((byte)4, state.Group);
            Assert.Equal((byte)5, state.Index);
            Assert.Equal((short)-1, state.X);
            Assert.Equal((short)2, state.Y);
            Assert.Equal((short)-3, state.Z);
            Assert.Equal((short)4, state.Heading);
        }

        [Fact]
        public void MapItemSnapshot_DecodesAllPairs()
        {
            byte[] packet = System.Convert.FromHexString(
                "1283220000000203010203040506");

            Assert.True(GameplayPeerDatagramCodec.TryParseMapItemSnapshot(packet, out var snapshot));
            Assert.Equal(0x22u, snapshot.Sequence);
            Assert.Equal((byte)2, snapshot.SourceSeat);
            Assert.Equal(new GameplayMapItemState(1, 2), snapshot.Items[0]);
            Assert.Equal(new GameplayMapItemState(3, 4), snapshot.Items[1]);
            Assert.Equal(new GameplayMapItemState(5, 6), snapshot.Items[2]);
        }

        [Fact]
        public void MapNpcCreateRequest_RequiresMapEntityKindAndDecodesRoute()
        {
            byte[] packet = System.Convert.FromHexString("10830500000002070309");

            Assert.True(GameplayPeerDatagramCodec.TryParseMapNpcCreateRequest(
                packet, out var request));
            Assert.Equal(5u, request.Sequence);
            Assert.Equal((byte)2, request.SourceSeat);
            Assert.Equal((byte)7, request.TargetSeat);
            Assert.Equal((byte)3, request.EntityKind);
            Assert.Equal((byte)9, request.MapIndex);
        }

        [Theory]
        [InlineData("10830100000000000003")]
        [InlineData("108301000000000003")]
        [InlineData("1083010000000000030100")]
        public void MapNpcCreateRequest_RejectsWrongOrderAndLength(string hex) =>
            Assert.False(GameplayPeerDatagramCodec.TryParseMapNpcCreateRequest(
                System.Convert.FromHexString(hex), out _));

        [Theory]
        [InlineData("07830100000000000107000000000000000000000000000000000000000000000000")]
        [InlineData("0B8311000000023412010405FFFF0200FDFF0400")]
        [InlineData("1283220000000202010203")]
        public void TypedNpcParsers_RejectTruncatedOrInvalidBodies(string hex)
        {
            byte[] packet = System.Convert.FromHexString(hex);

            Assert.False(GameplayPeerDatagramCodec.TryParseNpcCreation(packet, out _));
            Assert.False(GameplayPeerDatagramCodec.TryParseEntityState(packet, out _));
            Assert.False(GameplayPeerDatagramCodec.TryParseMapItemSnapshot(packet, out _));
        }

        [Fact]
        public void BadPingPayload_ExposesTransportSourcePlayerAndFlag()
        {
            byte[] packet = System.Convert.FromHexString("1383750400000A0A01");

            Assert.True(GameplayPeerDatagramCodec.TryParseBadPing(packet, out var status));
            Assert.Equal(0x475u, status.Sequence);
            Assert.Equal((byte)10, status.SourceSeat);
            Assert.Equal((byte)10, status.PlayerSeat);
            Assert.True(status.IsBad);
        }

        [Fact]
        public void EntityEvent_ExposesReliableRouteIdAndExactPayloadLength()
        {
            byte[] packet = System.Convert.FromHexString(
                "0C839D00000000000100000C0091010C000000000000000000C2420000C242");

            Assert.True(GameplayPeerDatagramCodec.TryParseEntityEvent(packet, out var entityEvent));
            Assert.Equal(0x9du, entityEvent.Sequence);
            Assert.Equal((byte)1, entityEvent.Route);
            Assert.Equal(GameplayPeerDatagramCodec.PlayerRemainHpEventId, entityEvent.EventId);
            Assert.Equal(12, entityEvent.PayloadLength);
        }

        [Fact]
        public void PlayerVitals_DecodesCapturedHpAndApFloats()
        {
            byte[] packet = System.Convert.FromHexString(
                "0C839D00000000000100000C0091010C000000000000000000C2420000C242");

            Assert.True(GameplayPeerDatagramCodec.TryParsePlayerVitals(packet, out var vitals));
            Assert.Equal(0u, vitals.PlayerId);
            Assert.Equal(97f, vitals.Hp);
            Assert.Equal(97f, vitals.Ap);
        }

        [Fact]
        public void PlayerDamage_DecodesExactClientClassPayload()
        {
            byte[] packet = BuildEntityEvent(
                GameplayPeerDatagramCodec.PlayerDamageEventId,
                "070000000B0434120000C03F0000204000004040000080400000A0400000C0400000E04000000041");

            Assert.True(GameplayPeerDatagramCodec.TryParsePlayerDamage(packet, out var damage));
            Assert.Equal(7u, damage.PlayerId);
            Assert.Equal((byte)11, damage.DamageType);
            Assert.Equal((byte)4, damage.DamageMotionType);
            Assert.Equal((ushort)0x1234, damage.Reserved);
            Assert.Equal(1.5f, damage.FirstDamageValue);
            Assert.Equal(2.5f, damage.SecondDamageValue);
            Assert.Equal(new GameplayVector3(3f, 4f, 5f), damage.FirstVector);
            Assert.Equal(new GameplayVector3(6f, 7f, 8f), damage.SecondVector);
        }

        [Fact]
        public void PlayerDeath_DecodesDamageVectorCopiedByClient()
        {
            byte[] packet = BuildEntityEvent(
                GameplayPeerDatagramCodec.PlayerDeathEventId,
                "000080BF0000003F00001040");

            Assert.True(GameplayPeerDatagramCodec.TryParsePlayerDeath(packet, out var death));
            Assert.Equal(new GameplayVector3(-1f, 0.5f, 2.25f), death.DeathVector);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(3, 0)]
        [InlineData(4, 0)]
        [InlineData(5, 0)]
        [InlineData(6, 0)]
        [InlineData(7, 2)]
        public void UsePotion_DecodesAllNativeKinds(int potionKind, int argument)
        {
            byte[] packet = BuildEntityEvent(
                GameplayPeerDatagramCodec.UsePotionEventId,
                $"{ToLittleEndianHex(potionKind)}{ToLittleEndianHex(argument)}");

            Assert.True(GameplayPeerDatagramCodec.TryParseUsePotion(packet, out var usePotion));
            Assert.Equal(potionKind, usePotion.PotionKind);
            Assert.Equal(argument, usePotion.Argument);
            Assert.True(GameplayPeerDatagramCodec.TryParse(packet, out _));
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(8, 0)]
        public void UsePotion_RejectsKindsOutsideNativeDispatcher(int potionKind, int argument)
        {
            byte[] packet = BuildEntityEvent(
                GameplayPeerDatagramCodec.UsePotionEventId,
                $"{ToLittleEndianHex(potionKind)}{ToLittleEndianHex(argument)}");

            Assert.False(GameplayPeerDatagramCodec.TryParseUsePotion(packet, out _));
            Assert.False(GameplayPeerDatagramCodec.TryParse(packet, out _));
        }

        [Fact]
        public void UsePotion_RejectsPayloadWithWrongLength()
        {
            byte[] packet = BuildEntityEvent(
                GameplayPeerDatagramCodec.UsePotionEventId,
                "07000000");

            Assert.False(GameplayPeerDatagramCodec.TryParseUsePotion(packet, out _));
            Assert.False(GameplayPeerDatagramCodec.TryParse(packet, out _));
        }

        [Fact]
        public void WeaponEvents_DecodeExactClientClassPayloads()
        {
            byte[] setWeapon = BuildEntityEvent(
                GameplayPeerDatagramCodec.SetWeaponEventId,
                "02000000FDFFFFFF");
            byte[] shootWeapon = BuildEntityEvent(
                GameplayPeerDatagramCodec.ShootWeaponEventId,
                "0000803F0000004000004040000080400000A0400000C04002010203");
            byte[] shootShuriken = BuildEntityEvent(
                GameplayPeerDatagramCodec.ShootShurikenEventId,
                "00001041000000410000E0400000C0400000A040000080400901BBAA");

            Assert.True(GameplayPeerDatagramCodec.TryParseSetWeapon(setWeapon, out var weapon));
            Assert.Equal(2, weapon.WeaponSelector);
            Assert.Equal(-3, weapon.Argument);

            Assert.True(GameplayPeerDatagramCodec.TryParseShootWeapon(
                shootWeapon, out var shot));
            Assert.Equal(new GameplayVector3(1f, 2f, 3f), shot.FirstVector);
            Assert.Equal(new GameplayVector3(4f, 5f, 6f), shot.SecondVector);
            Assert.Equal((byte)2, shot.ShootType);
            Assert.Equal((byte)3, shot.Reserved2);

            Assert.True(GameplayPeerDatagramCodec.TryParseShootShuriken(
                shootShuriken, out var shuriken));
            Assert.Equal(new GameplayVector3(9f, 8f, 7f), shuriken.FirstVector);
            Assert.Equal(new GameplayVector3(6f, 5f, 4f), shuriken.SecondVector);
            Assert.Equal((byte)9, shuriken.ProjectileCount);
            Assert.Equal((byte)1, shuriken.Variant);
            Assert.Equal((ushort)0xaabb, shuriken.Reserved);
        }

        [Fact]
        public void HoldEvents_DecodeExactClientClassPayloads()
        {
            byte[] request = BuildEntityEvent(
                GameplayPeerDatagramCodec.RequestHoldAttackEventId,
                "443322110A0B34120000484288776655");
            byte[] hold = BuildEntityEvent(
                GameplayPeerDatagramCodec.HoldAttackEventId,
                "04030201050608070D0C0B0A090AD0C0");

            Assert.True(GameplayPeerDatagramCodec.TryParseRequestHoldAttack(
                request, out var requestHold));
            Assert.Equal(0x11223344u, requestHold.EntityWord);
            Assert.Equal((byte)10, requestHold.EntityIndex);
            Assert.Equal((byte)11, requestHold.EntitySubIndex);
            Assert.Equal(50f, requestHold.MaximumDistance);
            Assert.Equal(0x55667788u, requestHold.Argument);

            Assert.True(GameplayPeerDatagramCodec.TryParseHoldAttack(hold, out var holdAttack));
            Assert.Equal(0x01020304u, holdAttack.EntityWord);
            Assert.Equal(0x0a0b0c0du, holdAttack.Argument);
            Assert.Equal((byte)9, holdAttack.ActorIndex);
            Assert.Equal((byte)10, holdAttack.ActorSubIndex);
            Assert.Equal((ushort)0xc0d0, holdAttack.Reserved1);
        }

        [Fact]
        public void RespawnEvent_AcceptsEmptyPayloadFromClientBuilder()
        {
            byte[] packet = System.Convert.FromHexString(
                "0C830100000000000100001700910100000000");

            Assert.True(GameplayPeerDatagramCodec.TryParseEntityEvent(packet, out var entityEvent));
            Assert.Equal(GameplayPeerDatagramCodec.RespawnEventId, entityEvent.EventId);
            Assert.Equal(0, entityEvent.PayloadLength);
        }

        [Fact]
        public void EntityEvent_RejectsLengthMismatch()
        {
            Assert.False(GameplayPeerDatagramCodec.TryParse(
                System.Convert.FromHexString(
                    "0C83010000000000010000160091010C00000000000000"), out _));
        }

        [Theory]
        [InlineData("1583D70000000A03", 10, true)]
        [InlineData("1583D70000000A03", 0, false)]
        [InlineData("040308000000FF00D81FC000", 10, true)]
        public void SourceSeatMustMatchAuthenticatedSeatExceptApplicationSentinel(
            string hex, byte authenticatedSeat, bool expected)
        {
            GameplayPeerDatagramCodec.TryParse(
                System.Convert.FromHexString(hex), out GameplayPeerDatagram datagram);

            Assert.Equal(expected,
                GameplayPeerDatagramCodec.SourceMatches(datagram, authenticatedSeat));
        }

        [Fact]
        public void EntityEventExposesSameTransportAndLogicalSenderInCapture()
        {
            byte[] packet = System.Convert.FromHexString(
                "0C839D00000000000100000C0091010C000000000000000000C2420000C242");

            GameplayPeerDatagramCodec.TryParseEntityEvent(packet, out var entityEvent);

            Assert.Equal((byte)0, entityEvent.TransportSourceSeat);
            Assert.Equal((byte)0, entityEvent.SenderSeat);
        }

        [Theory]
        [InlineData("10830500000002020309", 2, true)]
        [InlineData("10830500000002070309", 2, false)]
        [InlineData("0C830100000002020100001700910100000000", 2, true)]
        [InlineData("0C830100000002070100001700910100000000", 2, false)]
        public void NestedSenderMustMatchAuthenticatedTransportSource(
            string hex, byte authenticatedSeat, bool expected)
        {
            byte[] packet = System.Convert.FromHexString(hex);
            Assert.True(GameplayPeerDatagramCodec.TryParse(packet, out var datagram));

            Assert.Equal(expected, GameplayPeerDatagramCodec.SourceMatches(
                datagram, authenticatedSeat, packet));
        }

        private static byte[] BuildEntityEvent(uint eventId, string payloadHex)
        {
            byte[] payload = System.Convert.FromHexString(payloadHex);
            byte[] packet = new byte[19 + payload.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(packet, 0x830c);
            packet[7] = 0;
            packet[8] = 1;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                new System.Span<byte>(packet, 11, 4), eventId);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                new System.Span<byte>(packet, 15, 4), (uint)payload.Length);
            payload.CopyTo(packet, 19);
            return packet;
        }

        private static string ToLittleEndianHex(int value)
        {
            byte[] bytes = new byte[sizeof(int)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            return System.Convert.ToHexString(bytes);
        }
    }
}
