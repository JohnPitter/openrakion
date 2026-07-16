using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class CanonicalOpcodeNamesTests
    {
        [Theory]
        [InlineData(0x01)]
        [InlineData(0x78)]
        [InlineData(0x79)]
        public void OriginalJumpTableOpcodeIsAccepted(ushort opcode)
        {
            Assert.True(WorldHandlers.AcceptsOpcode(opcode));
        }

        [Theory]
        [InlineData(0x06)]
        [InlineData(0x1D)]
        [InlineData(0x28)]
        [InlineData(0x7A)]
        public void OriginalJumpTableGapIsRejected(ushort opcode)
        {
            Assert.False(WorldHandlers.AcceptsOpcode(opcode));
        }

        [Theory]
        [InlineData(0x04, "AdminBan")]
        [InlineData(0x0E, "SuccessUdp")]
        [InlineData(0x12, "CharacterCreate")]
        [InlineData(0x13, "CharacterDelete")]
        [InlineData(0x14, "CharacterSelect")]
        [InlineData(0x15, "CharacterChangeBuddyName")]
        [InlineData(0x1A, "CharacterTutorialClear")]
        [InlineData(0x1B, "CharacterStateClear")]
        [InlineData(0x1C, "CharacterChangeCharName")]
        [InlineData(0x1E, "ChannelCharacters")]
        [InlineData(0x22, "ChannelChat")]
        [InlineData(0x29, "ChannelFieldPingRequest")]
        [InlineData(0x2A, "ChannelFieldPingResponse")]
        [InlineData(0x2D, "InventoryLeave")]
        [InlineData(0x36, "FieldList")]
        [InlineData(0x38, "FieldEnter")]
        [InlineData(0x39, "FieldQuickEnter")]
        [InlineData(0x3A, "FieldExit")]
        [InlineData(0x3B, "FieldCreate")]
        [InlineData(0x3D, "FieldWeaponChange")]
        [InlineData(0x3E, "FieldChangeTeam")]
        [InlineData(0x46, "FieldGameExit")]
        [InlineData(0x47, "FieldChat")]
        [InlineData(0x48, "FieldGameRoundStart")]
        [InlineData(0x53, "FieldGameStagePoint")]
        [InlineData(0x56, "FieldGameTunnelingAll")]
        [InlineData(0x57, "FieldGameTunnelingOne")]
        [InlineData(0x59, "FieldPingRequest")]
        [InlineData(0x5A, "FieldPingResponse")]
        [InlineData(0x61, "WorldEchoReply")]
        [InlineData(0x62, "FieldSlotUdp")]
        [InlineData(0x78, "ClanMembersQuery")]
        [InlineData(0x6B, "PresentPeek")]
        [InlineData(0x74, "EnchantReinforce")]
        public void DispatcherUsesClientExportAsCanonicalName(int opcode, string expected)
        {
            Assert.Equal(expected, WorldHandlers.OpName((ushort)opcode));
        }

        [Theory]
        [InlineData(0x66)]
        [InlineData(0x69)]
        public void DormantClientEventExportsRemainOutsideWorldDispatcher(int opcode)
        {
            Assert.Equal($"op0x{opcode:x2}", WorldHandlers.OpName((ushort)opcode));
        }

        [Fact]
        public void FinalDispatcherHasNoStubDelegate()
        {
            for (ushort opcode = 0; opcode <= 0x79; opcode++)
                Assert.False(WorldHandlers.UsesStub(opcode), $"opcode 0x{opcode:X2}");
        }
    }
}
