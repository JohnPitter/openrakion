using System;
using System.Collections.Generic;
using BrokenServer;
using Xunit;

namespace RakionServer.World.Tests
{
    [Collection(BrokerTestCollection.Name)]
    public sealed class BrokerServerListGoldenTests
    {
        [Fact]
        public void OnlineServer_MatchesOriginalLiveProbe()
        {
            var previous = Systems.GSList;
            try
            {
                Systems.GSList = new Dictionary<int, Systems.SRX_Serverinfo>
                {
                    [1] = new()
                    {
                        status = 1,
                        ip = "127.0.0.1",
                        wan = "127.0.0.1",
                        lan_wan = false,
                        port = 41708,
                        usedSala = 0,
                        maxSalas = 2000,
                        usedSlots = 0,
                        maxSlots = 500
                    }
                };

                byte[] packet = Systems.ServerListPacket(cliVersion: 258);

                Assert.Equal(
                    "13000101017F000001A2EC0000D0070000F401",
                    Convert.ToHexString(packet));
            }
            finally
            {
                Systems.GSList = previous;
            }
        }

        [Fact]
        public void MixedList_EmitsOnlyCompleteOnlineRecords()
        {
            var previous = Systems.GSList;
            try
            {
                Systems.GSList = new Dictionary<int, Systems.SRX_Serverinfo>
                {
                    [1] = Server(status: 1),
                    [2] = Server(status: 0)
                };

                byte[] packet = Systems.ServerListPacket(cliVersion: 258);

                Assert.Equal(
                    "13000101017F000001A2EC0000D0070000F401",
                    Convert.ToHexString(packet));
            }
            finally
            {
                Systems.GSList = previous;
            }
        }

        [Fact]
        public void NoOnlineWorlds_EmitsZeroCountWithoutPlaceholder()
        {
            var previous = Systems.GSList;
            try
            {
                Systems.GSList = new Dictionary<int, Systems.SRX_Serverinfo>
                {
                    [1] = Server(status: 0)
                };

                Assert.Equal("0500010100", Convert.ToHexString(
                    Systems.ServerListPacket(cliVersion: 258)));
            }
            finally
            {
                Systems.GSList = previous;
            }
        }

        [Fact]
        public void WanMode_UsesConfiguredWanAddress()
        {
            var previous = Systems.GSList;
            try
            {
                Systems.SRX_Serverinfo server = Server(status: 1);
                server.lan_wan = true;
                server.wan = "10.20.30.40";
                Systems.GSList = new Dictionary<int, Systems.SRX_Serverinfo> { [1] = server };

                byte[] packet = Systems.ServerListPacket(cliVersion: 258);

                Assert.Equal(new byte[] { 10, 20, 30, 40 }, packet[5..9]);
            }
            finally
            {
                Systems.GSList = previous;
            }
        }

        [Fact]
        public void CapacityFieldsPreserveFullUnsignedRange()
        {
            var previous = Systems.GSList;
            try
            {
                Systems.SRX_Serverinfo server = Server(status: 1);
                server.usedSala = ushort.MaxValue;
                server.maxSalas = ushort.MaxValue;
                server.usedSlots = ushort.MaxValue;
                server.maxSlots = ushort.MaxValue;
                Systems.GSList = new Dictionary<int, Systems.SRX_Serverinfo> { [1] = server };

                byte[] packet = Systems.ServerListPacket(cliVersion: 258);

                Assert.Equal("FFFFFFFFFFFFFFFF", Convert.ToHexString(packet[11..19]));
            }
            finally
            {
                Systems.GSList = previous;
            }
        }

        private static Systems.SRX_Serverinfo Server(byte status) => new()
        {
            status = status,
            ip = "127.0.0.1",
            wan = "127.0.0.1",
            port = 41708,
            usedSala = 0,
            maxSalas = 2000,
            usedSlots = 0,
            maxSlots = 500
        };
    }
}
