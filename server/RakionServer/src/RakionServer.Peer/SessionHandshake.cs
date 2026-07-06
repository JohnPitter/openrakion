using System;
using System.Collections.Generic;

namespace RakionServer.Peer
{
    /// <summary>
    /// A máquina de estado do PEER que conecta — CSessionState::Start_AtClient_t portada (§5 do blueprint),
    /// dirigida por EVENTOS (não bloqueante: em vez do WaitStream_t com Sleep, avança quando o host responde).
    /// Sequência LITERAL da fonte SE1:
    ///   S0 keepalive → S1 REQ_CONNECTREMOTE(7) → [REP(8): consome MOTD/world/flags/props]
    ///   → S3 keepalive + S4 REQ_STATEDELTA(9) → [REP(10): unpack zlib + DESCARTA]
    ///   → S6 keepalive + S7 REQ_CRCLIST(11) → [REQ_CRCCHECK(12): responde S9 REP_CRCCHECK(13) = CRC dos
    ///     arquivos locais + ses_iLastProcessedSequence] → S10 keepalive + emite SEQ_ADDPLAYER(22).
    /// Depois de S10 + SEQ_ADDPLAYER, o host processa o AddPlayer e o gate do 0x30a abre.
    ///
    /// Domínio isolado de I/O (CLAUDE.md): a máquina NÃO conhece socket. Emite por delegates — reliable (vira
    /// payload do ReliableStream) e keepalive (L1). O CRC vem de uma fonte de arquivos injetada (self-host: os
    /// arquivos do worldserv). O SEQ_ADDPLAYER é emitido pelo <see cref="GameStream"/> recebido.
    /// </summary>
    public sealed class SessionHandshake
    {
        /// <summary>Passos da máquina (nomes da fonte). <see cref="WaitPlayerReply"/> = enviou REQ_CONNECTPLAYER e
        /// aguarda REP_CONNECTPLAYER(15); <see cref="Established"/> = host atribuiu o índice (player registrado).</summary>
        public enum State { Idle, WaitConnectReply, WaitStateDelta, WaitCrcChallenge, WaitPlayerReply, Established, Failed }

        /// <summary>Índice do player atribuído pelo host no REP_CONNECTPLAYER(15); -1 até registrar.</summary>
        public int AssignedPlayerIndex { get; private set; } = -1;

        private readonly Action<byte[]> _sendReliable;     // enfileira uma CNetworkMessage no canal reliable
        private readonly Action _sendKeepAlive;            // emite um 0x030d (L1)
        private readonly GameStream _gameStream;
        private readonly PeerIdentity _identity;
        private readonly Func<string, uint> _fileCrcOf;    // CRC32 de um arquivo local (self-host == o do host)
        private readonly ConnectParams _connect;

        public SessionHandshake(
            Action<byte[]> sendReliable,
            Action sendKeepAlive,
            GameStream gameStream,
            PeerIdentity identity,
            ConnectParams connect,
            Func<string, uint> fileCrcOf)
        {
            _sendReliable = sendReliable ?? throw new ArgumentNullException(nameof(sendReliable));
            _sendKeepAlive = sendKeepAlive ?? throw new ArgumentNullException(nameof(sendKeepAlive));
            _gameStream = gameStream ?? throw new ArgumentNullException(nameof(gameStream));
            _identity = identity;
            _connect = connect;
            _fileCrcOf = fileCrcOf ?? throw new ArgumentNullException(nameof(fileCrcOf));
        }

        /// <summary>Parâmetros do REQ_CONNECTREMOTE (mod/senha/socket). DTO de borda.</summary>
        /// <param name="ModName">Mod do host (self-host: o do worldserv; "" se nenhum) — gate de aceitação.</param>
        /// <param name="Password">Senha da sessão ("" se none).</param>
        /// <param name="SocketParams">CSessionSocketParams (loopback por default).</param>
        /// <param name="RequestCrc">Pedir a challenge de CRC (REQ_CRCLIST)? Default FALSE p/ o bot: o CRC é
        /// CLIENT-iniciado e o host NÃO o exige p/ CONNECTPLAYER (Server.cpp:1256 só checa iClient&gt;0+slot+nome);
        /// como o peer não tem os arquivos do host p/ um CRC válido, pedi-lo só arriscaria um disconnect.</param>
        public readonly record struct ConnectParams(string ModName, string Password, SessionSocketParams SocketParams,
                                                    bool RequestCrc = false)
        {
            public static ConnectParams Default(string modName) =>
                new(modName ?? "", "", SessionSocketParams.Loopback);
            public static ConnectParams WithCrc(string modName) =>
                new(modName ?? "", "", SessionSocketParams.Loopback, RequestCrc: true);
        }

        public State Current { get; private set; } = State.Idle;

        /// <summary>True quando o handshake passou de S10 e o SEQ_ADDPLAYER foi emitido (gate prestes a abrir).</summary>
        public bool IsEstablished => Current == State.Established;

        /// <summary>S0+S1: keepalive de "acordar" + REQ_CONNECTREMOTE(7). Inicia o handshake.</summary>
        public void Begin()
        {
            if (Current != State.Idle) return;
            PeerTrace.Emit("hs S0: keepalive (acorda conexao)");
            _sendKeepAlive();   // S0
            byte[] req = ConnectMessages.BuildConnectRemoteRequest(
                _connect.ModName, _connect.Password, _connect.SocketParams);   // S1 (type 7)
            PeerTrace.Emit("hs S1: -> REQ_CONNECTREMOTE(7) mod='{0}' {1}B [{2}]",
                _connect.ModName ?? "", req.Length, PeerTrace.ShortHex(req));
            _sendReliable(req);
            Transition(State.WaitConnectReply, "REP_CONNECTREMOTE(8)");
        }

        /// <summary>Muda de estado e loga o destino + a mensagem que o handshake agora ESPERA do host.</summary>
        private void Transition(State next, string waitingFor)
        {
            Current = next;
            PeerTrace.Emit("hs estado -> {0} (esperando {1} do host)", next, waitingFor);
        }

        /// <summary>
        /// Alimenta uma CNetworkMessage RELIABLE completa (entregue pelo ReliableStream) e avança a máquina. O
        /// host envia, em ordem: REP_CONNECTREMOTE(8) → REP_STATEDELTA(10) → REQ_CRCCHECK(12). Os tipos fora de
        /// sequência são ignorados (robustez). MSG_INF_DISCONNECTED(3) → Failed.
        /// </summary>
        public void OnMessage(NetMessage message)
        {
            PeerTrace.Emit("hs RX msg tipo={0}(0x{1:x2}) {2}B no estado {3} [{4}]",
                message.Type, (int)message.Type, message.Body.Length, Current, PeerTrace.ShortHex(message.Body.Span));

            if (message.Type == NetworkMessageType.InfDisconnected)
            {
                PeerTrace.Emit("hs <- INF_DISCONNECTED(3): host RECUSOU o connect -> Failed");
                Current = State.Failed;
                return;
            }

            switch (Current)
            {
                case State.WaitConnectReply:
                    if (message.Type == NetworkMessageType.RepConnectRemoteSessionState) OnConnectReply(message);
                    else PeerTrace.Emit("hs ignora tipo {0} (esperando REP_CONNECTREMOTE 8)", message.Type);
                    break;
                case State.WaitStateDelta:
                    if (message.Type == NetworkMessageType.RepStateDelta) OnStateDelta(message);
                    else PeerTrace.Emit("hs ignora tipo {0} (esperando REP_STATEDELTA 10)", message.Type);
                    break;
                case State.WaitCrcChallenge:
                    if (message.Type == NetworkMessageType.ReqCrcCheck) OnCrcChallenge(message);
                    else PeerTrace.Emit("hs ignora tipo {0} (esperando REQ_CRCCHECK 12)", message.Type);
                    break;
                case State.WaitPlayerReply:
                    if (message.Type == NetworkMessageType.RepConnectPlayer) OnPlayerReply(message);
                    else PeerTrace.Emit("hs ignora tipo {0} (esperando REP_CONNECTPLAYER 15)", message.Type);
                    break;
                default:
                    PeerTrace.Emit("hs RX no estado {0} sem transicao (mensagem fora de sequencia)", Current);
                    break;
            }
        }

        /// <summary>S2→S4: consome REP(8) (drena MOTD/world/flags/props), keepalive (S3), REQ_STATEDELTA(9).</summary>
        private void OnConnectReply(NetMessage message)
        {
            PeerTrace.Emit("hs S2: <- REP_CONNECTREMOTE(8) aceito (host viu o connect)");
            DrainConnectReply(message);     // o cursor precisa consumir o frame; o conteúdo não é necessário
            PeerTrace.Emit("hs S3+S4: keepalive + -> REQ_STATEDELTA(9)");
            _sendKeepAlive();               // S3
            _sendReliable(ConnectMessages.BuildTypeOnly(NetworkMessageType.ReqStateDelta));   // S4 (type 9)
            Transition(State.WaitStateDelta, "REP_STATEDELTA(10)");
        }

        /// <summary>S5→S7: unpack zlib do delta e DESCARTA (§5.6), keepalive (S6). Se <see cref="ConnectParams.
        /// RequestCrc"/>, pede REQ_CRCLIST(11); senão (default do bot) vai DIRETO ao REQ_CONNECTPLAYER — o host
        /// não exige CRC, e o peer não tem como produzir um CRC válido.</summary>
        private void OnStateDelta(NetMessage message)
        {
            PeerTrace.Emit("hs S5: <- REP_STATEDELTA(10) {0}B (unpack zlib + DESCARTA)", message.Body.Length);
            var r = message.BeginRead();
            // O corpo após o byte de tipo é o stream zlib do delta; inflamos só p/ validar/drenar e DESCARTAMOS.
            _ = Compression.TryUnpackZlib(r.ReadBytes(r.Remaining));
            _sendKeepAlive();               // S6
            if (_connect.RequestCrc)
            {
                PeerTrace.Emit("hs S7: -> REQ_CRCLIST(11) (challenge de CRC habilitada)");
                _sendReliable(ConnectMessages.BuildTypeOnly(NetworkMessageType.ReqCrcList));  // S7 (type 11)
                Transition(State.WaitCrcChallenge, "REQ_CRCCHECK(12)");
            }
            else
            {
                PeerTrace.Emit("hs S7 (pula CRC): host não exige CRC p/ CONNECTPLAYER -> direto ao registro");
                SendConnectPlayer();
            }
        }

        /// <summary>
        /// S8→S10: lê a LISTA de arquivos do REQ_CRCCHECK(12), responde REP_CRCCHECK(13) = [ULONG crc][INDEX
        /// lastSeq] (§5.5), keepalive final (S10), e manda REQ_CONNECTPLAYER(14) com o CPlayerCharacter — o
        /// caminho REAL da fonte (CPlayerSource::Start_t → SendToServerReliable). É o HOST que, ao receber o
        /// REQ_CONNECTPLAYER, gera o SEQ_ADDPLAYER(22) e o distribui a TODAS as sessões (inclusive a local do
        /// humano) → cria a CPlayerEntity com colisão. O peer NÃO emite SEQ_ADDPLAYER (isso é papel do host).
        /// </summary>
        private void OnCrcChallenge(NetMessage message)
        {
            var names = ReadCrcFileList(message);
            uint crc = CrcEngine.CombineFileList(names, _fileCrcOf);
            PeerTrace.Emit("hs S8: <- REQ_CRCCHECK(12) {0} arquivo(s) -> S9 REP_CRCCHECK(13) crc=0x{1:x8} lastSeq={2}",
                names.Count, crc, _gameStream.LastProcessedSequence);
            _sendReliable(ConnectMessages.BuildCrcCheckReply(crc, _gameStream.LastProcessedSequence));  // S9 (type 13)
            _sendKeepAlive();   // S10
            SendConnectPlayer();
        }

        /// <summary>Manda REQ_CONNECTPLAYER(14) reliable com o CPlayerCharacter — o caminho REAL da fonte
        /// (CPlayerSource::Start_t). O HOST, ao receber, gera o SEQ_ADDPLAYER(22) e o distribui a TODAS as sessões
        /// (inclusive a local do humano) → cria a CPlayerEntity com colisão. O peer NÃO emite SEQ_ADDPLAYER.</summary>
        private void SendConnectPlayer()
        {
            byte[] req = PlayerMessages.BuildConnectPlayerRequest(_identity.Character);   // type 14 (reliable)
            PeerTrace.Emit("hs -> REQ_CONNECTPLAYER(14) '{0}' {1}B [{2}] (host gera o SEQ_ADDPLAYER)",
                _identity.Name, req.Length, PeerTrace.ShortHex(req));
            _sendReliable(req);
            Transition(State.WaitPlayerReply, "REP_CONNECTPLAYER(15)");
        }

        /// <summary>REP_CONNECTPLAYER(15): [INDEX iPlayer] — o host registrou o bot e atribuiu o índice. A partir
        /// daqui o host já distribuiu o SEQ_ADDPLAYER às sessões (a CPlayerEntity do bot existe no mundo do humano)
        /// e o gate do 0x30a abre. PlayerSource.cpp:86-91 (<c>nmReceived&gt;&gt;pls_Index; pls_Active=TRUE</c>).</summary>
        private void OnPlayerReply(NetMessage message)
        {
            var r = message.BeginRead();
            int idx = r.ReadIndex();
            AssignedPlayerIndex = r.Overflowed ? -1 : idx;
            PeerTrace.Emit("hs <- REP_CONNECTPLAYER(15) idx={0}: bot REGISTRADO (CPlayerEntity criada no host)",
                AssignedPlayerIndex);
            Transition(State.Established, "1o 0x30a do host (gate)");
        }

        /// <summary>REP_CONNECTREMOTE(8): [CTString MOTD][CTString world][ULONG spawnFlags][2048 props] (§4.4).
        /// Lido só p/ avançar o cursor; o bot não precisa do conteúdo (world == o do host em self-host).</summary>
        private static void DrainConnectReply(NetMessage message)
        {
            var r = message.BeginRead();
            _ = r.ReadCString();   // MOTD
            _ = r.ReadCString();   // world (CTFileName = CTString)
            _ = r.ReadU32();       // spawnFlags
            // os 2048 bytes de props seguem; não precisam ser lidos individualmente (o frame inteiro é descartado).
        }

        /// <summary>
        /// Lê a challenge MSG_REQ_CRCCHECK(12): [INDEX ctFiles][CTString name × ctFiles] (CRCT_MakeCRCForFiles_t
        /// lê assim do próprio stream da mensagem). Frame curto/forjado → para (segurança por construção).
        /// </summary>
        private static List<string> ReadCrcFileList(NetMessage message)
        {
            var names = new List<string>();
            var r = message.BeginRead();
            int count = r.ReadIndex();
            if (r.Overflowed || count < 0) return names;
            for (int i = 0; i < count; i++)
            {
                string name = r.ReadCString();
                if (r.Overflowed) break;
                names.Add(name);
            }
            return names;
        }
    }
}
