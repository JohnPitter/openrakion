using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Camada UDP de gameplay do world (portas [UDP] Port1/Port2). Reconstruida do
    /// worldserv.exe: o recv (FUN_004040d0) faz recvfrom de ate 0x4b0 (1200) bytes e
    /// enfileira (FUN_0042e9c0); o send (FUN_00404010) faz sendto ao endpoint do peer.
    /// O original usa esta porta para handshake/controle e deixa movimento/combate no
    /// canal P2P direto. O relay por field é uma extensão de compatibilidade configurável.
    ///
    /// O endpoint é autenticado pelo slot, IP TCP e chave de sessão enviada no 0x0c.
    /// Depois do handshake, apenas o endpoint exato pode enviar input e ações ao field.
    /// </summary>
    public sealed class UdpGameplay(
        WorldServer world, int port, bool relayCompatibilityEnabled,
        int relayPacketsPerSecond, int relayBurst)
    {
        public const int MaxPacket = 0x4b0; // 1200, igual ao recvfrom do binario
        private const byte GameplayFeedbackOp0 = 0x15;
        private const byte GameplayFeedbackOp1 = 0x83;
        private const byte DefaultGameplayState = 0x0a;

        private readonly WorldServer _world = world;
        private readonly int _port = port;
        private readonly bool _relayCompatibilityEnabled = relayCompatibilityEnabled;
        private readonly UdpRelayLimiterRegistry _relayLimits = new UdpRelayLimiterRegistry(relayPacketsPerSecond, relayBurst);
        private Socket? _sock;
        private CancellationTokenSource? _cts;
        private readonly byte[] _rx = new byte[2048];

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _sock.Bind(new IPEndPoint(IPAddress.Any, _port));
            Log.Ok("udp", "gameplay UDP ouvindo na porta {0}; relay compatível={1}",
                _port, _relayCompatibilityEnabled);
            _ = Task.Run(() => RecvLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _sock?.Close(); } catch { }
        }

        private async Task RecvLoopAsync(CancellationToken ct)
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!ct.IsCancellationRequested && _sock != null)
            {
                SocketReceiveFromResult res;
                try { res = await _sock.ReceiveFromAsync(_rx, SocketFlags.None, any, ct); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { break; }

                int n = res.ReceivedBytes;
                if (n <= 0 || n > MaxPacket) continue;
                var from = (IPEndPoint)res.RemoteEndPoint;

                byte[] pkt = new byte[n];
                Buffer.BlockCopy(_rx, 0, pkt, 0, n);
                try { Process(from, pkt); }
                catch (Exception ex) { Log.Error("udp", "process: {0}", ex.Message); }
            }
        }

        /// <summary>
        /// Processa um pacote UDP de gameplay (FUN_00425d80/FUN_00425fa0): valida slot + IP de
        /// origem + chave de sessao (user+0x1464), registra o endpoint UDP do jogador e ecoa um
        /// pacote 0x201 (keepalive/sync). As acoes de jogo em si vao por TCP (field.Broadcast).
        /// Pacote: [u16 slot][u32 sessionKey][..][u32 data @ off6][u16 port @ off0xa].
        /// </summary>
        /// <summary>
        /// Tick de gameplay do server (capturado): [15 83][SEQ][00 00 00][00 0a]. O SEQ e' o
        /// relogio/frame da partida — INCREMENTA com o tempo (o cliente o ecoa). Manter SEQ fixo
        /// congela o timer/personagem; por isso o seq vem da sessao (ClientSession.GameSeq, que
        /// um timer incrementa).
        /// </summary>
        /// <summary>Envia um datagrama de gameplay já montado ao endpoint (usado p/ injetar o movimento
        /// SINTETIZADO do bot aos peers humanos — o servidor é a fonte, sem relay do canal humano).</summary>
        public void SendGameplayDatagram(IPEndPoint to, byte[] datagram)
        {
            try { _sock?.SendTo(datagram, to); }
            catch (Exception ex) { Log.Debug("udp", "bot datagram {0}: {1}", to, ex.Message); }
        }

        public void SendBotGameplay(PlayerRec target, byte[] datagram)
        {
            ClientSession? session = target.Session;
            if (session == null || !target.Occupied) return;
            if (target.UsesTunneling)
            {
                byte[] payload = GameplayActionDatagram.BuildTunnelPayload(datagram);
                using var writer = new PacketWriter();
                writer.WriteWord((ushort)payload.Length);
                writer.WriteBytes(payload);
                session.SendMessage(0x57, writer.ToArray());
                return;
            }

            IPEndPoint? endpoint = session.UdpAdvertisedEndpoint ?? session.UdpEndpoint;
            if (endpoint != null)
                SendGameplayDatagram(endpoint, datagram);
        }

        public void SendTick(IPEndPoint to, byte seq, byte state = DefaultGameplayState)
        {
            byte[] p = [GameplayFeedbackOp0, GameplayFeedbackOp1, seq, 0x00, 0x00, 0x00, 0x00, state];
            try { _sock?.SendTo(p, to); }
            catch (Exception ex) { Log.Debug("udp", "tick {0}: {1}", to, ex.Message); }
        }

        private ClientSession? ResolveSender(IPEndPoint from) => _world.GetSessionByUdpEndpoint(from);

        private void Process(IPEndPoint from, byte[] pkt)
        {
            Log.Debug("udp", "RX {0}B de {1}", pkt.Length, from);

            byte[]? echo = _world.AcceptUdpPort2Handshake(from, pkt);
            if (echo != null)
            {
                try { _sock!.SendTo(echo, from); }
                catch (Exception ex) { Log.Debug("udp", "echo {0}: {1}", from, ex.Message); }
                return;
            }

            if (BotTelemetryDatagram.TryUnwrap(
                    pkt, out ReadOnlySpan<byte> telemetry, out bool headlessRelay))
            {
                ProcessBotTelemetry(from, telemetry, headlessRelay);
                return;
            }

            // O World v258 ignora os tipos 0x03xx/0x83xx nesta porta: eles pertencem ao
            // socket P2P direto do engine. O relay abaixo é uma extensão de compatibilidade
            // para ambientes onde os peers não conseguem abrir o canal direto.
            if (!_relayCompatibilityEnabled) return;

            // 0x4000 confirma a sequência reliable. No fallback central, segue intacto ao peer.
            // 0x8315 tem semantica dupla: atualiza o feedback local e continua para o relay P2P.
            if (pkt.Length == 8 && pkt[0] == GameplayFeedbackOp0 && pkt[1] == GameplayFeedbackOp1)
            {
                var gs = ResolveSender(from);
                if (gs != null)
                {
                    gs.LastGameplayFeedbackSeq = pkt[2];
                    gs.LastGameplayFeedbackState = pkt[7];
                    Log.Debug("udp", "[{0}] feedback 1583 seq={1:X2} state={2:X2}", gs.Slot, pkt[2], pkt[7]);
                }
                else
                {
                    Log.Debug("udp", "feedback 1583 sem sessao ({0}) seq={1:X2} state={2:X2}", from, pkt[2], pkt[7]);
                }
            }
            // ACK 0x030d do cliente (7B): consome.
            if (pkt.Length >= 2 && pkt[0] == 0x0d && pkt[1] == 0x03) return;

            // ACAO DE CAMPO (combate/objetos): markers legados 0x0401/0x0201/0x0301 e streams
            // do engine 0x030A/0x030F/0x0311. O world original PROCESSA + BROADCASTA aos OUTROS membros do field
            // via SendData_Unreliable. FUN_00426b30 confirma: o broadcast EXCLUI o proprio sender
            // ("if slot != sender"). O jogador LOCAL preve a acao no cliente; reenviar de volta a ele
            // causa DUPLA-PROCESSACAO -> hit fantasma no inicio + ataques que nao completam + troca de
            // arma bugada numa direcao. Relay so aos OUTROS in-field; em solo (sem peers) nada e' enviado
            // e o cliente preve sozinho (fiel ao original).
            bool legacyAction = pkt.Length >= 2 && pkt[0] == 0x01 &&
                (pkt[1] == 0x04 || pkt[1] == 0x02 || pkt[1] == 0x03);
            bool engineAction = GameplayActionDatagram.TryParseHeader(pkt, out var actionHeader);
            bool peerControl = GameplayPeerDatagramCodec.TryParse(pkt, out var peerDatagram);
            if (legacyAction || engineAction || peerControl)
            {
                ushort type = engineAction ? actionHeader.Type :
                    peerControl ? peerDatagram.Type : (ushort)(pkt[0] | pkt[1] << 8);
                byte? sourceSeat = engineAction ? actionHeader.SourceSlot :
                    peerControl ? peerDatagram.SourceSeat : null;
                RelayToField(from, pkt, type, sourceSeat, peerDatagram);
            }

            Log.Debug("udp", "pacote não reconhecido ({0}B de {1})", pkt.Length, from);
        }

        private void RelayToField(
            IPEndPoint from, byte[] packet, ushort type, byte? sourceSeat,
            GameplayPeerDatagram peerDatagram)
        {
            var sender = ResolveSender(from);
            if (sender == null || !sender.InField)
            {
                Log.Warn("udp", "datagrama 0x{0:X4} descartado de endpoint não autenticado {1}", type, from);
                return;
            }
            bool sourceMatches = peerDatagram.Type != 0
                ? GameplayPeerDatagramCodec.SourceMatches(
                    peerDatagram, sender.FieldSeat, packet)
                : sourceSeat is null || sourceSeat == sender.FieldSeat;
            if (!sourceMatches)
            {
                Log.Warn("udp", "datagrama 0x{0:X4} descartado: seat declarado {1} != autenticado {2}",
                    type, sourceSeat?.ToString() ?? "-", sender.FieldSeat);
                return;
            }
            if (!_relayLimits.TryConsume(sender.Slot, sender.UdpKey, Environment.TickCount64))
            {
                Log.Debug("udp", "datagrama 0x{0:X4} limitado para slot {1}", type, sender.Slot);
                return;
            }

            HumanMatchCapture.RecordUdp(sender, type, packet);
            ApplyBotInput(sender, type, packet);
            RelayToUdpPeers(sender, packet, type);
        }

        private void ProcessBotTelemetry(
            IPEndPoint from, ReadOnlySpan<byte> packet, bool headlessRelay)
        {
            ClientSession? sender = ResolveSender(from);
            // Peer que saiu do field ainda tem datagramas em trânsito — é rede normal, não
            // anomalia. Só quem está autenticado E em partida pode forjar telemetria, então o
            // aviso de segurança fica restrito a esse caso; o resto é ruído em nível Debug.
            if (sender == null || !sender.InField)
            {
                Log.Debug("udp", "telemetria de bot fora de partida descartada de {0}", from);
                return;
            }
            if (!TryValidateBotTelemetry(packet, sender.FieldSeat, out ushort type))
            {
                Log.Warn(
                    "udp",
                    "telemetria de bot inválida do slot {0} ({1})",
                    sender.Slot,
                    from);
                return;
            }
            if (!_relayLimits.TryConsume(sender.Slot, sender.UdpKey, Environment.TickCount64)) return;
            ApplyBotInput(sender, type, packet);
            if (headlessRelay)
            {
                RelayToAdvertisedPeers(sender, packet, type);
                return;
            }
            Log.Debug("udp", "telemetria 0x{0:X4} aplicada somente no servidor para slot {1}",
                type, sender.Slot);
        }

        private void RelayToAdvertisedPeers(
            ClientSession sender, ReadOnlySpan<byte> packet, ushort type)
        {
            byte[] datagram = packet.ToArray();
            int relayed = 0;
            foreach (ClientSession session in _world.Sessions)
            {
                IPEndPoint? endpoint = session.UdpAdvertisedEndpoint;
                if (!session.InField || endpoint == null || session == sender ||
                    session.FieldId != sender.FieldId) continue;
                try { _sock!.SendTo(datagram, endpoint); relayed++; }
                catch (SocketException ex)
                {
                    Log.Debug("udp", "relay headless 0x{0:X4} para {1}: {2}",
                        type, endpoint, ex.Message);
                }
            }
            Log.Debug("udp", "telemetria headless 0x{0:X4} relay p/ {1} peer(s) do field {2}",
                type, relayed, sender.FieldId);
        }

        private void RelayToUdpPeers(
            ClientSession sender, ReadOnlySpan<byte> packet, ushort type)
        {
            byte[] datagram = packet.ToArray();
            int relayed = 0;
            foreach (ClientSession session in _world.Sessions)
            {
                if (!session.InField || session.UdpEndpoint == null || session == sender ||
                    session.FieldId != sender.FieldId) continue;
                try { _sock!.SendTo(datagram, session.UdpEndpoint); relayed++; }
                catch (SocketException ex)
                {
                    Log.Debug("udp", "relay 0x{0:X4} para {1}: {2}",
                        type, session.UdpEndpoint, ex.Message);
                }
            }
            Log.Debug("udp", "datagrama 0x{0:X4} relay p/ {1} peer(s) do field {2}",
                type, relayed, sender.FieldId);
        }

        private static bool TryValidateBotTelemetry(
            ReadOnlySpan<byte> packet, byte senderSeat, out ushort type)
        {
            type = 0;
            if (GameplayActionDatagram.TryParseMove(packet, out var move) &&
                move.Header.SourceSlot == senderSeat && move.SourceEcho == senderSeat)
            {
                type = move.Header.Type;
                return true;
            }
            if (GameplayActionDatagram.TryParseAnimation(packet, out var animation) &&
                animation.Header.SourceSlot == senderSeat && animation.SourceEcho == senderSeat &&
                animation.Kind == PlayerAnimationKind.Attack)
            {
                type = animation.Header.Type;
                return true;
            }
            return false;
        }

        private void ApplyBotInput(ClientSession sender, ushort type, ReadOnlySpan<byte> packet)
        {
            if (type == BotMovement.MoveType &&
                BotMovement.TryReadPose(packet, out BotVector position, out float heading))
            {
                UpdateHumanPose(sender, position, heading);
                return;
            }
            if (type == GameplayActionDatagram.AnimationType &&
                GameplayActionDatagram.TryParseAnimation(packet, out var animation))
                _world.RegisterHumanAnimation(sender, animation);
        }

        private void UpdateHumanPose(ClientSession sender, BotVector position, float heading)
        {
            Field? field = _world.GetField(sender.FieldId);
            if (field == null) return;
            lock (field.SyncRoot)
            {
                PlayerRec? record = field.FindRec(sender);
                if (record == null) return;
                record.Position = position;
                record.Heading = heading;
            }
        }

    }
}
