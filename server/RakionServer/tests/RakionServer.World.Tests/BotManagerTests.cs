using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using RakionServer.World;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>Roster/lifecycle do bot: adição pelo host, time oposto, gates e limpeza efêmera.</summary>
    public sealed class BotManagerTests
    {
        private static string LifecycleBasePath => Path.Combine(
            Path.GetTempPath(), "openrakion-tests", $"bot-lifecycle-{Environment.ProcessId}.txt");

        private static BotManager NewManager()
        {
            return new BotManager(LifecycleBasePath);
        }

        private static ClientSession NewSession(ushort slot)
        {
            var server = new WorldServer(new WorldConfig(), new Database.WorldDatabase(new WorldConfig().Db));
            return new ClientSession(
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), slot, server);
        }

        private static (Field field, ClientSession host) GolemRoomWithHost()
        {
            var field = new Field(1) { Mode = (byte)GameMode.Golem, MaxPlayers = 8 };
            ClientSession host = NewSession(1);
            host.CharLevel = 20;
            host.CharClass = 2;
            field.Slots[0].Session = host;
            field.Slots[0].State = 3;
            field.MasterSlot = 0;
            field.Master = host;
            field.Add(host);
            return (field, host);
        }

        [Fact]
        public void AddBot_ByHost_LandsInOppositeTeamSeat()
        {
            var (field, host) = GolemRoomWithHost();
            BotManager mgr = NewManager();

            var result = mgr.AddBotToField(field, host, BotDifficulty.Normal);

            Assert.True(result.Ok, result.Message);
            Assert.InRange(result.Seat, 10, 19);                 // host time 0 -> bot time 1
            Assert.Equal((byte)1, field.Slots[result.Seat].Team);
            Assert.NotNull(field.Slots[result.Seat].Bot);
            Assert.Equal((byte)2, field.Slots[result.Seat].State); // ready
            Assert.True(field.Slots[result.Seat].LobbyReady);
            Assert.Equal(1, field.BotCount);
        }

        [Fact]
        public void AddBot_ByNonHost_IsRejected()
        {
            var (field, _) = GolemRoomWithHost();
            ClientSession intruder = NewSession(2);
            field.Slots[1].Session = intruder;
            field.Slots[1].State = 3;
            field.Add(intruder);
            BotManager mgr = NewManager();

            var result = mgr.AddBotToField(field, intruder, BotDifficulty.Normal);

            Assert.False(result.Ok);
            Assert.Equal(0, field.BotCount);
        }

        [Fact]
        public void AddBot_DuringMatch_IsRejected()
        {
            var (field, host) = GolemRoomWithHost();
            field.Phase = MatchPhase.Playing;
            BotManager mgr = NewManager();

            Assert.False(mgr.AddBotToField(field, host, BotDifficulty.Normal).Ok);
        }

        [Fact]
        public void AddBot_InSoloStage_IsRejected()
        {
            var (field, host) = GolemRoomWithHost();
            field.Mode = 0; // stage PvE
            BotManager mgr = NewManager();

            Assert.False(mgr.AddBotToField(field, host, BotDifficulty.Normal).Ok);
        }

        [Fact]
        public void RemoveAllBots_ClearsSeatsAndKeepsHumans()
        {
            var (field, host) = GolemRoomWithHost();
            BotManager mgr = NewManager();
            mgr.AddBotToField(field, host, BotDifficulty.Hard);
            mgr.AddBotToField(field, host, BotDifficulty.Easy);
            Assert.Equal(2, field.BotCount);

            int removed = mgr.RemoveAllBots(field);

            Assert.Equal(2, removed);
            Assert.Equal(0, field.BotCount);
            Assert.Same(host, field.Slots[0].Session);   // humano preservado
            Assert.True(field.HasHumans);
        }

        [Fact]
        public void AddBot_FillsTeamThenRejectsWhenFull()
        {
            var (field, host) = GolemRoomWithHost();
            BotManager mgr = NewManager();
            int ok = 0;
            for (int i = 0; i < 12; i++)
                if (mgr.AddBotToField(field, host, BotDifficulty.Normal).Ok) ok++;

            Assert.Equal(10, ok);              // time oposto tem 10 assentos (10..19)
            Assert.Equal(10, field.BotCount);
        }

        [Fact]
        public void LifecycleSnapshot_IsScopedToAuthenticatedClientPortAndCarriesHit()
        {
            var (field, host) = GolemRoomWithHost();
            const int clientPort = 32123;
            var endpoint = new IPEndPoint(IPAddress.Loopback, clientPort);
            host.NotifyUdpReady(endpoint, endpoint, 1);
            BotManager manager = NewManager();
            string path = BotManager.ClientLifecyclePath(LifecycleBasePath, clientPort);

            try
            {
                BotManager.AddBotResult added = manager.AddBotToField(
                    field, host, BotDifficulty.Normal);
                Assert.True(added.Ok, added.Message);
                BotPlayer bot = added.Bot!;
                bot.TakeDamage(34, attackerSeat: 0, attackerHitSequence: 1);
                manager.PublishBotLifecycles(field);

                string[] values = File.ReadAllText(path).Trim().Split(' ');
                Assert.Equal(7, values.Length);
                Assert.Equal(added.Seat.ToString(), values[0]);
                Assert.Equal(field.Id.ToString(), values[1]);
                Assert.Equal("1", values[4]);
                Assert.Equal("0", values[5]);
                Assert.Equal("1", values[6]);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
