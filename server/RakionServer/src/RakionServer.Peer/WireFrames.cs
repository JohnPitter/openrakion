using System;
using System.Buffers.Binary;

namespace RakionServer.Peer
{
    /// <summary>
    /// Camada L1 (transporte do FIO Rakion): o framing dos datagramas UDP que ENVELOPAM as CNetworkMessages
    /// reliable (minipeer_blueprint §6 + netcode_peer_re §1.2). É a forma do worldserv relay (NÃO o CPacketBuffer
    /// "stock" da SE1 com flags UDP_PACKET_*); decodificada da captura (stage_udp_capture.txt, :40709):
    ///
    ///   geral:        [u16 msgType LE][u32 seq LE][u8 role][u8 sub][u32 token=OFFSET LE][ payload... ]
    ///   0x0304 PUSH:  empurra um trecho do byte-stream reliable; token = OFFSET do byte-stream do canal.
    ///   0x0305 ACK:   eco do peer "recebi até este offset" (mesmo seq/role/sub/token do 0x0304).
    ///   0x0319 ADDR:  addr-update/ACK-lite 8B; manutenção de endpoint, não ACK de dados.
    ///   0x030d KEEP:  keepalive 7B [u16 0x030d][u8 0x03][u32 seq] (heartbeat durante o load).
    ///
    /// DIVERGÊNCIA L1 (M1 do blueprint, OFF-WIRE não-medível aqui — resolvida in-game): a captura só logou os
    /// HEADERS 12-13B; o PAYLOAD reliable (~103B/frame) é reconstruído in-memory na engine. A síntese do peer
    /// CARREGA o payload no corpo do 0x0304 (caso (i) da §6: é o único modo de um peer .NET ENTREGAR mensagens
    /// reais ao host). token = posição (offset) do 1º byte do payload no byte-stream de saída.
    ///
    /// O token é o OFFSET CRU do byte-stream (o "0xc0" de uma captura antiga era só o HI byte do offset naquele
    /// ponto, NÃO um marcador fixo — ver <see cref="TokenFromOffset"/> e p2p_handshake_decode §FASE B).
    /// </summary>
    public static class WireFrames
    {
        public const ushort MsgPushReliable = 0x0304;
        public const ushort MsgAckReliable = 0x0305;
        public const ushort MsgAddrUpdate = 0x0319;
        public const ushort MsgKeepAlive = 0x030d;

        /// <summary>role do canal de dados do peer (captura: 0x0a no W->C do stream-do-peer).</summary>
        public const byte RolePeerStream = 0x0a;

        /// <summary>role do stream de controle que ABRE o canal (captura: 0xff).</summary>
        public const byte RoleControl = 0xff;

        private const int MsgTypeLen = 2;
        private const int SeqLen = 4;

        /// <summary>Offset do byte role no frame 0x0304/0x0305 ([u16 type][u32 seq] = 6).</summary>
        public const int RoleOffset = MsgTypeLen + SeqLen;            // +6

        /// <summary>Offset do byte sub.</summary>
        public const int SubOffset = RoleOffset + 1;                  // +7

        /// <summary>Offset do u32 token (=byte-stream offset).</summary>
        public const int TokenOffset = SubOffset + 1;                 // +8

        /// <summary>Tamanho do HEADER fixo do frame reliable (12B): [u16 type][u32 seq][u8 role][u8 sub][u32 token].</summary>
        public const int HeaderLen = TokenOffset + 4;                 // +12

        /// <summary>
        /// Offset do início do "payload" reliable. CRAVADO da captura (stage_udp_capture.txt): TODO 0x0304/0x0305
        /// real é 12 ou 13 bytes — o 13º é um byte de CAUDA (marcador de sub-stream 0x00/0x0a), NÃO bytes de
        /// mensagem. O byte-stream reliable NÃO viaja no datagrama (M1 = caso (ii) do blueprint §6: off-wire). Logo
        /// o payload de mensagem neste fio é SEMPRE vazio; o que vinha depois do header é a cauda de controle.
        /// </summary>
        public const int PayloadOffset = HeaderLen;                   // +12 (= fim do header)

        private const int KeepAliveLen = 7;
        private const byte KeepAliveSub = 0x03;

        /// <summary>True se o frame é um 0x0304/0x0305/0x0319 (descriminador de roteamento no UdpGameplay).</summary>
        public static bool IsReliableFrame(ReadOnlySpan<byte> pkt)
        {
            if (pkt.Length < MsgTypeLen) return false;
            if (pkt[1] != 0x03) return false;
            return pkt[0] == 0x04 || pkt[0] == 0x05 || pkt[0] == 0x19;
        }

        /// <summary>msgType (u16 LE) de um datagrama (datagrama[0:2]); 0 se curto demais.</summary>
        public static ushort MsgTypeOf(ReadOnlySpan<byte> pkt) =>
            pkt.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(pkt) : (ushort)0;

        /// <summary>O token É o offset CRU do byte-stream reliable (32b LE). GROUND TRUTH (p2p_handshake_decode
        /// §FASE B, linha "0x02f8 NAO e marcador fixo: e o HI do offset corrente"): o "0xc0" de uma captura antiga
        /// era só o HI byte do offset NAQUELE ponto, não um marcador de canal. Logo token==offset, sem máscara.</summary>
        public static uint TokenFromOffset(uint offset) => offset;

        /// <summary>Extrai o offset de um token (= o próprio token; ver <see cref="TokenFromOffset"/>).</summary>
        public static uint OffsetFromToken(uint token) => token;

        /// <summary>
        /// Monta um datagrama 0x0304 (PUSH reliable) que carrega <paramref name="payload"/> a partir do
        /// <paramref name="offset"/> do byte-stream de saída. [u16 0x0304][u32 seq][u8 role][u8 sub][u32 token]
        /// [payload]. token = marcador|offset (§6 caso (i)).
        /// </summary>
        public static byte[] BuildPush(uint seq, byte role, byte sub, uint offset, ReadOnlySpan<byte> payload)
        {
            byte[] f = new byte[PayloadOffset + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0), MsgPushReliable);
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(MsgTypeLen), seq);
            f[RoleOffset] = role;
            f[SubOffset] = sub;
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(TokenOffset), TokenFromOffset(offset));
            payload.CopyTo(f.AsSpan(PayloadOffset));
            return f;
        }

        /// <summary>
        /// ACK 0x0305 de um 0x0304 recebido: ECO VERBATIM do frame com APENAS o msgType trocado 0x0304→0x0305.
        /// CRAVADO da captura real de 2 peers (stage_udp_capture.txt): push <c>040305000000000a7c2ec00000</c> →
        /// ack <c>050305000000000a7c2ec00000</c> — só o byte 0 muda (0x04→0x05); seq/role/sub/token E o byte de
        /// CAUDA (13º) são preservados byte-a-byte. NÃO se zera role nem se recomputa o token (o eco é literal).
        /// Devolve null só se o frame for curto demais p/ ser um 0x0304 (precisa do header de 12B + caem os tails).
        /// </summary>
        public static byte[]? BuildAck(ReadOnlySpan<byte> push)
        {
            if (push.Length < PayloadOffset) return null;
            if (!(push[0] == 0x04 && push[1] == 0x03)) return null;
            byte[] ack = push.ToArray();                          // eco verbatim (preserva seq/role/sub/token/cauda)
            BinaryPrimitives.WriteUInt16LittleEndian(ack.AsSpan(0), MsgAckReliable);   // único byte mudado: 0x0304→0x0305
            return ack;
        }

        /// <summary>Keepalive 0x030d (7B): [u16 0x030d][u8 0x03][u32 seq] (§6, heartbeat S0/S3/S6/S10).</summary>
        public static byte[] BuildKeepAlive(uint seq)
        {
            byte[] f = new byte[KeepAliveLen];
            BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0), MsgKeepAlive);
            f[MsgTypeLen] = KeepAliveSub;
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(3), seq);
            return f;
        }

        /// <summary>Header decodificado de um 0x0304/0x0305 (seq/role/sub/offset).</summary>
        public readonly record struct ReliableHeader(uint Seq, byte Role, byte Sub, uint Offset)
        {
            public uint Token => TokenFromOffset(Offset);
        }

        /// <summary>
        /// Decodifica o header (12B) de um 0x0304/0x0305 e devolve a região após o header. CRAVADO da captura: o
        /// frame real do engine é 12B (sem cauda) ou 13B (1 byte de CAUDA de sub-stream, 0x00/0x0a) — NUNCA carrega
        /// bytes de mensagem (M1: o byte-stream reliable é off-wire). Logo um trailing de ATÉ 1 byte é cauda de
        /// controle e NÃO é payload de mensagem; só um trailing >1B é tratado como payload (frame sintético/futuro
        /// canal de corpo). Frame curto → false + payload vazio (nunca estoura; segurança por construção).
        /// </summary>
        public static bool TryReadReliable(ReadOnlyMemory<byte> pkt, out ReliableHeader header, out ReadOnlyMemory<byte> payload)
        {
            header = default;
            payload = ReadOnlyMemory<byte>.Empty;
            var span = pkt.Span;
            if (span.Length < HeaderLen) return false;
            uint seq = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(MsgTypeLen));
            byte role = span[RoleOffset];
            byte sub = span[SubOffset];
            uint token = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(TokenOffset));
            header = new ReliableHeader(seq, role, sub, OffsetFromToken(token));
            int trailing = pkt.Length - HeaderLen;
            // trailing 0 (12B) ou 1 (13B = cauda de controle) -> sem payload de mensagem; >1 -> payload real.
            payload = trailing > 1 ? pkt.Slice(HeaderLen) : ReadOnlyMemory<byte>.Empty;
            return true;
        }
    }
}
