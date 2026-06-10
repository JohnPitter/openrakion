using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Camada UDP de gameplay do world (portas [UDP] Port1/Port2). Reconstruida do
    /// worldserv.exe: o recv (FUN_004040d0) faz recvfrom de ate 0x4b0 (1200) bytes e
    /// enfileira (FUN_0042e9c0); o send (FUN_00404010) faz sendto ao endpoint do peer.
    /// O gameplay em tempo real (movimento/combate) e RELAYADO: um pacote recebido de
    /// um jogador e reenviado aos demais membros do mesmo field, nos endpoints UDP deles.
    ///
    /// O endpoint UDP de cada jogador e aprendido do primeiro pacote (a sessao e casada
    /// pelo IP de origem). O formato/opcode interno do pacote (parse no consumidor da
    /// fila) e a proxima camada de RE; aqui o relay preserva o pacote intacto, que e o
    /// comportamento essencial do servidor (repassar o estado entre os peers do field).
    /// </summary>
    public sealed class UdpGameplay
    {
        public const int MaxPacket = 0x4b0; // 1200, igual ao recvfrom do binario
        private const byte ClientInputOp0 = 0x00;
        private const byte ClientInputOp1 = 0x40;
        private const byte GameplayFeedbackOp0 = 0x15;
        private const byte GameplayFeedbackOp1 = 0x83;
        private const byte DefaultGameplayState = 0x0a;

        private readonly WorldServer _world;
        private readonly int _port;
        private Socket? _sock;
        private CancellationTokenSource? _cts;
        private readonly byte[] _rx = new byte[2048];

        public UdpGameplay(WorldServer world, int port)
        {
            _world = world;
            _port = port;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _sock.Bind(new IPEndPoint(IPAddress.Any, _port));
            Log.Ok("udp", "gameplay UDP ouvindo na porta {0}", _port);
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
        public void SendTick(IPEndPoint to, byte seq, byte state = DefaultGameplayState)
        {
            byte[] p = { GameplayFeedbackOp0, GameplayFeedbackOp1, seq, 0x00, 0x00, 0x00, 0x00, state };
            try { _sock?.SendTo(p, to); }
            catch (Exception ex) { Log.Debug("udp", "tick {0}: {1}", to, ex.Message); }
        }

        /// <summary>Resolve a sessao remetente de um pacote UDP (por endpoint exato, senao por IP).</summary>
        private ClientSession? ResolveSender(IPEndPoint from)
        {
            foreach (var s in _world.Sessions)
                if (s.UdpEndpoint != null && s.UdpEndpoint.Equals(from)) return s;
            return _world.GetSessionByIp(from.Address.ToString());
        }

        private void Process(IPEndPoint from, byte[] pkt)
        {
            Log.Debug("udp", "RX {0}B de {1}: {2}", pkt.Length, from, Convert.ToHexString(pkt));

            // GAMEPLAY: input do cliente (marker 0x4000, 11B: [0040][cc][00000000][val u32]).
            // ECHO IMEDIATO 1:1 (fiel a captura original): CADA 0040 do cliente -> UM tick 1583 com o
            // MESMO val (pkt[7]), respondido NA HORA. O timer global de 150ms desacoplava o echo do
            // input -> em combos/cargas/troca-de-arma (vals mudando rapido) perdia vals intermediarios
            // e a acao nao COMPLETAVA (combo nao encadeia, carga "comeca e termina rapido", troca de
            // arma so 1x). Movimento ja funcionava; este 1:1 destrava a sequencia das acoes.
            if (pkt.Length >= 2 && pkt[0] == ClientInputOp0 && pkt[1] == ClientInputOp1)
            {
                // DESCOBERTA (frida, world real): o combate solo PvE e' CLIENT-SIDE; o world real NAO ecoa
                // 1583. Ecoar o tick mantinha o cliente em "modo networked" (retransmitindo 0040) -> combate
                // bugava. Agora so CONSUMIMOS o input (sem eco) p/ o cliente cair em client-side.
                if (pkt.Length >= 8)
                {
                    var gs = _world.GetSessionByIp(from.Address.ToString());
                    if (gs != null) gs.LastInput = pkt[7];
                }
                return;
            }
            // FEEDBACK 1583 do cliente (8B): o client ecoa seq/state de gameplay. Antes caia em
            // "pkt curto" e escondia o lockstep de combate/carga. Ainda nao aplicamos regra de
            // negocio aqui; esta trilha e a fonte unica para a futura maquina de estado de acoes.
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
                return;
            }
            // ACK 0x030d do cliente (7B): consome.
            if (pkt.Length >= 2 && pkt[0] == 0x0d && pkt[1] == 0x03) return;

            // ACAO DE CAMPO (combate/objetos): markers 0x0401(0104 destroy/ataque), 0x0203, 0x0305,
            // 0x0304. O world original (FUN_00411760) PROCESSA + BROADCASTA aos OUTROS membros do field
            // via SendData_Unreliable. FUN_00426b30 confirma: o broadcast EXCLUI o proprio sender
            // ("if slot != sender"). O jogador LOCAL preve a acao no cliente; reenviar de volta a ele
            // causa DUPLA-PROCESSACAO -> hit fantasma no inicio + ataques que nao completam + troca de
            // arma bugada numa direcao. Relay so aos OUTROS in-field; em solo (sem peers) nada e' enviado
            // e o cliente preve sozinho (fiel ao original).
            if (pkt.Length >= 2 && pkt[0] == 0x01 && (pkt[1] == 0x04 || pkt[1] == 0x02 || pkt[1] == 0x03))
            {
                // Resolve o SENDER (por endpoint UDP, senao por IP) p/ excluir e escopar ao field dele.
                // (Nao ecoamos a acao de volta ao proprio sender: o cliente PREVE a acao localmente;
                //  reenviar causa DUPLA-PROCESSACAO = hit fantasma. Confirmado: o contador de hits do
                //  HUD nao depende de eco do servidor — o card de Rank do world original nem tem campo
                //  de hits; a nota e' 100% por tempo.)
                var sender = ResolveSender(from);
                int senderField = sender?.FieldId ?? -1;
                int n = 0;
                foreach (var sess in _world.Sessions)
                {
                    if (!sess.InField || sess.UdpEndpoint == null) continue;
                    if (sess == sender) continue;                       // FUN_00426b30: exclui o proprio sender
                    if (sess.UdpEndpoint.Equals(from)) continue;        // fallback de exclusao por endpoint
                    if (senderField >= 0 && sess.FieldId != senderField) continue; // so peers do mesmo field
                    try { _sock!.SendTo(pkt, sess.UdpEndpoint); n++; } catch { }
                }
                Log.Debug("udp", "acao de campo 0x{0:X2}{1:X2} relay p/ {2} outro(s) do field {3} (exclui sender)",
                    pkt[1], pkt[0], n, senderField);
                return;
            }

            // Formato REAL (capturado): [u16 type=0x0202][u8 counter][u16 pad][BODY...].
            // BODY (offset 5): [u16 slot][u32 key][...][u32 echoData @ body+0xc = pkt+17].
            // (FUN_00425d80: *body=slot, *(body+1)=key==user+0x1464; echo data = body[0xc].)
            if (pkt.Length < 21) { Log.Debug("udp", "pkt curto {0}B", pkt.Length); return; }
            ushort slot = BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(5));
            // O 0x0C replayado fixa slot 0 -> o cliente manda slot 0 sempre. Resolve pelo slot;
            // se nao casar com a sessao TCP real (slot incremental), cai pro IP do remetente.
            var s = _world.GetSession(slot) ?? _world.GetSessionByIp(from.Address.ToString());
            if (s == null) { Log.Debug("udp", "[{0}] UDP sem sessao (slot off5 nem IP {1})", slot, from.Address); return; } // UDP 0
            if (!s.Connected || !s.SlotActive) return;                                       // UDP 1/2 (status+slot ativo; NAO exige InField)
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(pkt.AsSpan(7));
            if (s.UdpKey != 0 && key != s.UdpKey) { Log.Debug("udp", "[{0}] UDP key mismatch (got {1:X8} exp {2:X8})", slot, key, s.UdpKey); return; } // UDP 4

            // Registra o endpoint UDP e dispara (1x) a msg TCP 0x10 que destrava a entrada no
            // campo (capturada do world ORIGINAL via MITM). Substitui o "msg5" que era errado.
            s.NotifyUdpReady(from);  // registra endpoint UDP (FUN_0040ab90)
            // echoData = ULTIMOS 4 bytes do ping (offset 19-22), nao offset 17. O world ecoa esse
            // valor de volta (confirmado na captura: echo data == ping[19:23]).
            uint echoData = BinaryPrimitives.ReadUInt32LittleEndian(pkt.AsSpan(19));

            // eco 0x0201 (FUN_00425fa0 = PORT2): [u16 0x0201][u32 echoData][u8 1][u8 1][u32 echoData]
            // result byte = 1 (port2/40709) -> OnRecvSuccessUDP registra o 2o endpoint.
            byte[] echo = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(echo.AsSpan(0), 0x201);
            BinaryPrimitives.WriteUInt32LittleEndian(echo.AsSpan(2), echoData);
            echo[6] = 1; echo[7] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(echo.AsSpan(8), echoData);
            try { _sock!.SendTo(echo, from); Log.Info("udp", "[{0}] echo 0x0201 R=1 (port2) -> {1}", slot, from); }
            catch (Exception ex) { Log.Debug("udp", "echo {0}: {1}", from, ex.Message); }
        }
    }
}
