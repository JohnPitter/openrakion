using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace RakionServer.Peer
{
    /// <summary>
    /// Camada reliable (L2) da SE1 sobre o transporte do FIO Rakion (§3 do blueprint): o canal reliable é um
    /// BYTE-STREAM ordenado por OFFSET. Cada CNetworkMessage é emoldurada no stream por [u32 len][bytes] e o
    /// stream é fatiado em datagramas 0x0304 (token=offset). O receptor confirma por 0x0305 (ACK cumulativo de
    /// offset) e reassembla o stream de entrada por offset, surfando as CNetworkMessages completas em ordem.
    /// Espelha SendGameStreamBlocks/ProcessGameStream/ResendGameStreamBlocks: em loopback (perda~0) a janela é
    /// grande e o resend quase nunca dispara — mas o caminho NACK existe por robustez.
    ///
    /// DOIS contadores distintos (blueprint §1): o OFFSET (L1, posição no byte-stream) ≠ o nº de sequência de
    /// bloco do game-stream (L2, INDEX no MSG_GAMESTREAMBLOCKS). Esta classe é a camada de OFFSET; a camada de
    /// bloco (CNetworkStreamBlock) é montada por <see cref="GameStream"/> e trafega como payload aqui.
    ///
    /// I/O isolado por delegate (CLAUDE.md): a classe NÃO conhece socket; emite frames por <see cref="_send"/>.
    /// </summary>
    public sealed class ReliableStream
    {
        /// <summary>Prefixo de emolduramento de cada CNetworkMessage no byte-stream ([u32 len]).</summary>
        private const int LengthPrefix = 4;

        /// <summary>MTU do payload por 0x0304 (folga sob o recvfrom 0x4b0=1200 do worldserv, menos o header L1).</summary>
        private const int PayloadMtu = 1024;

        private readonly Action<byte[]> _send;
        private readonly byte _role;
        private readonly byte _sub;

        // ---- SAÍDA: byte-stream reliable que o peer empurra ao host ----
        private readonly List<byte> _outStream = new();
        private uint _outAckedOffset;        // até onde o host confirmou (0x0305)
        private uint _outSentOffset;         // até onde já empurramos
        private uint _outSeq;                // seq do datagrama 0x0304 (++ por push)
        // O ACK do host ECOA o offset (start) do frame recebido (captura: eco verbatim). Mapeamos start→end p/
        // avançar a janela ao FIM do frame confirmado, mantendo o ACK cumulativo correto.
        private readonly Dictionary<uint, uint> _outFrameEnd = new();   // offset-de-início -> offset-de-fim do frame

        // ---- ENTRADA: reassembly do byte-stream do host por offset ----
        private readonly SortedDictionary<uint, byte[]> _inSegments = new();
        private uint _inContiguous;          // prefixo contíguo já recebido
        private readonly List<byte> _inAssembled = new();
        private int _inConsumed;             // bytes do _inAssembled já entregues como mensagens

        public ReliableStream(Action<byte[]> send, byte role = WireFrames.RolePeerStream, byte sub = 0x00)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _role = role;
            _sub = sub;
        }

        /// <summary>Offset confirmado pelo host (diagnóstico/testes).</summary>
        public uint AckedOffset => _outAckedOffset;

        /// <summary>Offset já empurrado (diagnóstico/testes).</summary>
        public uint SentOffset => _outSentOffset;

        /// <summary>
        /// Enfileira uma CNetworkMessage para envio reliable: emoldura [u32 len][bytes] no byte-stream de saída
        /// e empurra os 0x0304 pendentes (do offset enviado ao fim). Em loopback empurra tudo de uma vez.
        /// </summary>
        public void SendMessage(ReadOnlySpan<byte> message)
        {
            Span<byte> prefix = stackalloc byte[LengthPrefix];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, message.Length);
            _outStream.AddRange(prefix.ToArray());
            _outStream.AddRange(message.ToArray());
            FlushOut();
        }

        /// <summary>Empurra os 0x0304 do offset enviado até o fim do byte-stream de saída, fatiando por MTU. O
        /// offset enviado avança ANTES do _send: um ACK síncrono (loopback/test) já vê a janela atualizada.</summary>
        private void FlushOut()
        {
            while (_outSentOffset < _outStream.Count)
            {
                int start = (int)_outSentOffset;
                int len = Math.Min(PayloadMtu, _outStream.Count - start);
                byte[] chunk = _outStream.GetRange(start, len).ToArray();
                _outSentOffset += (uint)len;
                _outFrameEnd[(uint)start] = _outSentOffset;   // p/ mapear o eco do ACK (start) ao fim do frame
                byte[] push = WireFrames.BuildPush(_outSeq, _role, _sub, (uint)start, chunk);
                PeerTrace.Emit("rel TX 0x0304 seq={0} role=0x{1:x2} sub=0x{2:x2} off={3} len={4} (frame {5}B) [{6}]",
                    _outSeq, _role, _sub, start, len, push.Length, PeerTrace.ShortHex(push));
                _outSeq++;
                _send(push);
            }
        }

        /// <summary>
        /// Processa um ACK 0x0305 do host: avança o offset confirmado (cumulativo). Sem retransmissão proativa
        /// em loopback; se o ACK ficar atrás do enviado por perda, <see cref="ResendFrom"/> reenvia a faixa.
        /// </summary>
        public void OnAck(uint ackedOffset)
        {
            uint before = _outAckedOffset;
            // O host ECOA o offset de início do frame; a confirmação real cobre até o FIM desse frame.
            uint confirmed = _outFrameEnd.TryGetValue(ackedOffset, out uint end) ? end : ackedOffset;
            if (confirmed > _outAckedOffset) _outAckedOffset = Math.Min(confirmed, _outSentOffset);
            PeerTrace.Emit("rel RX 0x0305 ACK ecoOff={0} (fim-do-frame={1}) -> janela acked {2}->{3} (sent={4})",
                ackedOffset, confirmed, before, _outAckedOffset, _outSentOffset);
        }

        /// <summary>
        /// Reenvia a faixa do byte-stream de saída a partir de <paramref name="offset"/> (resposta a um
        /// MSG_REQUESTGAMESTREAMRESEND/NACK do host, ou recuperação local). ResendGameStreamBlocks por faixa.
        /// </summary>
        public void ResendFrom(uint offset)
        {
            if (offset >= _outStream.Count) return;
            int start = (int)offset;
            while (start < _outStream.Count)
            {
                int len = Math.Min(PayloadMtu, _outStream.Count - start);
                byte[] chunk = _outStream.GetRange(start, len).ToArray();
                _send(WireFrames.BuildPush(_outSeq++, _role, _sub, (uint)start, chunk));
                start += len;
            }
        }

        /// <summary>
        /// Recebe um 0x0304 do host: insere o segmento por offset, responde 0x0305 (ACK do offset recebido),
        /// e reassembla o prefixo contíguo. Devolve as CNetworkMessages que ficaram COMPLETAS (em ordem). Em
        /// buraco (offset à frente do contíguo) o segmento espera; o ACK cumulativo continua no contíguo.
        /// </summary>
        public IReadOnlyList<NetMessage> OnPush(ReadOnlyMemory<byte> pkt)
        {
            if (!WireFrames.TryReadReliable(pkt, out var header, out var payload))
            {
                PeerTrace.Emit("rel RX 0x0304 CURTO {0}B [{1}] (ignorado)", pkt.Length, PeerTrace.ShortHex(pkt.Span));
                return Array.Empty<NetMessage>();
            }

            PeerTrace.Emit("rel RX 0x0304 seq={0} role=0x{1:x2} sub=0x{2:x2} off={3} payload={4}B (frame {5}B) [{6}]",
                header.Seq, header.Role, header.Sub, header.Offset, payload.Length, pkt.Length, PeerTrace.ShortHex(pkt.Span));

            // ACK: o eco do offset recebido (regra da captura: todo 0x0304 → 0x0305).
            byte[]? ack = WireFrames.BuildAck(pkt.Span);
            if (ack != null)
            {
                PeerTrace.Emit("rel TX 0x0305 ACK eco [{0}]", PeerTrace.ShortHex(ack));
                _send(ack);
            }

            if (payload.Length > 0 && header.Offset >= _inContiguous)
                _inSegments[header.Offset] = payload.ToArray();
            else if (payload.Length > 0)
                PeerTrace.Emit("rel RX off={0} < contiguo={1} (duplicata/atrasado, descarta payload)",
                    header.Offset, _inContiguous);

            AdvanceContiguous();
            var msgs = ExtractMessages();
            if (msgs.Count > 0)
                PeerTrace.Emit("rel reassembly: {0} CNetworkMessage(s) completa(s) (contiguo agora={1})",
                    msgs.Count, _inContiguous);
            return msgs;
        }

        /// <summary>Funde os segmentos contíguos a partir de <see cref="_inContiguous"/> no buffer remontado.</summary>
        private void AdvanceContiguous()
        {
            while (_inSegments.TryGetValue(_inContiguous, out byte[]? seg))
            {
                _inAssembled.AddRange(seg);
                _inSegments.Remove(_inContiguous);
                _inContiguous += (uint)seg.Length;
            }
        }

        /// <summary>Lê do buffer remontado todas as mensagens [u32 len][bytes] completas ainda não entregues.</summary>
        private List<NetMessage> ExtractMessages()
        {
            var msgs = new List<NetMessage>();
            while (true)
            {
                int avail = _inAssembled.Count - _inConsumed;
                if (avail < LengthPrefix) break;
                int len = BinaryPrimitives.ReadInt32LittleEndian(
                    CollectionsMarshalSpan(_inConsumed, LengthPrefix));
                if (len < 0 || avail - LengthPrefix < len) break;   // mensagem ainda incompleta
                byte[] body = _inAssembled.GetRange(_inConsumed + LengthPrefix, len).ToArray();
                _inConsumed += LengthPrefix + len;
                msgs.Add(NetMessage.From(body));
            }
            return msgs;
        }

        /// <summary>Fatia do buffer remontado (evita cópia só p/ ler o prefixo de tamanho).</summary>
        private ReadOnlySpan<byte> CollectionsMarshalSpan(int start, int count) =>
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_inAssembled).Slice(start, count);
    }
}
