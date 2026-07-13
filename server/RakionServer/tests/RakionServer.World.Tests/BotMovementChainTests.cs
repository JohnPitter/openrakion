using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Cadeia FIM-A-FIM do movimento do bot no lado do SERVIDOR: BotTick (IA) -> EmitBotMovement
    /// (socket dedicado do bot) -> UdpGameplay.Process (desambiguação por srcSlot) -> RelayToAllFields
    /// (0x319 endpoint-register + relay do 0x30a) -> endpoint UDP do humano. Usa WorldServer +
    /// UdpGameplay REAIS e um "cliente" fake (socket UDP) no papel do host — reproduz o caminho
    /// que o teste in-game exercita, sem o rakion.exe. Se este teste passa e o bot segue parado
    /// in-game, a quebra é no GATE do cliente (0x319/lockstep), não no servidor.
    /// </summary>
    public class BotMovementChainTests : IDisposable
    {
        private readonly WorldServer _world;
        private readonly UdpGameplay _udp;
        private readonly Socket _fakeClientUdp;          // socket do "cliente" (host humano)
        private readonly IPEndPoint _fakeClientEp;
        private readonly Socket _tcpClient;              // ponta do cliente do par TCP da sessão
        private readonly Socket _tcpServer;              // ponta do servidor (vai pro ClientSession)
        private readonly ConcurrentQueue<byte[]> _rx = new();
        private readonly CancellationTokenSource _cts = new();

        public BotMovementChainTests()
        {
            var cfg = new WorldConfig();
            _world = new WorldServer(cfg, new Database.WorldDatabase(cfg.Db));

            // UdpGameplay real numa porta efêmera, injetado no campo privado (o BotManager lê a porta via lambda).
            _udp = new UdpGameplay(_world, FreeUdpPort());
            _udp.Start();
            typeof(WorldServer).GetField("_udpGame", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_world, _udp);

            // "Cliente" fake: socket UDP que recebe o que o servidor relaya ao host.
            _fakeClientUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _fakeClientUdp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _fakeClientEp = (IPEndPoint)_fakeClientUdp.LocalEndPoint!;
            _ = Task.Run(RecvLoop);

            // Par TCP real p/ a sessão (SendLobby/SendMessage escrevem num socket de verdade; a ponta
            // do cliente só drena).
            (_tcpClient, _tcpServer) = TcpPair();
            _ = Task.Run(DrainTcp);
        }

        private static int FreeUdpPort()
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        private static (Socket client, Socket server) TcpPair()
        {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(listener.LocalEndPoint!);
            var server = listener.Accept();
            listener.Close();
            return (client, server);
        }

        private async Task RecvLoop()
        {
            var buf = new byte[2048];
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var res = await _fakeClientUdp.ReceiveFromAsync(buf, SocketFlags.None, any);
                    if (res.ReceivedBytes <= 0) continue;
                    _rx.Enqueue(buf.AsSpan(0, res.ReceivedBytes).ToArray());
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { }
            }
        }

        private async Task DrainTcp()
        {
            var buf = new byte[8192];
            try { while (await _tcpClient.ReceiveAsync(buf, SocketFlags.None) > 0) { } }
            catch { }
        }

        /// <summary>Monta o cenário in-game mínimo: host humano (sessão registrada, em stage, endpoint UDP
        /// conhecido) numa sala Golem (mode=1, gravity 210) com 1 bot, round rodando.</summary>
        private (Field f, ClientSession host, BotPlayer bot, int botSeat) Arrange()
        {
            var host = new ClientSession(_tcpServer, 0, _world)
            {
                CharName = "Heroi",
                CharClass = 1,
                CharLevel = 5,
                InField = true,
                SlotActive = true,        // slot vivo (GetSessionByIp exige; in-game é setado no login)
                Status = 3,               // UserStatus.InField (stage)
                UdpEndpoint = _fakeClientEp,
            };
            SetConnected(host);
            RegisterSession(host);

            var f = new Field(0) { Mode = 1, MapId = 210, MaxRounds = 3, RoundDurationSec = 300, IsRoom = true };
            int hostSeat = f.AssignSeat(host);
            host.FieldId = f.Id;
            f.Master = host;
            f.MasterSlot = hostSeat;
            lock (_world.Fields) _world.Fields.Add(f);

            var added = _world.Bots.AddBotToField(f, host);
            Assert.True(added.Ok, added.Message);
            f.State = 2;
            f.StartRound();                                  // Phase = Playing
            Assert.Equal(MatchPhase.Playing, f.Phase);
            return (f, host, added.Bot!, added.Seat);
        }

        private void RegisterSession(ClientSession s)
        {
            var dict = (ConcurrentDictionary<ushort, ClientSession>)typeof(WorldServer)
                .GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_world)!;
            dict[s.Slot] = s;
        }

        private static void SetConnected(ClientSession s)
        {
            var prop = typeof(ClientSession).GetProperty("Connected")!;
            if (prop.CanWrite) { prop.SetValue(s, true); return; }
            typeof(ClientSession).GetField("<Connected>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(s, true);
        }

        [Fact]
        public async Task BotTick_EmitsMovement_ThatReachesTheHumanEndpoint_WithChangingPosition()
        {
            var (f, host, _, botSeat) = Arrange();
            _world.Bots.SpawnFieldBotsInStage(f, host);      // caminho primário (load do host)

            // Roda o motor como o FieldEngineLoop faria (100ms), tempo bastante p/ passar o hold
            // de spawn (BotMoveStartDelayMs=2200) e acumular 0x30a andando.
            long deadline = Environment.TickCount64 + 5000;
            while (Environment.TickCount64 < deadline)
            {
                _world.Bots.BotTick(f);
                await Task.Delay(50);
            }

            var frames = _rx.ToArray();
            Assert.True(frames.Length > 0, "o endpoint do humano não recebeu NENHUM datagrama do servidor");

            // 0x319 endpoint-register do slot do bot precisa preceder/acompanhar os moves.
            bool saw319 = false;
            var moves = new List<(short X, short Z)>();
            foreach (var p in frames)
            {
                if (p.Length >= 8 && p[0] == 0x19 && p[1] == 0x03 && p[7] == (byte)botSeat) saw319 = true;
                // 0x30a: [u16 0x030a][u32 seq][u8 srcSlot][corpo 19B]; corpo: x/z packed s16 em +11/+15.
                if (p.Length >= 26 && p[0] == 0x0a && p[1] == 0x03 && p[6] == (byte)botSeat)
                    moves.Add((BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(11)),
                               BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(15))));
            }

            Assert.True(saw319, "0x319 (endpoint-register do bot) não chegou ao humano — o gate do cliente nunca abriria");
            Assert.True(moves.Count >= 10, $"esperava um fluxo de 0x30a (~10Hz), chegaram {moves.Count}");

            // O bot tem de ANDAR: a posição dos últimos 0x30a difere da dos primeiros além do ruído.
            var first = moves[0];
            var last = moves[^1];
            int dx = Math.Abs(last.X - first.X), dz = Math.Abs(last.Z - first.Z);
            Assert.True(dx + dz > 100,   // 100 = 1.0 coord de mundo (packed *100)
                $"posição do bot não avançou: primeiro=({first.X},{first.Z}) último=({last.X},{last.Z}) em {moves.Count} moves");
        }

        [Fact]
        public async Task BotAttack_0x311_IsRelayed_NotArbitratedAgainstTheBotItself()
        {
            var (f, host, bot, botSeat) = Arrange();
            _world.Bots.SpawnFieldBotsInStage(f, host);

            // Injeta um 0x0311 "do bot" direto no socket do servidor (como o EmitBotAttack faz) e
            // verifica que ele é RELAYADO ao humano (e não arbitrado como golpe humano->bot).
            using var botSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            botSock.Bind(new IPEndPoint(IPAddress.Loopback, 45999));   // > BotUdpPortBase (41000): porta "de bot"
            byte[] atk = BotMovement.BuildAttackDatagram(bot, botSeat, 0x0400);
            botSock.SendTo(atk, new IPEndPoint(IPAddress.Loopback, _udp.Port));

            ushort hpBefore = bot.Hp;
            long deadline = Environment.TickCount64 + 1500;
            bool relayed = false;
            while (Environment.TickCount64 < deadline && !relayed)
            {
                foreach (var p in _rx.ToArray())
                    if (p.Length >= 10 && p[0] == 0x11 && p[1] == 0x03 && p[6] == (byte)botSeat) relayed = true;
                await Task.Delay(50);
            }

            Assert.True(relayed, "0x0311 do bot não foi relayado ao humano");
            Assert.Equal(hpBefore, bot.Hp);   // não pode ter sido arbitrado contra o próprio bot
        }

        public void Dispose()
        {
            _cts.Cancel();
            _udp.Stop();
            try { _fakeClientUdp.Close(); } catch { }
            try { _tcpClient.Close(); } catch { }
            try { _tcpServer.Close(); } catch { }
        }
    }
}
