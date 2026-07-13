using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.Peer;

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
        private const byte ReliableAckOp0 = 0x00;
        private const byte ReliableAckOp1 = 0x40;
        private const byte GameplayFeedbackOp0 = 0x15;
        private const byte GameplayFeedbackOp1 = 0x83;
        private const byte DefaultGameplayState = 0x0a;

        /// <summary>Base das portas UDP dedicadas dos bots (<c>WorldServer.BotPeer._botPortSeq</c> = 41000; bots
        /// bindam 41001+). No loopback bot e humano compartilham 127.0.0.1; usar isto p/ NÃO deixar um pacote de
        /// bot resolver como cliente real e sobrescrever o <c>UdpEndpoint</c> do humano.</summary>
        private const int BotUdpPortBase = 41000;

        private readonly WorldServer _world;
        private readonly int _port;
        public int Port => _port;
        private Socket? _sock;
        private CancellationTokenSource? _cts;
        private readonly byte[] _rx = new byte[2048];

        // Endpoint-handshake 0x319: throttle de re-registro por (endpoint do humano, slot do bot). O cliente
        // grava o endpoint UMA vez, mas re-enviamos a cada RegisterTtlMs p/ robustez (re-spawn/round/re-join).
        private const long RegisterTtlMs = 1500;
        private readonly System.Collections.Generic.Dictionary<string, long> _botEpReg = new();
        private uint _regSeq;

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

        /// <summary>Funil ÚNICO de TX do socket de gameplay: todo datagrama enviado passa aqui
        /// (wire-tap de captura + erro em Debug). Devolve false se o envio falhou.</summary>
        private bool Send(byte[] pkt, IPEndPoint to)
        {
            if (_sock == null) return false;
            try { _sock.SendTo(pkt, to); }
            catch (Exception ex) { Log.Debug("udp", "tx {0}: {1}", to, ex.Message); return false; }
            return true;
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
            Send(p, to);
        }

        /// <summary>Envia um datagrama de gameplay já montado ao endpoint de um peer (usado pelo bot, que
        /// SINTETIZA o pacote de move/ação 0x30a — não há cliente p/ enviá-lo). Não é relay: o pacote é
        /// construído do estado do bot.</summary>
        public void SendRaw(IPEndPoint to, byte[] pkt) => Send(pkt, to);

        /// <summary>
        /// RELAY de pacote do bot para todos os jogadores humanos de um field. Espelha o comportamento
        /// do worldserv original (FUN_00411760): recebe o pacote sintetizado pelo bot e reenvia aos
        /// endpoints UDP de todos os humanos no mesmo field. O host recebe do socket do servidor (mesma
        /// fonte que pacotes relayed de outros jogadores), então o engine processa como gameplay válido.
        /// </summary>
        public void RelayToField(int fieldId, byte[] pkt)
        {
            if (_sock == null) return;
            int n = 0;
            foreach (var sess in _world.Sessions)
            {
                if (sess.UdpEndpoint == null || (!sess.InField && sess.Status != 3)) continue; // Status 3 = stage (UserStatus.InField): robusto a relog que zera o bool InField
                if (sess.FieldId != fieldId) continue;
                if (Send(pkt, sess.UdpEndpoint)) n++;
            }
            if (n > 0) Log.Debug("udp", "relay bot -> {0} humano(s) field {1} ({2}B)", n, fieldId, pkt.Length);
        }

        /// <summary>
        /// Relay de pacote de fonte desconhecida (bot) para TODOS os humanos em fields com bots.
        /// O bot envia 0x30a do seu socket dedicado; o servidor recebe e repassa ao host pelo socket
        /// do servidor — o mesmo canal que o relay original do worldserv.
        /// </summary>
        private int RelayToAllFields(byte[] pkt, IPEndPoint from)
        {
            if (_sock == null) return 0;
            // slot do bot = srcSlot do header (offset 6) dos 0x30a/0x030f — usado p/ registrar o endpoint.
            byte botSlot = pkt.Length > 6 ? pkt[6] : (byte)0xff;
            // Canal de lockstep 0x0304 (open/push) é PAREADO: entrega SÓ ao humano do dstSeat (byte 7 — no
            // 0x0305 os bytes 6/7 são do ACKER, não roteáveis). Broadcast cruzava os canais (o master recebia
            // o open endereçado ao joiner e vice-versa) — entre 2 humanos o relay natural nunca faz isso
            // (exclui o sender e sobra só o par certo).
            bool paired = pkt.Length >= 12 && pkt[1] == 0x03 && pkt[0] == 0x04;
            byte dstSeat = paired ? pkt[7] : (byte)0xff;
            int n = 0;
            foreach (var sess in _world.Sessions)
            {
                if (sess.UdpEndpoint == null || (!sess.InField && sess.Status != 3)) continue; // Status 3 = stage (UserStatus.InField): robusto a relog que zera o bool InField
                if (sess.UdpEndpoint.Equals(from)) continue;   // não ecoa ao sender
                var f = _world.GetField(sess.FieldId);
                if (f == null || f.BotCount == 0) continue;    // só fields com bots
                if (paired && f.FindRec(sess)?.Slot != dstSeat) continue;   // canal pareado: só o par certo
                // Registra o endpoint do bot (0x319) no cliente ANTES do 0x30a: faz o cliente gravar o socket do
                // servidor como endpoint do slot do bot, destravando o gate de movimento (IsValidUDP_ForPlayer).
                // SÓ p/ seat de BOT: registrar um seat de HUMANO redirecionaria o canal P2P-direto do humano p/ o
                // servidor (o sequestro que matava o HIT×N — wiretap 2026-07-10).
                if (botSlot != 0xff && f.RecAt(botSlot)?.IsBot == true) EnsureBotEndpointRegistered(sess.UdpEndpoint, botSlot);
                if (Send(pkt, sess.UdpEndpoint)) n++;
            }
            if (n == 0) Log.Debug("udp", "RELAY bot: 0 humanos em fields com bots ({0}B)", pkt.Length);
            return n;
        }

        /// <summary>
        /// Relay dos datagramas do AVATAR NPC/CELL do BOT (0x307/0x8307 create, 0x30b move) aos humanos. O CREATE
        /// reliable (0x8307) passa por <c>IsApplyReliableUDP@0x36109e20</c>, cujo 1º gate (<c>IsValidUDP_ForPlayer</c>)
        /// exige o endpoint do remetente gravado em <c>playerRec[seat]+0x1e8/+0x1ec</c> — por isso REGISTRAMOS o
        /// 0x319 do seat-dono ANTES (a RE mostrou que pular isso derrubava o create; docs/hitbox-re-findings.md §VIRADA).
        ///
        /// SÓ NPC DE BOT: a Cell de um HUMANO corre P2P-DIRETO (wiretap 2026-07-11) — o outro humano já a recebe
        /// direto. Relayá-la aqui (a) dupla-entregava e (b) — pior — registrava o 0x319 do SEAT DO HUMANO no outro,
        /// RE-SEQUESTRANDO o canal P2P-direto deles (quebraria o HIT×N humano↔humano toda vez que alguém summonasse
        /// uma Cell). Só o NPC do bot (sem cliente) precisa do relay; o do humano é capturado (wiretap) e ignorado.
        /// </summary>
        private void RelayNpcToFields(byte[] pkt, IPEndPoint from)
        {
            if (_sock == null) return;
            if (from.Port < BotUdpPortBase) return;   // NPC de HUMANO = P2P-direto: nunca relayar/registrar (anti-sequestro)
            // owner (seat do dono do NPC) mora em offsets DIFERENTES: no create 0x8307 = srcSlot @ pkt[6]; no move
            // 0x030b = corpo+3 @ pkt[9] (após [op][seq][s16 A][u8 type]). Ler pkt[6] pro move pegava o campo errado.
            bool isReliableCreate = pkt.Length > 6 && pkt[0] == 0x07 && pkt[1] == 0x83;   // 0x8307
            byte ownerSeat = isReliableCreate ? pkt[6] : (pkt.Length > 9 ? pkt[9] : (byte)0xff);
            int n = 0;
            foreach (var sess in _world.Sessions)
            {
                if (sess.UdpEndpoint == null || (!sess.InField && sess.Status != 3)) continue; // Status 3 = stage (UserStatus.InField): robusto a relog que zera o bool InField
                if (sess.UdpEndpoint.Equals(from)) continue;   // não ecoa ao sender
                var f = _world.GetField(sess.FieldId);
                if (f == null) continue;   // do bot (from.Port guard) -> relaya a todos os humanos in-stage
                // Create reliable: grava o endpoint do servidor como playerRec[ownerSeat=BOT] no host (0x319) ANTES,
                // senão o gate 1 do IsApplyReliableUDP descarta o 0x8307 (RE do Muro 2). ownerSeat é de BOT (from bot).
                if (isReliableCreate && ownerSeat != 0xff) EnsureBotEndpointRegistered(sess.UdpEndpoint, ownerSeat);
                if (Send(pkt, sess.UdpEndpoint)) n++;
            }
            if (n > 0) Log.Ok("udp", "RELAY NPC{0} -> {1} humano(s) ({2}B)", isReliableCreate ? " create(reliable+0x319)" : "", n, pkt.Length);
            else Log.Debug("udp", "RELAY NPC: 0 humanos no field ({0}B)", pkt.Length);
        }

        /// <summary>
        /// Garante (com throttle) que o cliente <paramref name="humanEp"/> tem o endpoint do servidor gravado como
        /// o do <paramref name="slot"/> do bot, via o handshake 0x319 (<see cref="BotMovement.BuildPeerRegister"/>).
        /// Enviado do _sock — a MESMA origem que relaya os 0x30a — para que o (IP,porta) gravado bata no check de
        /// movimento. Re-envia a cada <see cref="RegisterTtlMs"/> (robustez a re-spawn/round).
        /// </summary>
        private void EnsureBotEndpointRegistered(IPEndPoint humanEp, byte slot)
        {
            if (_sock == null) return;
            string key = humanEp + "|" + slot;
            long now = Environment.TickCount64;
            if (_botEpReg.TryGetValue(key, out long last) && now - last < RegisterTtlMs) return;
            _botEpReg[key] = now;
            if (!Send(BotMovement.BuildPeerRegister(slot, _regSeq++), humanEp)) return;
            Log.Ok("udp", "0x319 endpoint-register bot slot {0} -> {1} (destrava movimento)", slot, humanEp);
        }

        /// <summary>Resolve a sessao remetente de um pacote UDP (por endpoint exato, senao por IP).</summary>
        private ClientSession? ResolveSender(IPEndPoint from)
        {
            foreach (var s in _world.Sessions)
                if (s.UdpEndpoint != null && s.UdpEndpoint.Equals(from)) return s;
            return _world.GetSessionByIp(from.Address.ToString());
        }

        /// <summary>Resolve a sessao de um PING de handshake UDP. O ping manda slot=0 FIXO -> GetSession(0) daria o
        /// host p/ TODOS os clientes (2 na mesma maquina colidem). Resolve por: (1) endpoint ja aprendido; (2) a
        /// sessao que ainda ESPERA endpoint (UdpEndpoint==null) — prefere a de GameInfoId==key (id ecoado no ping),
        /// senao a 1a conectada+ativa sem endpoint; (3) fallback GetSession(slot)/IP.</summary>
        private ClientSession? ResolveUdpHandshake(IPEndPoint from, ushort slot, uint key)
        {
            foreach (var s in _world.Sessions)
                if (s.UdpEndpoint != null && s.UdpEndpoint.Equals(from)) return s;   // ja aprendido (pings seguintes)
            ClientSession? waiting = null;
            foreach (var s in _world.Sessions)
            {
                if (!s.Connected || !s.SlotActive || s.UdpEndpoint != null) continue;
                if (key != 0 && (uint)s.GameInfoId == key) return s;                 // match forte: id de sessao ecoado
                waiting ??= s;                                                       // 1a sessao esperando endpoint
            }
            if (waiting != null) return waiting;
            return _world.GetSession(slot) ?? _world.GetSessionByIp(from.Address.ToString());  // solo/legado
        }

        private readonly HashSet<(int FieldId, byte HumanSeat, byte BotSeat, uint Sequence)> _reliableAcks = new();

        private void Process(IPEndPoint from, byte[] pkt)
        {
            Log.Debug("udp", "RX {0}B de {1}: {2}", pkt.Length, from, Convert.ToHexString(pkt));

            // ACK de aplicação reliable: [0040][seq do confirmador u32][seat][seq confirmada u32].
            // A classificação antiga como "input" gravava o low-byte da sequência confirmada em LastInput.
            if (pkt.Length == 11 && pkt[0] == ReliableAckOp0 && pkt[1] == ReliableAckOp1)
            {
                return;
            }
            AckReliableFromHuman(pkt, from);
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

            // LOCKSTEP de sessão P2P (0x0304 open/push, 0x0305 ack) — dialeto REAL da captura de 2 humanos
            // (docs/p2p-handshake-groundtruth.txt l.12-23; codec BotLockstep). TODO o P2P transita pelo
            // servidor, então o servidor é o HUB do canal:
            //  - push p/ um BOT   -> ACKeia no lugar dele (formato do joiner: bytes 6/7 = seat do ACKER);
            //  - push do BOT       -> entrega aos humanos do field;
            //  - tráfego humano↔humano permanece P2P direto e é consumido se chegar ao servidor.
            if (pkt.Length >= 2 && pkt[1] == 0x03 && (pkt[0] == 0x04 || pkt[0] == 0x05 || pkt[0] == 0x19))
            {
                if (from.Port >= BotUdpPortBase)
                {
                    RelayToAllFields(pkt, from);
                    return;
                }
                var ls = ResolveSender(from);
                var lf = ls != null ? _world.GetField(ls.FieldId) : null;
                byte dst = pkt.Length >= 8 ? BotLockstep.DstSeat(pkt) : (byte)0xff;
                if (BotLockstep.IsPush(pkt) && lf != null)
                {
                    var dstRec = lf.RecAt(dst);
                    if (dstRec?.IsBot == true)
                    {
                        Send(BotLockstep.BuildAck(pkt, dst), from);
                        return;
                    }
                    // dst = OUTRO HUMANO: DROP. O canal reliable humano↔humano corre P2P-DIRETO (wiretap: sem bot,
                    // zero tráfego no servidor). Relayá-lo + registrar o servidor como peer do pusher (0x319) era o
                    // SEQUESTRO — redirecionava o canal mútuo dos humanos p/ o servidor, que não preserva o seq/ack
                    // reliable → 0x8312/0x830C nunca fechavam. Só chega aqui se o humano já foi sequestrado num round
                    // anterior; ignorar deixa o P2P-direto reassumir.
                    return;
                }
                if (pkt[0] == 0x05 && pkt.Length >= 12 && lf != null)
                {
                    // ACK do humano ao push do BOT: consumido (o push do bot já foi ackeado por nós; este é o
                    // reconhecimento do humano, fim da linha). ACK humano→humano corre P2P-direto — se chegar aqui
                    // é resíduo de sequestro; consumir (não relayar). Fecha o canal reliable humano↔humano no P2P.
                    return;
                }
                return;
            }

            // AVATAR NPC do bot (0x307 CreateNpc / 0x30b move), RELIABLE (0x83xx) OU unreliable (0x03xx): SEMPRE
            // do bot -> relay ao host. Não passa pela desambiguação por slot do bloco abaixo (o 1º byte do corpo
            // é owner/A, não um player slot) nem pelo 0x319 (o NPC tem chave própria na tabela do host).
            // FIX 2026-07-01: o create do bot (e o da Cell real) vai RELIABLE (0x8307). Sem casar pkt[1]==0x83
            // aqui, ele NÃO batia neste bloco nem em IsReliableFrame -> caía no ping-path (~l.420) e era consumido
            // como ping falso (slot = bytes[5..6] = 0x0A00 = 2560) -> NUNCA relayado -> Golem nunca renderizava.
            if (pkt.Length >= 2 && (pkt[1] == 0x03 || pkt[1] == 0x83) && (pkt[0] == 0x07 || pkt[0] == 0x0b))
            {
                RelayNpcToFields(pkt, from);
                return;
            }

            // NETCODE da SESSÃO P2P da engine: TODA a família de gameplay 0x03xx/0x83xx (0x83xx = 0x03xx com o
            // bit de entrega reliable) é RELAY INTACTO — o papel do worldserv original. A whitelist antiga
            // (0x30a/0x30f/0x830c/0x8313) DROPAVA o resto no parse de ping: o 0x8312 (estado/time do player
            // novo, emitido quando um 3º peer registra) nunca chegava ao outro humano -> nunca ackeado ->
            // retransmitido 1x/s p/ sempre -> a ativação de combate não completava ("ninguém se hita" com o
            // bot presente; sem bot o 0x8312 nem existe). Exceções tratadas ANTES deste bloco:
            // input 0x0040, feedback 1583 (0x8315 8B), 0x030d, lockstep 0x0304/0x0305/0x0319, NPC 0x07/0x0b.
            // O 0x0311 (ataque) fica FORA (pkt[0] != 0x11): tem arbitragem própria no bloco abaixo.
            if (pkt.Length >= 7 && (pkt[1] == 0x03 || pkt[1] == 0x83) && pkt[0] != 0x11)
            {
                var sender = ResolveSender(from);
                var sf = sender != null ? _world.GetField(sender.FieldId) : null;
                // LOOPBACK FIX: bot e humano são AMBOS 127.0.0.1, então ResolveSender (fallback por IP) atribui os
                // pacotes do BOT ao humano. Desambigua pelo srcSlot do datagrama (offset 6): se o dono é um BOT no
                // field, é o pacote do BOT (a relayar), mesmo que o IP tenha resolvido o humano.
                byte srcSlot = pkt.Length > 6 ? pkt[6] : (byte)0xff;
                bool isBotPacket = sf != null && srcSlot != 0xff && sf.RecAt(srcSlot)?.IsBot == true;
                if (sender != null && !isBotPacket)
                {
                    // 0x30a do humano: extrai posição. Datagrama 26B = [u16 msg][u32 seq][u8 slot][corpo 19B];
                    // corpo: dt@7, actState@9, e a POSIÇÃO x/y/z (packed *0.01) em 11/13/15 — NÃO em 7/9/11.
                    if (pkt[0] == 0x0a && pkt.Length >= 17 && sf != null)
                    {
                        var rec = sf.FindRec(sender);
                        if (rec != null && !rec.IsBot)
                        {
                            const float scale = 0.01f;
                            float nx = BinaryPrimitives.ReadInt16LittleEndian(pkt.AsSpan(11)) * scale;
                            float ny = BinaryPrimitives.ReadInt16LittleEndian(pkt.AsSpan(13)) * scale;
                            float nz = BinaryPrimitives.ReadInt16LittleEndian(pkt.AsSpan(15)) * scale;
                            long nowMs = Environment.TickCount64;
                            // velocidade do alvo (coord/ms, EMA p/ amortecer ruído do 0x30a a 10 Hz) — antecipação da IA
                            if (rec.LastPositionMs != 0)
                            {
                                float dt = nowMs - rec.LastPositionMs;
                                if (dt > 0f && dt < 500f)
                                {
                                    rec.VelX = 0.5f * rec.VelX + 0.5f * (nx - rec.LastX) / dt;
                                    rec.VelZ = 0.5f * rec.VelZ + 0.5f * (nz - rec.LastZ) / dt;
                                }
                            }
                            rec.LastX = nx; rec.LastY = ny; rec.LastZ = nz;
                            if (pkt.Length >= 19) rec.LastHeading = BinaryPrimitives.ReadInt16LittleEndian(pkt.AsSpan(17)); // +0a do corpo = heading (graus)
                            rec.LastPositionMs = nowMs;
                            // RE-SYNC client-authoritative: um humano em death-cam PARA de mandar 0x30a; se ele volta
                            // a se mover durante o round, RESPAWNOU no cliente. Restaura State=4 (o servidor o marcara
                            // morto — ex.: golpe do bot no passado, ou 0x4f não reconciliado). Sem isto o servidor
                            // ignorava os golpes dele o resto do round ("golpe IGNORADO playing=False"). Ver Field.cs §hit.
                            if (sf.Phase == Domain.MatchPhase.Playing && rec.State == 1) { rec.State = 4; rec.Dead = false; }
                            sf.ExpandHumanHull(nx, nz);   // chão VÁLIDO empírico p/ o clamp do bot (onde o humano pisou)
                        }
                    }
                    // NÃO relaya humano->humano. O WIRETAP (2026-07-10) provou que 2 clientes na mesma máquina
                    // falam P2P DIRETO (partida sem bot = ZERO tráfego no servidor, HIT×N ok). Este pacote é a
                    // CÓPIA que o humano manda AO BOT (bot registrado no servidor via 0x319); consumimos p/ a IA
                    // (posição extraída acima) e PARAMOS. Relayá-lo ao outro humano ENTREGAVA EM DOBRO (o outro já
                    // recebe P2P-direto) — a conta do fio batia exata (tx_p/_A = rx_de_B + rx_do_bot) — e a dupla
                    // entrega do estado reliable (0x830C/0x8312) impedia o seq/ack de fechar → HIT×N morto p/ todos.
                    return;
                }
                // Pacote do BOT (srcSlot=bot) OU fonte realmente desconhecida -> RELAY aos humanos no field
                // (do _sock do servidor — mesma origem que o 0x319 registra, p/ passar no IsValidUDP_ForPlayer).
                RelayToAllFields(pkt, from);
                return;
            }

            // 0x0311 ATAQUE (10B): [u16 0x0311][u32 seq][u8 srcSlot][u8 sub][u16 actionId]. O dono é o srcSlot
            // (offset 6) — NÃO o IP (no loopback bot e humano colidem em 127.0.0.1). Dois casos:
            //  - srcSlot = BOT: é o ataque do BOT -> RELAY aos humanos (o cliente do alvo prevê o próprio hit/morte,
            //    modelo cliente-autoritativo). NÃO arbitrar, senão o bot se auto-mata (bug do loopback).
            //  - srcSlot = HUMANO: o servidor ARBITRA o hit nos BOTS inimigos (o bot não tem cliente p/ reportar a
            //    própria morte) via Field.ResolveBotHitByHuman e, na morte, broadcasta o 0x4f sintetizado.
            if (pkt.Length >= 10 && pkt[0] == 0x11 && pkt[1] == 0x03)
            {
                var attacker = ResolveSender(from);
                var f = attacker != null ? _world.GetField(attacker.FieldId) : null;
                byte srcSlot = pkt[6];
                if (f != null && f.RecAt(srcSlot)?.IsBot == true)
                {
                    RelayToAllFields(pkt, from);
                    return;
                }
                var arec = f != null ? f.FindRec(attacker!) : null;
                if (f != null && arec != null && !arec.IsBot)
                {
                    ushort actionId = BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(8));
                    // ARBITRA o hit nos BOTS inimigos (só o bot não tem cliente p/ reportar a própria morte).
                    if (f.BotCount > 0)
                    {
                        int? killed = f.ResolveBotHitByHuman(arec.Slot, actionId, out _);
                        if (killed is int botSeat)
                        {
                            f.BroadcastField(0x4f,
                                new byte[] { (byte)botSeat, 0, (byte)arec.Slot, f.Score0, f.Score1 }, attacker);
                        }
                    }
                    // NÃO relaya o ataque humano->humano: o outro humano já recebe o 0x0311 P2P-direto (wiretap).
                    // Relayar duplicava o golpe no cliente da vítima. Aqui só arbitramos vs os BOTS (sem cliente).
                }
                return;
            }

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
                foreach (var sess in _world.Sessions)
                {
                    if (sess.UdpEndpoint == null || (!sess.InField && sess.Status != 3)) continue; // Status 3 = stage (UserStatus.InField): robusto a relog que zera o bool InField
                    if (sess == sender) continue;                       // FUN_00426b30: exclui o proprio sender
                    if (sess.UdpEndpoint.Equals(from)) continue;        // fallback de exclusao por endpoint
                    if (senderField >= 0 && sess.FieldId != senderField) continue; // so peers do mesmo field
                    Send(pkt, sess.UdpEndpoint);
                }
                // O melee do humano chega por 0x0311 (confirmado no log: 0 pacotes 0x0401), arbitrado lá;
                // este bloco só RELAYA a ação de campo aos outros peers (sem arbitrar = sem caminho duplo).
                return;
            }

            // Formato REAL (capturado): [u16 type=0x0202][u8 counter][u16 pad][BODY...].
            // BODY (offset 5): [u16 slot][u32 key][...][u32 echoData @ body+0xc = pkt+17].
            // (FUN_00425d80: *body=slot, *(body+1)=key==user+0x1464; echo data = body[0xc].)
            // >= 23: o handler lê echoData em [19..22] (ReadUInt32 @19). Frames reliable curtos (21-22B) caíam aqui
            // e estouravam IndexOutOfRange no AsSpan(19) — input externo nunca pode lançar (CLAUDE.md).
            if (pkt.Length < 23) return;
            ushort slot = BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(5));
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(pkt.AsSpan(7));
            // 2 CLIENTES NA MESMA MAQUINA: o cliente manda slot=0 FIXO (o 0x0C fixa slot 0), entao GetSession(0)
            // daria o HOST p/ os DOIS -> o host ficava com o endpoint do ultimo ping e o 2o cliente nunca recebia
            // endpoint (udp=-). O cliente JA resolve o conflito de PORTA sozinho (retry no bind, engine.dll
            // 0x360ff826) -> cada um tem porta UNICA. Resolve o ping pela sessao que ainda ESPERA endpoint.
            var s = ResolveUdpHandshake(from, slot, key);
            if (s == null) { Log.Debug("udp", "[{0}] UDP sem sessao (slot off5 nem IP {1})", slot, from.Address); return; } // UDP 0
            if (!s.Connected || !s.SlotActive) return;                                       // UDP 1/2 (status+slot ativo; NAO exige InField)
            if (s.UdpKey != 0 && key != s.UdpKey) { Log.Debug("udp", "[{0}] UDP key mismatch (got {1:X8} exp {2:X8})", slot, key, s.UdpKey); return; } // UDP 4
            if (s.UdpEndpoint == null)
            {
                Log.Ok("udp", "[{0}] handshake UDP: endpoint {1} (key={2:X8} gameId={3})", s.Slot, from, key, s.GameInfoId);
            }

            // Registra o endpoint UDP e dispara (1x) a msg TCP 0x10 que destrava a entrada no
            // campo (capturada do world ORIGINAL via MITM). Substitui o "msg5" que era errado.
            // LOOPBACK FIX (2026-07-01): um ping vindo de uma porta de BOT (41xxx) resolvia à sessão do humano
            // por IP (GetSessionByIp em 127.0.0.1) e SOBRESCREVIA o UdpEndpoint dele com a porta do bot — daí o
            // relay pulava o humano em `UdpEndpoint.Equals(from)` (achava que ele era o sender) e o create/move
            // do bot NUNCA chegava. Só registra endpoint de porta de cliente real (< base de porta de bot).
            if (from.Port < BotUdpPortBase) s.NotifyUdpReady(from);  // registra endpoint UDP (FUN_0040ab90)
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
            if (Send(echo, from)) Log.Info("udp", "[{0}] echo 0x0201 R=1 (port2) -> {1}", slot, from);
        }

        private void AckReliableFromHuman(byte[] packet, IPEndPoint from)
        {
            if (packet.Length < 7 || packet[1] != 0x83 || from.Port >= BotUdpPortBase) return;
            if (packet.Length == 8 && packet[0] == GameplayFeedbackOp0) return;

            var sender = ResolveSender(from);
            var field = sender != null ? _world.GetField(sender.FieldId) : null;
            byte sourceSeat = packet[6];
            if (sender == null || field == null || field.RecAt(sourceSeat)?.IsBot == true) return;

            uint confirmed = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(2));
            AckReliableForBots(field, sender.FieldSeat, confirmed, from);
        }

        private int AckReliableForBots(Domain.Field field, byte humanSeat, uint confirmedSequence, IPEndPoint to)
        {
            int sent = 0;
            foreach (var rec in field.BotRecs())
            {
                if (rec.Bot == null || rec.State == 0) continue;
                var key = (field.Id, humanSeat, (byte)rec.Slot, confirmedSequence);
                if (!_reliableAcks.Add(key)) continue;
                if (_reliableAcks.Count > 8192) _reliableAcks.Clear();
                if (Send(_world.Bots.BuildReliableAck(rec.Bot, rec.Slot, confirmedSequence), to)) sent++;
            }
            return sent;
        }
    }
}
