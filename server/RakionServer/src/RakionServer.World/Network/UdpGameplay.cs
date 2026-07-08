using System;
using System.Buffers.Binary;
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
        private const byte ClientInputOp0 = 0x00;
        private const byte ClientInputOp1 = 0x40;
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

        /// <summary>Envia um datagrama de gameplay já montado ao endpoint de um peer (usado pelo bot, que
        /// SINTETIZA o pacote de move/ação 0x30a — não há cliente p/ enviá-lo). Não é relay: o pacote é
        /// construído do estado do bot.</summary>
        public void SendRaw(IPEndPoint to, byte[] pkt)
        {
            try { _sock?.SendTo(pkt, to); }
            catch (Exception ex) { Log.Debug("udp", "sendraw {0}: {1}", to, ex.Message); }
        }

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
                try { _sock.SendTo(pkt, sess.UdpEndpoint); n++; } catch { }
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
                if (botSlot != 0xff) EnsureBotEndpointRegistered(sess.UdpEndpoint, botSlot);
                try { _sock.SendTo(pkt, sess.UdpEndpoint); n++; } catch { }
            }
            if (n == 0) Log.Debug("udp", "RELAY bot: 0 humanos em fields com bots ({0}B)", pkt.Length);
            return n;
        }

        /// <summary>
        /// Relay dos datagramas do AVATAR NPC do bot (0x307/0x8307 create, 0x30b move) aos humanos em fields com
        /// bots. O CREATE reliable (0x8307) passa por <c>IsApplyReliableUDP@0x36109e20</c>, cujo 1º gate
        /// (<c>IsValidUDP_ForPlayer</c>) exige o endpoint do remetente gravado em <c>playerRec[seat]+0x1e8/+0x1ec</c>
        /// — por isso REGISTRAMOS o 0x319 do seat-dono ANTES do relay (a RE mostrou que pular isso derrubava o
        /// create; ver docs/hitbox-re-findings.md §VIRADA). O move 0x30b unreliable é endereçado por owner*9+sub
        /// na tabela do host e não precisa do gate.
        /// </summary>
        private void RelayNpcToFields(byte[] pkt, IPEndPoint from)
        {
            if (_sock == null) return;
            bool isReliableCreate = pkt.Length > 6 && pkt[0] == 0x07 && pkt[1] == 0x83;   // 0x8307
            byte ownerSeat = pkt.Length > 6 ? pkt[6] : (byte)0xff;
            int n = 0;
            foreach (var sess in _world.Sessions)
            {
                if (sess.UdpEndpoint == null || (!sess.InField && sess.Status != 3)) continue; // Status 3 = stage (UserStatus.InField): robusto a relog que zera o bool InField
                if (sess.UdpEndpoint.Equals(from)) continue;   // não ecoa ao sender
                var f = _world.GetField(sess.FieldId);
                if (f == null) continue;   // relaya NPC a TODOS no field — a Cell de um humano tem de aparecer pros outros.
                // Create reliable: grava o endpoint do servidor como playerRec[ownerSeat] no host (0x319) ANTES,
                // senão o gate 1 do IsApplyReliableUDP descarta o 0x8307 (RE do Muro 2).
                if (isReliableCreate && ownerSeat != 0xff) EnsureBotEndpointRegistered(sess.UdpEndpoint, ownerSeat);
                try { _sock.SendTo(pkt, sess.UdpEndpoint); n++; } catch { }
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
            try { _sock.SendTo(BotMovement.BuildPeerRegister(slot, _regSeq++), humanEp); }
            catch (Exception ex) { Log.Debug("udp", "0x319 reg {0} slot {1}: {2}", humanEp, slot, ex.Message); return; }
            Log.Ok("udp", "0x319 endpoint-register bot slot {0} -> {1} (destrava movimento)", slot, humanEp);
        }

        /// <summary>Relay HUMANO->HUMANO: manda o pacote de gameplay (0x30a/0x30f/reliable) do <paramref name="sender"/>
        /// aos OUTROS humanos do MESMO field, registrando (0x319) o endpoint do servidor como peer do sender em cada
        /// receptor (senão o 0x30a relayado não passa em IsValidUDP_ForPlayer). Do _sock do servidor — mesma origem do
        /// register. 2 clientes na mesma máquina: cada um vê o movimento do outro.</summary>
        private int RelayHumanToOthers(byte[] pkt, ClientSession sender, Domain.Field? field)
        {
            if (_sock == null || field == null) return 0;
            byte seat = sender.FieldSeat;
            if (seat >= 0x14) return 0;
            int n = 0;
            foreach (var s in _world.Sessions)
            {
                if (s == sender || s.UdpEndpoint == null || s.FieldId != field.Id) continue;
                if (!s.InField && s.Status != 3) continue;
                EnsureBotEndpointRegistered(s.UdpEndpoint, seat);   // server = peer[seat] no receptor -> passa o filtro
                try { _sock.SendTo(pkt, s.UdpEndpoint); n++; } catch { }
            }
            return n;
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

        /// <summary>Nome legível do frame p/ o rastro de jornada (só diagnóstico).</summary>
        private static string FrameName(ushort t) => t switch
        {
            0x4000 => "INPUT", 0x030d => "KEEPALIVE", 0x8315 => "FB1583", 0x030a => "MOVE", 0x030f => "KEYSTATE",
            0x0304 => "LS-PUSH", 0x0305 => "LS-ACK", 0x0319 => "PEER-REG", 0x0311 => "ATAQUE",
            0x830c => "ALIVE/ANCHOR", 0x8312 => "PEER-STATE", 0x8313 => "CH-CTRL", 0x8307 => "NPC-CREATE",
            0x030b => "NPC-MOVE", 0x0104 => "ACAO-CAMPO", 0x0204 => "ACAO-CAMPO",
            _ => "?"
        };

        /// <summary>Frame de alta frequência (10 Hz+) — rastro em DEBUG p/ não afogar o log; o resto vai em OK.</summary>
        private static bool IsChatty(ushort t) => t is 0x030a or 0x030f or 0x4000 or 0x8315 or 0x030d;

        /// <summary>Rastro de jornada de UM datagrama: tipo + origem + VEREDITO de roteamento. Combat-relevante
        /// em OK; alta-frequência em DEBUG. É a fonte única p/ diagnosticar "por onde o pacote foi".</summary>
        private void Journey(ushort type, IPEndPoint from, string verdict)
        {
            bool bot = from.Port >= BotUdpPortBase;
            if (IsChatty(type)) Log.Debug("jornada", "0x{0:X4} {1} {2} {3}B :: {4}", type, FrameName(type), bot ? "BOT" : "hum", 0, verdict);
            else Log.Ok("jornada", "0x{0:X4} {1} de {2} ({3}) :: {4}", type, FrameName(type), from, bot ? "BOT" : "humano", verdict);
        }

        private void Process(IPEndPoint from, byte[] pkt)
        {
            Log.Debug("udp", "RX {0}B de {1}: {2}", pkt.Length, from, Convert.ToHexString(pkt));
            ushort _jt = pkt.Length >= 2 ? (ushort)(pkt[0] | (pkt[1] << 8)) : (ushort)0;

            // GAMEPLAY: input do cliente (marker 0x4000, 11B: [0040][cc][00000000][val u32]).
            // ECHO IMEDIATO 1:1 (fiel a captura original): CADA 0040 do cliente -> UM tick 1583 com o
            // MESMO val (pkt[7]), respondido NA HORA. O timer global de 150ms desacoplava o echo do
            // input -> em combos/cargas/troca-de-arma (vals mudando rapido) perdia vals intermediarios
            // e a acao nao COMPLETAVA (combo nao encadeia, carga "comeca e termina rapido", troca de
            // arma so 1x). Movimento ja funcionava; este 1:1 destrava a sequencia das acoes.
            if (pkt.Length >= 2 && pkt[0] == ClientInputOp0 && pkt[1] == ClientInputOp1)
            {
                // DESCOBERTA (frida, world real): o combate solo PvE e' CLIENT-SIDE; o world real NAO ecoa
                // 1583 no stage — so CONSUMIMOS o input. Nas salas BATTLE/PvP (Mode != 0) o relogio 1583 e'
                // DIRIGIDO PELO TIMER do motor (WorldServer, ~150ms, GameSeq INCREMENTANDO — seq fixo congela
                // o personagem). NAO ecoar 1:1 aqui: eco devolve seq fixo (=val) e vira tempestade de 2ms.
                if (pkt.Length >= 8)
                {
                    var gs = _world.GetSessionByIp(from.Address.ToString());
                    if (gs != null) gs.LastInput = pkt[7];
                }
                Journey(_jt, from, "CONSUMIDO (input do cliente; sem relay — original tbm consome)");
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
                Journey(_jt, from, "CONSUMIDO (feedback 1583; na captura é P2P — nós não relayamos)");
                return;
            }
            // ACK 0x030d do cliente (7B): consome.
            if (pkt.Length >= 2 && pkt[0] == 0x0d && pkt[1] == 0x03) { Journey(_jt, from, "CONSUMIDO (keepalive)"); return; }

            // LOCKSTEP de sessão P2P (0x0304 open/push, 0x0305 ack) — dialeto REAL da captura de 2 humanos
            // (docs/p2p-handshake-groundtruth.txt l.12-23; codec BotLockstep). O host abre/re-pusha o stream
            // dele PARA O BOT neste socket (o 0x319 registrou o servidor como endpoint do bot). O servidor faz
            // as duas pontas no lugar do bot: (a) ACKeia o push do host com o formato exato do joiner (bytes
            // 6/7 = seat do ACKER — o eco-clone antigo mantinha os seats do host, o host descartava e re-pushava
            // a cada 5s); (b) relaya ao host os opens/pushes que o BOT origina do socket dedicado (41xxx).
            if (pkt.Length >= 2 && pkt[1] == 0x03 && (pkt[0] == 0x04 || pkt[0] == 0x05 || pkt[0] == 0x19))
            {
                if (from.Port >= BotUdpPortBase) { Journey(_jt, from, "relay BOT->humanos (lockstep do bot)"); RelayToAllFields(pkt, from); return; }
                var ls = ResolveSender(from);
                var lf = ls != null ? _world.GetField(ls.FieldId) : null;
                byte dst = pkt.Length >= 8 ? BotLockstep.DstSeat(pkt) : (byte)0xff;
                if (BotLockstep.IsPush(pkt) && lf != null && lf.RecAt(dst)?.IsBot == true)
                {
                    try { _sock!.SendTo(BotLockstep.BuildAck(pkt, dst), from); } catch { }
                    Journey(_jt, from, $"host seat {ls?.FieldSeat ?? 0xff} -> BOT seat {dst}: ACKEADO no lugar do bot (seq={BinaryPrimitives.ReadUInt32LittleEndian(pkt.AsSpan(2))})");
                    return;
                }
                // Lockstep de um HUMANO (push p/ outro humano OU ack). HOJE não é relayado — só instrumentado
                // p/ o diagnóstico saber se esse tráfego chega ao servidor e p/ quem vai (dst do push; no ack
                // o byte 7 é o seat do ACKER, não roteável). Candidato ao muro se o stream P2P reliable entre
                // humanos passar por aqui e morrer.
                bool isPush = pkt[0] == 0x04;
                var dstRec = lf?.RecAt(dst);
                string alvo = dstRec == null ? "?" : dstRec.IsBot ? $"BOT seat {dst}" : $"humano seat {dst}";
                Journey(_jt, from, $"CONSUMIDO (lockstep {(isPush ? "push" : "ack")} do humano seat {ls?.FieldSeat ?? 0xff} " +
                    $"{(isPush ? $"-> {alvo}" : "(acker)")} — NÃO relayado)");
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
                // CAPTURA de CreateNpc só de CLIENTE real (porta < base de bot) — golden p/ o blob; nunca o do bot.
                if (pkt[0] == 0x07 && from.Port < BotUdpPortBase)
                {
                    string hex = Convert.ToHexString(pkt);
                    Log.Ok("udp", "CAPTURE-CELL 0x{0:X2}07 {1}B de {2}: {3}", pkt[1], pkt.Length, from, hex);
                    BridgeIo.CaptureCreateNpc(pkt.Length, from.ToString(), hex);
                }
                Journey(_jt, from, "relay NPC (0x07/0x0b) a todos do field");
                RelayNpcToFields(pkt, from);
                return;
            }

            // NETCODE da SESSÃO P2P da engine: TODA a família de gameplay 0x03xx/0x83xx (0x83xx = 0x03xx com o
            // bit de entrega reliable) é RELAY INTACTO — o papel do worldserv original. A whitelist antiga
            // (0x30a/0x30f/0x830c/0x8313) DROPAVA o resto no parse de ping: o 0x8312 (estado/time do player
            // novo, emitido quando um 3º peer registra) nunca chegava ao outro humano -> nunca ackeado ->
            // retransmitido 1x/s p/ sempre -> a ativação de combate não completava ("ninguém se hita" com o
            // bot presente; sem bot o 0x8312 nem existe). Exceções com trilha própria ANTES deste bloco:
            // input 0x0040, feedback 1583 (0x8315 8B), 0x030d, lockstep 0x0304/0x0305/0x0319, NPC 0x07/0x0b.
            // O 0x0311 (ataque) fica FORA (pkt[0] != 0x11): tem arbitragem própria no bloco abaixo.
            if (pkt.Length >= 7 && (pkt[1] == 0x03 || pkt[1] == 0x83) && pkt[0] != 0x11)
            {
                ushort mt = pkt.Length >= 2 ? (ushort)(pkt[0] | (pkt[1] << 8)) : (ushort)0;
                Log.Ok("udp", "GAMEPLAY RX {0}B type=0x{1:X4} de {2} data={3}", pkt.Length, mt, from, Convert.ToHexString(pkt[..Math.Min(32, pkt.Length)]));
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
                            sf.ExpandHumanHull(nx, nz);   // chão VÁLIDO empírico p/ o clamp do bot (onde o humano pisou)
                        }
                    }
                    // RELAY HUMANO->HUMANO (2 clientes): o 0x30a/0x30f/reliable do humano vai aos OUTROS humanos do
                    // MESMO field. Registra o endpoint do servidor como peer do sender no receptor (0x319) p/ passar
                    // no IsValidUDP_ForPlayer. Sem isto o 2º humano fica "fantasma" (só se vê). O avatar é criado pelo
                    // spawn mútuo 0x4b (SpawnStageForHumans); aqui só o movimento.
                    int nh = RelayHumanToOthers(pkt, sender, sf);
                    Journey(_jt, from, $"RELAY humano seat {sender.FieldSeat} -> {nh} outro(s) do field {sf?.Id ?? -1}");
                    return;
                }
                // Pacote do BOT (srcSlot=bot) OU fonte realmente desconhecida -> RELAY aos humanos no field
                // (do _sock do servidor — mesma origem que o 0x319 registra, p/ passar no IsValidUDP_ForPlayer).
                int nb = RelayToAllFields(pkt, from);
                Journey(_jt, from, $"RELAY do BOT srcSlot={srcSlot} -> {nb} humano(s)");
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
                    int nb = RelayToAllFields(pkt, from);
                    Journey(_jt, from, $"ATAQUE do BOT srcSlot={srcSlot} -> RELAY a {nb} humano(s) (alvo prevê própria morte)");
                    return;
                }
                var arec = f != null ? f.FindRec(attacker!) : null;
                if (f != null && arec != null && !arec.IsBot)
                {
                    ushort actionId = BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(8));
                    // ARBITRA o hit nos BOTS inimigos (só o bot não tem cliente p/ reportar a própria morte).
                    int killedBots = 0, hitBots = 0;
                    if (f.BotCount > 0)
                    {
                        int? killed = f.ResolveBotHitByHuman(arec.Slot, actionId, out int hitBotSlot);
                        if (hitBotSlot >= 0) { hitBots = 1; BridgeIo.SignalHit(hitBotSlot); }
                        if (killed is int botSeat) { killedBots = 1; f.BroadcastField(0x4f, new byte[] { (byte)botSeat, 0, (byte)arec.Slot, f.Score0, f.Score1 }); }
                    }
                    // RELAY o ataque aos OUTROS HUMANOS do field (o alvo humano prevê o próprio hit — modelo
                    // cliente-autoritativo, como no P2P onde o 0x0311 corre peer-a-peer). Sem isto o hit do
                    // atacante nunca chega ao cliente da vítima humana quando há um bot no field.
                    int nh = RelayHumanToOthers(pkt, attacker!, f);
                    Journey(_jt, from, $"ATAQUE humano seat {arec.Slot} action=0x{actionId:X4}: arbitrado vs bots (hit={hitBots} kill={killedBots}) + RELAY a {nh} humano(s)");
                }
                else Journey(_jt, from, $"ATAQUE srcSlot={srcSlot}: DROP (sender não resolvido no field)");
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
                int n = 0;
                foreach (var sess in _world.Sessions)
                {
                    if (sess.UdpEndpoint == null || (!sess.InField && sess.Status != 3)) continue; // Status 3 = stage (UserStatus.InField): robusto a relog que zera o bool InField
                    if (sess == sender) continue;                       // FUN_00426b30: exclui o proprio sender
                    if (sess.UdpEndpoint.Equals(from)) continue;        // fallback de exclusao por endpoint
                    if (senderField >= 0 && sess.FieldId != senderField) continue; // so peers do mesmo field
                    try { _sock!.SendTo(pkt, sess.UdpEndpoint); n++; } catch { }
                }
                // O melee do humano chega por 0x0311 (confirmado no log: 0 pacotes 0x0401), arbitrado lá;
                // este bloco só RELAYA a ação de campo aos outros peers (sem arbitrar = sem caminho duplo).
                Journey(_jt, from, $"AÇÃO-CAMPO -> RELAY a {n} outro(s) do field {senderField} (exclui sender)");
                return;
            }

            // Formato REAL (capturado): [u16 type=0x0202][u8 counter][u16 pad][BODY...].
            // BODY (offset 5): [u16 slot][u32 key][...][u32 echoData @ body+0xc = pkt+17].
            // (FUN_00425d80: *body=slot, *(body+1)=key==user+0x1464; echo data = body[0xc].)
            // >= 23: o handler lê echoData em [19..22] (ReadUInt32 @19). Frames reliable curtos (21-22B) caíam aqui
            // e estouravam IndexOutOfRange no AsSpan(19) — input externo nunca pode lançar (CLAUDE.md).
            if (pkt.Length < 23) { Journey(_jt, from, $"DROP pkt curto {pkt.Length}B (não bateu em nenhuma rota — frame não roteado!)"); return; }
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
            if (s.UdpEndpoint == null) Log.Ok("udp", "[{0}] handshake UDP: endpoint {1} (key={2:X8} gameId={3})", s.Slot, from, key, s.GameInfoId);

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
            try { _sock!.SendTo(echo, from); Log.Info("udp", "[{0}] echo 0x0201 R=1 (port2) -> {1}", slot, from); }
            catch (Exception ex) { Log.Debug("udp", "echo {0}: {1}", from, ex.Message); }
            Journey(_jt, from, $"HANDSHAKE ping seat {s.FieldSeat} -> echo 0x0201");
        }
    }
}
