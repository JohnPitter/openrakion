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
    /// Cadeia FIM-A-FIM do COMBATE type-7 do bot pelo lado do SERVIDOR — o que a "exibição dos hits"
    /// depende in-game, exercitado sem o rakion.exe. Dirige o <see cref="WorldServer"/> + o
    /// <see cref="UdpGameplay"/> REAIS com "clientes" fake (socket UDP + par TCP) e crava, no FIO:
    ///  - o type-7 NUNCA emite create de NPC (0x0307/0x8307) → sem o storm que gerava o "unknown error";
    ///  - o golpe do humano (0x0311) é ARBITRADO server-side, derruba o bot e emite a morte 0x4f no canal FIELD;
    ///  - o bot NUNCA mata o humano server-side (combate cliente-autoritativo — o desync travava o HIT×N);
    ///  - presença reliable do bot é pareada sem relayar nem re-registrar o canal humano↔humano.
    ///
    /// Contraparte de combate do <see cref="BotMovementChainTests"/> (que cobre movimento). Se estes passam e
    /// o comportamento falha in-game, a quebra é no GATE/predição do cliente, não no servidor.
    /// </summary>
    public sealed class BotCombatChainTests : IDisposable
    {
        private readonly WorldServer _world;
        private readonly UdpGameplay _udp;
        private readonly List<FakeHuman> _humans = new();
        private readonly CancellationTokenSource _cts = new();
        private ushort _slotSeq;

        public BotCombatChainTests()
        {
            var cfg = new WorldConfig();
            _world = new WorldServer(cfg, new Database.WorldDatabase(cfg.Db));
            _udp = new UdpGameplay(_world, FreeEphemeralUdpPort());
            _udp.Start();
            typeof(WorldServer).GetField("_udpGame", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_world, _udp);
        }

        // ---------- infraestrutura de "cliente" fake ----------

        /// <summary>Um humano fake: socket UDP (porta &lt; base de bot, p/ NÃO ser classificado como bot),
        /// par TCP real (o servidor escreve frames FIELD nele) e a <see cref="ClientSession"/> registrada.</summary>
        private sealed class FakeHuman
        {
            public required Socket Udp;
            public required IPEndPoint UdpEp;
            public required Socket TcpClient;
            public required Socket TcpServer;
            public required ClientSession Session;
            public readonly ConcurrentQueue<byte[]> UdpRx = new();
            public readonly List<byte> TcpBuf = new();   // stream TCP acumulado (frames [u16 size][content])
        }

        /// <summary>Porta UDP efêmera (para o socket do SERVIDOR — pode ser alta).</summary>
        private static int FreeEphemeralUdpPort()
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        /// <summary>Socket UDP num porto BAIXO (&lt; 41000 = base de porta de bot) — um cliente real tem porta
        /// baixa; a porta efêmera do Windows (≥49152) seria classificada como bot pelo UdpGameplay.</summary>
        private static Socket LowUdpSocket()
        {
            for (int port = 32000; port < 41000; port++)
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try { s.Bind(new IPEndPoint(IPAddress.Loopback, port)); return s; }
                catch (SocketException) { s.Close(); }
            }
            throw new InvalidOperationException("sem porta UDP baixa livre");
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

        /// <summary>Cria + registra um humano fake no world (conectado, em stage, com endpoint UDP conhecido).</summary>
        private FakeHuman AddHuman()
        {
            var udp = LowUdpSocket();
            var udpEp = (IPEndPoint)udp.LocalEndPoint!;
            var (tcpClient, tcpServer) = TcpPair();
            var sess = new ClientSession(tcpServer, _slotSeq++, _world)
            {
                CharName = "H" + _slotSeq, CharClass = 1, CharLevel = 5,
                InField = true, SlotActive = true, Status = 3, UdpEndpoint = udpEp,
            };
            typeof(ClientSession).GetProperty("Connected")!.SetValue(sess, true);
            var dict = (ConcurrentDictionary<ushort, ClientSession>)typeof(WorldServer)
                .GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_world)!;
            dict[sess.Slot] = sess;

            var h = new FakeHuman { Udp = udp, UdpEp = udpEp, TcpClient = tcpClient, TcpServer = tcpServer, Session = sess };
            _humans.Add(h);
            _ = Task.Run(() => UdpRecvLoop(h));
            _ = Task.Run(() => TcpRecvLoop(h));
            return h;
        }

        private async Task UdpRecvLoop(FakeHuman h)
        {
            var buf = new byte[2048];
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var res = await h.Udp.ReceiveFromAsync(buf, SocketFlags.None, any);
                    if (res.ReceivedBytes > 0) h.UdpRx.Enqueue(buf.AsSpan(0, res.ReceivedBytes).ToArray());
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { }
            }
        }

        private async Task TcpRecvLoop(FakeHuman h)
        {
            var buf = new byte[8192];
            while (!_cts.IsCancellationRequested)
            {
                int n;
                try { n = await h.TcpClient.ReceiveAsync(buf, SocketFlags.None); }
                catch { break; }
                if (n <= 0) break;
                lock (h.TcpBuf) h.TcpBuf.AddRange(buf.AsSpan(0, n).ToArray());
            }
        }

        /// <summary>Walk dos frames FIELD/lobby acumulados no TCP: cada frame é [u16 size][content], size = tamanho
        /// TOTAL (inclui os 2 bytes). O msgType é os 2 primeiros bytes do content. Plaintext (cripto desligada nos testes).</summary>
        private static List<(ushort msgType, byte[] data)> ParseTcpFrames(FakeHuman h)
        {
            byte[] all;
            lock (h.TcpBuf) all = h.TcpBuf.ToArray();
            var frames = new List<(ushort, byte[])>();
            int off = 0;
            while (off + 2 <= all.Length)
            {
                int size = BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(off));
                if (size < 4 || off + size > all.Length) break;   // frame incompleto: espera mais bytes
                ushort msgType = BinaryPrimitives.ReadUInt16LittleEndian(all.AsSpan(off + 2));
                var data = all.AsSpan(off + 4, size - 4).ToArray();
                frames.Add((msgType, data));
                off += size;
            }
            return frames;
        }

        // ---------- cenário ----------

        /// <summary>Sala em jogo (default Golem mode=1, gravity 210) com o(s) humano(s) já adicionado(s) e 1 bot
        /// inimigo, round rodando. O 1º humano é o master/loader. <paramref name="mode"/> = TeamDeath (3) nos
        /// cenários de morte-com-respawn (no Golem, eliminar o único do time ENCERRA o round e o BotTick para).</summary>
        private (Field f, FakeHuman host, BotPlayer bot, int botSeat) ArrangeMatch(int humans = 1, byte mode = 1)
        {
            var host = AddHuman();
            for (int i = 1; i < humans; i++) AddHuman();

            var f = new Field(0) { Mode = mode, MapId = 210, MaxRounds = 3, RoundDurationSec = 300, IsRoom = true };
            foreach (var h in _humans)
            {
                int seat = f.AssignSeat(h.Session);   // AssignSeat NÃO grava FieldSeat na sessão — o join real grava; aqui espelhamos.
                h.Session.FieldSeat = (byte)seat;
                h.Session.FieldId = f.Id;
            }
            f.Master = host.Session;
            f.MasterSlot = host.Session.FieldSeat;
            lock (_world.Fields) _world.Fields.Add(f);

            var added = _world.Bots.AddBotToField(f, host.Session);
            Assert.True(added.Ok, added.Message);
            f.State = 2;
            f.StartRound();
            Assert.Equal(MatchPhase.Playing, f.Phase);
            return (f, host, added.Bot!, added.Seat);
        }

        /// <summary>Posiciona um humano no domínio (o que o handler do 0x30a faria) — origem do golpe.</summary>
        private static void PlaceHuman(Field f, FakeHuman h, float x, float z, short heading)
        {
            var rec = f.FindRec(h.Session)!;
            rec.State = 4; rec.LastX = x; rec.LastZ = z; rec.LastPositionMs = 1; rec.LastHeading = heading;
        }

        /// <summary>Golpe 0x0311 de um humano, injetado pelo socket UDP dele (porta baixa) no socket do servidor —
        /// a MESMA via de um cliente real. srcSlot = seat do humano (o servidor arbitra por srcSlot, não por IP).</summary>
        private void SendHumanAttack(FakeHuman h, int seat, ushort actionId)
        {
            byte[] atk = BotMovement.BuildAttackDatagram(new BotPlayer(0, "tmp", 5, 1, 0), seat, actionId);
            h.Udp.SendTo(atk, new IPEndPoint(IPAddress.Loopback, _udp.Port));
        }

        private static async Task<bool> WaitUntil(Func<bool> cond, int timeoutMs)
        {
            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (cond()) return true;
                await Task.Delay(25);
            }
            return cond();
        }

        // ---------- testes ----------

        [Fact]
        public async Task TypeSevenBot_NeverEmitsNpcCreate_NoUnknownErrorStorm()
        {
            var (f, host, _, _) = ArrangeMatch();
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);

            // Roda o motor ~3s (passa o hold de spawn e acumula tráfego).
            long deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline) { _world.Bots.BotTick(f); await Task.Delay(50); }

            // NENHUM datagrama de create-NPC (0x0307 unreliable nem 0x8307 reliable) pode chegar ao humano:
            // o re-envio de create era o "storm" que resetava a entidade e gerava o "unknown error".
            int npcCreates = 0, moves = 0;
            foreach (var p in host.UdpRx.ToArray())
            {
                if (p.Length >= 2 && p[0] == 0x07 && (p[1] == 0x03 || p[1] == 0x83)) npcCreates++;
                if (p.Length >= 2 && p[0] == 0x0a && p[1] == 0x03) moves++;
            }
            Assert.Equal(0, npcCreates);
            Assert.True(moves > 0, "o bot type-7 deveria emitir 0x30a (movimento) ao humano");
        }

        [Fact]
        public async Task BotPresence_EmitsReliableChannelAndStillMoves()
        {
            var (f, host, _, botSeat) = ArrangeMatch();
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);

            long deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline) { _world.Bots.BotTick(f); await Task.Delay(50); }

            int lockstep = 0, anchor = 0, moves = 0;
            foreach (var p in host.UdpRx.ToArray())
            {
                if (p.Length >= 13 && p[0] == 0x04 && p[1] == 0x03)
                {
                    Assert.Equal((byte)botSeat, p[6]);
                    Assert.Equal(host.Session.FieldSeat, p[7]);
                    lockstep++;
                }
                if (p.Length == 23 && p[0] == 0x0c && p[1] == 0x83)
                {
                    Assert.Equal((byte)botSeat, p[6]);
                    anchor++;
                }
                if (p.Length >= 2 && p[0] == 0x0a && p[1] == 0x03) moves++;      // 0x030a
            }
            Assert.True(lockstep > 0, "faltou o lado bot→humano do 0x0304");
            Assert.True(anchor > 0, "faltou a âncora 0x830C do combatente bot");
            Assert.True(moves > 0, "a presença reliable não pode interromper o movimento 0x30a");
        }

        [Fact]
        public async Task BotPresence_PairsEachPushWithOnlyItsTargetHuman()
        {
            var (f, host, _, botSeat) = ArrangeMatch(humans: 2);
            var other = _humans[1];
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);

            long deadline = Environment.TickCount64 + 2200;
            while (Environment.TickCount64 < deadline) { _world.Bots.BotTick(f); await Task.Delay(50); }

            AssertOnlyPairedBotPushes(host, botSeat);
            AssertOnlyPairedBotPushes(other, botSeat);
        }

        [Fact]
        public async Task HumanReliableState_IsAcknowledgedByBotEntity()
        {
            var (f, host, _, botSeat) = ArrangeMatch();
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);

            var source = new BotPlayer(0, "human", 5, 1, 0) { UdpSeq = 0x08f3 };
            byte[] anchor = BotMovement.BuildCombatAnchorDatagram(
                source, host.Session.FieldSeat, host.Session.FieldSeat);
            host.Udp.SendTo(anchor, new IPEndPoint(IPAddress.Loopback, _udp.Port));

            bool acknowledged = await WaitUntil(() =>
            {
                foreach (var packet in host.UdpRx.ToArray())
                {
                    if (packet.Length != 11 || packet[0] != 0x00 || packet[1] != 0x40) continue;
                    if (packet[6] != (byte)botSeat) continue;
                    if (BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(7)) == 0x08f3) return true;
                }
                return false;
            }, 2000);

            Assert.True(acknowledged, "o bot não confirmou o 0x830C reliable recebido do humano");
        }

        [Fact]
        public async Task HumanMeleeKillsBot_DeathGoesToObserversButNotAttacker()
        {
            // TeamDeath: a morte NÃO encerra o round (respawn) — o BotTick segue vivo depois do golpe letal.
            var (f, host, bot, botSeat) = ArrangeMatch(humans: 2, mode: (byte)GameMode.TeamDeath);
            var observer = _humans[1];
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);

            // Humano em (0,0) encarando +Z (heading 180); bot colado a +Z, com HP de um golpe (dano 40).
            PlaceHuman(f, host, 0f, 0f, heading: 180);
            bot.X = 0f; bot.Z = 2f; bot.Hp = 40;

            SendHumanAttack(host, host.Session.FieldSeat, actionId: 0x0400);

            // O golpe chega pelo fio, é arbitrado, o bot morre e o 0x4f é broadcastado no canal FIELD.
            // Prova DURÁVEL = o 0x4f no fio (emitido só DEPOIS de OnPlayerDeath): esperar pela morte via
            // `bot.Dead` seria racy (a flag é setada em TakeDamage, alguns passos antes de OnPlayerDeath, na
            // thread de recv) — o frame FIELD é o sinal que o humano de fato vê.
            bool died = await WaitUntil(() => bot.Dead, 2000);
            Assert.True(died, "o golpe do humano não derrubou o bot server-side");

            bool observerSaw4f = await WaitUntil(() =>
            {
                foreach (var (mt, data) in ParseTcpFrames(observer))
                    if (mt == 0x4f && data.Length >= 3 && data[0] == (byte)botSeat && data[2] == (byte)host.Session.FieldSeat) return true;
                return false;
            }, 2000);
            Assert.True(observerSaw4f, "a morte 0x4f do bot não chegou ao observador");
            foreach (var (mt, data) in ParseTcpFrames(host))
                Assert.False(mt == 0x4f && data.Length > 0 && data[0] == (byte)botSeat,
                    "o atacante recebeu morte duplicada do bot, causa provável do Unknown error");
        }

        [Fact]
        public async Task HumanHitOnBot_AnnouncesHitChainOnWire_AndKnocksBack()
        {
            var (f, host, bot, _) = ArrangeMatch();
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);

            // Humano em (0,0) encarando +Z; bot colado, com HP folgado (golpes NÃO-fatais).
            PlaceHuman(f, host, 0f, 0f, heading: 180);
            bot.X = 0f; bot.Z = 2f; bot.Hp = 200;
            float zBefore = bot.Z;

            // Dois golpes espaçados além do cooldown (250ms) e dentro da janela de combo (4s).
            SendHumanAttack(host, host.Session.FieldSeat, 0x0400);
            await Task.Delay(400);
            SendHumanAttack(host, host.Session.FieldSeat, 0x0500);

            // A exibição do hit = anúncio de stage 0x47 "HIT : xN" no TCP, encadeando x1 -> x2.
            bool sawChain = await WaitUntil(() =>
            {
                bool x1 = false, x2 = false;
                foreach (var (mt, data) in ParseTcpFrames(host))
                {
                    if (mt != 0x47 || data.Length < 2) continue;
                    string text = System.Text.Encoding.ASCII.GetString(data, 1, data.Length - 1).TrimEnd('\0');
                    if (text.StartsWith("HIT : x1")) x1 = true;
                    if (text.StartsWith("HIT : x2")) x2 = true;
                }
                return x1 && x2;
            }, 2000);
            Assert.True(sawChain, "os anúncios 0x47 'HIT : x1'/'HIT : x2' não chegaram ao humano (exibição do hit no bot)");

            // Recuo visível: o bot foi empurrado p/ LONGE do atacante (0,0) — o próximo 0x30a carrega a nova posição.
            Assert.True(bot.Z > zBefore + 0.5f, $"o bot não recuou ao apanhar (Z {zBefore} -> {bot.Z})");
            Assert.Equal(120, bot.Hp);   // 2 golpes de 40 aplicados (sem throttle indevido)
        }

        [Fact]
        public async Task BotNeverKillsHuman_NoServerSideDeathForHuman()
        {
            var (f, host, bot, _) = ArrangeMatch();
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);

            int hostSeat = host.Session.FieldSeat;
            // Humano colado ao bot: o bot vai "revidar" (ResolveHumanHitByBot) muitas vezes durante o loop.
            PlaceHuman(f, host, 0f, 0f, heading: 180);
            bot.X = 0f; bot.Z = 1.5f; bot.TargetSeat = hostSeat;

            long deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline)
            {
                var rec = f.FindRec(host.Session)!;
                rec.LastPositionMs = 1; rec.LastX = 0f; rec.LastZ = 0f;   // mantém o humano "vivo e visto"
                _world.Bots.BotTick(f);
                await Task.Delay(50);
            }

            Assert.False(f.RecAt(hostSeat)!.Dead, "o servidor marcou o HUMANO morto — o bot não pode matar server-side");
            // E nenhuma morte 0x4f com a vítima = seat do humano pode ter sido emitida.
            foreach (var (mt, data) in ParseTcpFrames(host))
                if (mt == 0x4f && data.Length >= 1)
                    Assert.NotEqual((byte)hostSeat, data[0]);
        }

        [Fact]
        public async Task NoHijack_ServerDoesNotRelayNorReRegisterHumanToHuman_WithBotPresent()
        {
            var (f, host, _, _) = ArrangeMatch(humans: 2);
            _world.Bots.SpawnFieldBotsInStage(f, host.Session);
            var other = _humans[1];
            byte hostSeat = host.Session.FieldSeat;

            PlaceHuman(f, host, 0f, 0f, heading: 180);
            PlaceHuman(f, other, 10f, 10f, heading: 0);

            // O host golpeia (a cópia que ele manda AO BOT chega ao servidor). Com o bot no field o servidor NÃO
            // pode: (a) relayar o 0x0311 ao outro humano — ele já o recebe P2P-DIRETO; relayar entrega em DOBRO e
            // corrompe o stream reliable (0x830C/0x8312) → era o que matava o HIT×N (wiretap 2026-07-10: a conta
            // tx_p/_A = rx_de_B + rx_do_bot batia exata). (b) registrar (0x319) o SEAT DO HOST no outro humano —
            // isso redirecionava o canal P2P-direto do host p/ o servidor (o próprio sequestro).
            for (int i = 0; i < 6; i++) { SendHumanAttack(host, hostSeat, 0x0400); await Task.Delay(60); }
            byte[] humanPush = BotLockstep.BuildPush(77, hostSeat, other.Session.FieldSeat, 0x12345678);
            host.Udp.SendTo(humanPush, new IPEndPoint(IPAddress.Loopback, _udp.Port));
            await Task.Delay(300);

            foreach (var p in other.UdpRx.ToArray())
            {
                bool relayedAttack = p.Length >= 10 && p[0] == 0x11 && p[1] == 0x03 && p[6] == hostSeat;
                Assert.False(relayedAttack, "servidor relayou o ataque humano→humano (sequestro: dupla-entrega mata o HIT×N)");
                // 0x319 registrando o seat do HOST (byte 7 = seat) no outro humano = sequestro do canal P2P-direto.
                bool reRegisteredHost = p.Length == 8 && p[0] == 0x19 && p[1] == 0x03 && p[7] == hostSeat;
                Assert.False(reRegisteredHost, "servidor re-registrou (0x319) o seat de um HUMANO — sequestra o P2P-direto");
                bool relayedHumanPush = p.Length >= 13 && p[0] == 0x04 && p[1] == 0x03 && p[6] == hostSeat;
                Assert.False(relayedHumanPush, "servidor relayou 0x0304 humano→humano");
            }
        }

        private static void AssertOnlyPairedBotPushes(FakeHuman human, int botSeat)
        {
            int pushes = 0;
            foreach (var packet in human.UdpRx.ToArray())
            {
                if (packet.Length < 13 || packet[0] != 0x04 || packet[1] != 0x03) continue;
                Assert.Equal((byte)botSeat, packet[6]);
                Assert.Equal(human.Session.FieldSeat, packet[7]);
                pushes++;
            }
            Assert.True(pushes > 0, $"nenhum push bot→humano chegou ao seat {human.Session.FieldSeat}");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _udp.Stop();
            foreach (var h in _humans)
            {
                try { h.Udp.Close(); } catch { }
                try { h.TcpClient.Close(); } catch { }
                try { h.TcpServer.Close(); } catch { }
            }
        }
    }
}
