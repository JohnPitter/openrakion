using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests.E2E
{
    [Collection("E2E")]
    public sealed class EnchantPersistenceE2ETests
    {
        private const int CharacterId = 9001;

        [Fact]
        public async Task PreviewCommitReplayAndReconnect_PersistExactlyOnceOnRealWire()
        {
            await using var fixture = await WorldServerFixture.CreateAsync();
            if (!fixture.Available) return;
            await using var sandbox = await EnchantE2ESandbox.CreateAsync(
                fixture.DbConnectionString);

            EnchantRun run;
            await using (var client = await ConnectAsync(fixture, "enchant-commit"))
            {
                ClientSession session = Login(client, fixture.Server!);
                AssertFixtureLoaded(session, sandbox);
                client.CreateGolemRoom("e2e-enchant");
                JourneyHelper.WaitUntil(
                    () => session.FieldId >= 0 && fixture.Server!.GetField(session.FieldId) != null,
                    "sala de enchant não foi criada");

                client.PreviewEnchant(
                    sandbox.Target.Slot, sandbox.Catalyst.Slot, sandbox.Material.Slot);
                byte[] preview = client.WaitForNext(IsEnchantPreview, JourneyHelper.Timeout);
                AssertPreview(preview, sandbox);

                client.CommitEnchant(sandbox.Target.Slot, sandbox.Catalyst.Slot,
                    clientResult: 5, sandbox.Material.Slot);
                byte[] result = client.WaitForNext(IsEnchantResult, JourneyHelper.Timeout);
                run = AssertResultAndSession(result, session, sandbox);

                client.CommitEnchant(sandbox.Target.Slot, sandbox.Catalyst.Slot,
                    clientResult: 5, sandbox.Material.Slot);
                byte[] replay = client.WaitForNext(IsEnchantResult, JourneyHelper.Timeout);
                Assert.Equal(result, replay);
            }

            JourneyHelper.WaitUntil(
                () => !fixture.Server!.Sessions.Any(value => value.UserId == "test2"),
                "sessão de enchant não encerrou");
            await AssertReconnectAsync(fixture, sandbox, run.NewLevel);
            await AssertDatabaseAsync(sandbox, run);
        }

        private static void AssertFixtureLoaded(
            ClientSession session, EnchantE2ESandbox sandbox)
        {
            AssertBoxItem(session, sandbox.Target);
            AssertBoxItem(session, sandbox.Catalyst);
            AssertBoxItem(session, sandbox.Material);
            Assert.Equal(sandbox.Target.Level, session.BoxLevel[sandbox.Target.Slot]);
        }

        private static void AssertPreview(byte[] frame, EnchantE2ESandbox sandbox)
        {
            Assert.True(frame.Length >= 40);
            Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(frame));
            Assert.Equal((ushort)0x28, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2)));
            Assert.Equal((uint)sandbox.GameInfoId,
                BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4)));
            AssertDescriptor(frame, 8, sandbox.Target);
            AssertDescriptor(frame, 13, sandbox.Catalyst);
            Assert.Equal((byte)1, frame[18]);
            AssertDescriptor(frame, 19, sandbox.Material);
            Assert.Equal(new byte[10], frame.AsSpan(24, 10).ToArray());
            Assert.Equal((byte)0, frame[34]);
            Assert.Equal((uint)CharacterId,
                BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(35)));
            Assert.Equal((byte)0, frame[39]);
            Assert.All(frame.AsSpan(40).ToArray(), value => Assert.Equal((byte)0, value));
        }

        private static EnchantRun AssertResultAndSession(
            byte[] frame, ClientSession session, EnchantE2ESandbox sandbox)
        {
            byte result = frame[2];
            Assert.Contains(result, new byte[] { 0, 1, 2, 3, 4, 6 });
            Assert.NotEqual((byte)5, result);
            Assert.Equal(sandbox.Target.Slot, frame[3]);
            Assert.Equal(sandbox.Catalyst.Slot, frame[4]);
            Assert.Equal((byte)1, frame[5]);
            Assert.Equal(sandbox.Material.Slot, frame[6]);
            int newLevel = Math.Clamp(
                sandbox.Target.Level + EnchantRules.Delta(result), 0, 15);
            JourneyHelper.WaitUntil(
                () => session.BoxLevel[sandbox.Target.Slot] == newLevel &&
                      session.BoxItems[sandbox.Catalyst.Slot] == 0 &&
                      session.BoxItems[sandbox.Material.Slot] == 0,
                "sessão não refletiu o commit de enchant");
            Assert.Equal(sandbox.Target.ItemId, session.BoxItems[sandbox.Target.Slot]);
            Assert.Equal(sandbox.Target.RowId, session.BoxRowId[sandbox.Target.Slot]);
            return new EnchantRun(result, newLevel);
        }

        private static async Task AssertReconnectAsync(
            WorldServerFixture fixture, EnchantE2ESandbox sandbox, int newLevel)
        {
            await using var client = await ConnectAsync(fixture, "enchant-reconnect");
            ClientSession session = Login(client, fixture.Server!);
            AssertBoxItem(session, sandbox.Target);
            Assert.Equal(newLevel, session.BoxLevel[sandbox.Target.Slot]);
            Assert.Equal(0, session.BoxItems[sandbox.Catalyst.Slot]);
            Assert.Equal(0, session.BoxItems[sandbox.Material.Slot]);
        }

        private static async Task AssertDatabaseAsync(
            EnchantE2ESandbox sandbox, EnchantRun run)
        {
            EnchantDatabaseState state = await sandbox.ReadStateAsync();
            Assert.Equal(run.NewLevel, state.TargetLevel);
            Assert.Equal(0, state.InputCount);
            EnchantLedgerRow ledger = await sandbox.ReadLedgerAsync();
            Assert.NotEmpty(ledger.OperationId);
            Assert.Equal(sandbox.Target.RowId, ledger.TargetRowId);
            Assert.Equal(sandbox.Target.ItemId, ledger.TargetItemId);
            Assert.Equal(sandbox.Target.Level, ledger.PreviousLevel);
            Assert.Equal(run.NewLevel, ledger.CurrentLevel);
            Assert.Equal(sandbox.Catalyst.RowId, ledger.CatalystRowId);
            Assert.Equal(sandbox.Material.RowId.ToString(), ledger.MaterialRowIds);
            Assert.Equal(run.Result, ledger.Result);
            Assert.InRange(ledger.Chance, 0.0, 1.0);
            Assert.True(ledger.ConfigVersion >= 2);
        }

        private static void AssertBoxItem(ClientSession session, EnchantFixtureItem item)
        {
            Assert.Equal(item.ItemId, session.BoxItems[item.Slot]);
            Assert.Equal(item.RowId, session.BoxRowId[item.Slot]);
        }

        private static void AssertDescriptor(
            byte[] frame, int offset, EnchantFixtureItem item)
        {
            Assert.Equal(item.Slot, frame[offset]);
            Assert.Equal(item.Serial,
                BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(offset + 1)));
        }

        private static ClientSession Login(HeadlessWorldClient client, WorldServer server)
        {
            client.Login("test2", "test2");
            client.WaitForFirstByte(0x0c, JourneyHelper.Timeout);
            client.SelectCharacter(CharacterId);
            client.WaitForNext(IsCharacterSelectAck, JourneyHelper.Timeout);
            client.WaitForNext(IsChannelSnapshot, JourneyHelper.Timeout);
            return JourneyHelper.WaitForSession(server, "test2",
                value => value.ActiveCharId == CharacterId && value.Status == UserStatus.FieldLobby);
        }

        private static async Task<HeadlessWorldClient> ConnectAsync(
            WorldServerFixture fixture, string name) => await HeadlessWorldClient.ConnectAsync(
                WorldServerFixture.Host, fixture.TcpPort, name);

        private static bool IsCharacterSelectAck(byte[] frame) =>
            frame.Length >= 3 && frame[0] == 0x14 && frame[1] == 0 && frame[2] == 0;
        private static bool IsChannelSnapshot(byte[] frame) =>
            frame.Length >= 2 && frame[0] == 0x1e && frame[1] == 0;
        private static bool IsEnchantPreview(byte[] frame) =>
            frame.Length >= 40 && frame[0] == 0 && frame[1] == 0 &&
            frame[2] == 0x28 && frame[3] == 0;
        private static bool IsEnchantResult(byte[] frame) =>
            frame.Length >= 7 && frame[0] == 0x74 && frame[1] == 0;

        private readonly record struct EnchantRun(byte Result, int NewLevel);
    }
}
