using System;

namespace RakionServer.Peer
{
    /// <summary>
    /// O PEER de netcode de UM bot (P6 do blueprint): o objeto que FALA o protocolo da Serious Engine 1 com o
    /// host p/ registrar o bot como peer da sessão e ABRIR o gate do 0x30a. Amarra as camadas:
    ///   L1 transporte (<see cref="WireFrames"/>)  ·  L2 reliable (<see cref="ReliableStream"/> + game-stream)
    ///   ·  L3 handshake (<see cref="SessionHandshake"/> = Start_AtClient_t).
    /// É TRANSPORTE/PROTOCOLO (borda), não regra de IA: a posição/decisão do bot continua no domínio
    /// (WorldServer.BotAi/BotPlayer), que monta os datagramas de ação (BotMovement) e os entrega por
    /// <see cref="EmitAction"/>. Substitui o BotSessionConnect (eco de controle que NÃO abria o gate): aqui o
    /// eco 0x0305 é PARTE do protocolo reliable real, não heurística.
    ///
    /// I/O por delegate (CLAUDE.md): o BotPeer recebe um <c>send</c> já ligado ao endpoint UDP do host; não
    /// conhece socket nem IPEndPoint. Um objeto por bot; estado por bot.
    /// </summary>
    public sealed class BotPeer
    {
        private readonly Action<byte[]> _send;
        private readonly ReliableStream _reliable;
        private readonly GameStream _gameStream = new();
        private readonly SessionHandshake _handshake;
        private uint _keepAliveSeq;

        /// <summary>
        /// <paramref name="send"/> = emissor de 1 datagrama UDP ao host (ligado ao endpoint do host pelo World).
        /// <paramref name="identity"/> = nome/char/índice do bot p/ o SEQ_ADDPLAYER. <paramref name="modName"/> =
        /// mod do host (self-host: o do worldserv). <paramref name="fileCrcOf"/> = CRC32 de um arquivo local p/ a
        /// challenge de CRC (self-host: o arquivo do host; o CRC bate por construção).
        /// </summary>
        public BotPeer(Action<byte[]> send, PeerIdentity identity, string modName, Func<string, uint> fileCrcOf)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _reliable = new ReliableStream(send);
            _handshake = new SessionHandshake(
                sendReliable: msg => _reliable.SendMessage(msg),
                sendKeepAlive: SendKeepAlive,
                gameStream: _gameStream,
                identity: identity,
                connect: SessionHandshake.ConnectParams.Default(modName),
                fileCrcOf: fileCrcOf);
        }

        /// <summary>Fase atual do handshake (diagnóstico/idempotência).</summary>
        public SessionHandshake.State HandshakeState => _handshake.Current;

        /// <summary>True quando o handshake passou de S10 (SEQ_ADDPLAYER emitido) — o gate está prestes a abrir.</summary>
        public bool HandshakeEstablished => _handshake.IsEstablished;

        /// <summary>
        /// O gate do 0x30a do HOST abriu? Sinal FIEL (cravado na captura): o host só EMITE 0x030a/0x030f quando a
        /// sessão networked concluiu o LOAD e processou o SEQ_ADDPLAYER do peer. Acende em <see cref="OnHostGameplay"/>.
        /// </summary>
        public bool GateOpen { get; private set; }

        /// <summary>Já iniciou o handshake (idempotência do spawn).</summary>
        public bool Started => _handshake.Current != SessionHandshake.State.Idle;

        /// <summary>
        /// Inicia a conexão. (1) ABRE o canal reliable direto com um <b>0x0304 role=0xff</b> (control/open) —
        /// GROUND TRUTH da captura (p2p_handshake_decode FASE B: o 1º frame do canal peer↔host é
        /// <c>0403 [seq] ff 00 [offset]</c>, e SÓ depois vêm os sub-streams role 0x0a/0x00). É o open do canal
        /// que faz o host aceitar/processar o reliable subsequente (sem ele, o stream role=0x0a é só ACKeado).
        /// (2) Inicia o handshake de sessão (S0+S1) no stream role=0x0a. Idempotente.
        /// </summary>
        public void Connect()
        {
            if (_handshake.Current != SessionHandshake.State.Idle) return;
            byte[] open = WireFrames.BuildPush(_keepAliveSeq, WireFrames.RoleControl, 0x00, 0u, ReadOnlySpan<byte>.Empty);
            PeerTrace.Emit("peer TX 0x0304 OPEN role=0xff (abre canal reliable direto) [{0}]", PeerTrace.ShortHex(open));
            _send(open);
            _handshake.Begin();
        }

        /// <summary>
        /// Roteia um datagrama recebido DO HOST. 0x0304 → reassembla o reliable (responde 0x0305) e entrega as
        /// CNetworkMessages completas ao handshake / ao game-stream; 0x0305 → avança o ACK do nosso stream de
        /// saída; 0x0319 → consome (addr-update); 0x030a/0x030f → sinaliza o gate aberto. Outros → ignora.
        /// Segurança por construção: frame curto/forjado nunca estoura.
        /// </summary>
        public void OnDatagram(ReadOnlyMemory<byte> pkt)
        {
            ushort msgType = WireFrames.MsgTypeOf(pkt.Span);
            PeerTrace.Emit("peer RX host frame type=0x{0:x4} {1}B [{2}] (hs={3} gate={4})",
                msgType, pkt.Length, PeerTrace.ShortHex(pkt.Span), _handshake.Current, GateOpen);
            switch (msgType)
            {
                case WireFrames.MsgPushReliable:   // 0x0304
                    foreach (var msg in _reliable.OnPush(pkt))
                        DispatchReliable(msg);
                    break;
                case WireFrames.MsgAckReliable:    // 0x0305
                    if (WireFrames.TryReadReliable(pkt, out var ack, out _))
                        _reliable.OnAck(ack.Offset);
                    break;
                case WireFrames.MsgAddrUpdate:     // 0x0319 (consome)
                    PeerTrace.Emit("peer RX 0x0319 addr-update (consome)");
                    break;
                default:
                    if (IsHostGameplay(pkt.Span)) OnHostGameplay();
                    else PeerTrace.Emit("peer RX type=0x{0:x4} sem rota no peer (ignora)", msgType);
                    break;
            }
        }

        /// <summary>
        /// Entrega uma CNetworkMessage reliable completa: as de SESSÃO (CONNECT/STATEDELTA/CRC) vão p/ o
        /// handshake; um MSG_GAMESTREAMBLOCKS é lido pelo game-stream (avança o lastProcessedSequence; o conteúdo
        /// é descartado — o bot não simula mundo). Em loopback as duas trilhas chegam em ordem.
        /// </summary>
        private void DispatchReliable(NetMessage message)
        {
            if (message.Type == NetworkMessageType.GameStreamBlocks)
            {
                var blocks = _gameStream.ReadBlocks(message);
                PeerTrace.Emit("peer <- GAMESTREAMBLOCKS(26) {0} bloco(s); lastSeq agora={1}",
                    blocks.Count, _gameStream.LastProcessedSequence);
            }
            else
            {
                _handshake.OnMessage(message);
            }
        }

        /// <summary>
        /// Emite um datagrama de AÇÃO já montado (0x030a/0x030f/0x0311 do BotMovement) pelo canal UNRELIABLE,
        /// direto ao host. Não passa pelo reliable (ação é não-confiável por design da engine). O domínio só deve
        /// chamar isto após <see cref="GateOpen"/> (antes, o host ignora as ações).
        /// </summary>
        public void EmitAction(byte[] actionDatagram) => _send(actionDatagram);

        /// <summary>Emite um keepalive 0x030d (L1) — usado pelo handshake nos passos S0/S3/S6/S10.</summary>
        private void SendKeepAlive()
        {
            byte[] k = WireFrames.BuildKeepAlive(_keepAliveSeq);
            PeerTrace.Emit("peer TX 0x030d keepalive seq={0} [{1}]", _keepAliveSeq, PeerTrace.ShortHex(k));
            _keepAliveSeq++;
            _send(k);
        }

        /// <summary>True se o datagrama é gameplay do stream da engine vindo do host (0x030a move / 0x030f keystate).</summary>
        private static bool IsHostGameplay(ReadOnlySpan<byte> pkt) =>
            pkt.Length >= 2 && pkt[1] == 0x03 && (pkt[0] == 0x0a || pkt[0] == 0x0f);

        /// <summary>O host emitiu gameplay → o gate abriu (sinal fiel). Idempotente.</summary>
        private void OnHostGameplay()
        {
            if (!GateOpen) PeerTrace.Emit("peer GATE ABERTO: host emitiu 0x030a/0x030f (sessao networked ativa)");
            GateOpen = true;
        }

        /// <summary>
        /// Força o gate aberto (fallback de timeout). Usado quando o handshake não completa
        /// em tempo hábil. O bot passa a emitir frames mesmo sem handshake completo.
        /// </summary>
        public void ForceGate()
        {
            if (!GateOpen)
            {
                PeerTrace.Emit("peer GATE FORÇADO: handshake não completou em tempo (fallback)");
                GateOpen = true;
            }
        }
    }
}
