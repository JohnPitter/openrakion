using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Codec do LOCKSTEP de sessão P2P da engine — o dialeto REAL que registra um peer no host e abre a
    /// colisão/HIT×N nativos. Cravado byte-a-byte da captura de 2 humanos (docs/p2p-handshake-groundtruth.txt,
    /// l.12-23): NÃO existe CONNECT SE1 (TAGV/32KB) no fio; o canal é minúsculo:
    ///
    ///   OPEN (12B): [04 03][u32 seq][ff][u8 dstSeat][u32 token]           — o joiner abre com DOIS opens
    ///   PUSH (13B): [04 03][u32 seq][u8 srcSeat][u8 dstSeat][u32 token][u8 srcSeat] — periódico a partida toda
    ///   ACK  (eco): frame copiado, [0]=05, bytes 6 E 7 = seat do ACKER, token+payload ecoados
    ///
    /// Evidência do ack (captura): host (seat 0) ackeia push do joiner com bytes 6/7 = 00 00 (l.17); joiner
    /// (seat 0x0a) ackeia push do host com 0a 0a (l.19). O eco-clone antigo mantinha os seats do REMETENTE →
    /// o host descartava o ack e re-pushava a cada 5s (worldserver.log 2026-07-06). O token é um valor opaco
    /// do remetente (ponteiro no cliente real) que o ack só ecoa. seq = contador único por-sender no canal
    /// (o MESMO espaço dos 0x30a/0x30f — <see cref="Domain.BotPlayer.UdpSeq"/>).
    /// </summary>
    public static class BotLockstep
    {
        /// <summary>Push reliable do canal de sessão (0x0304).</summary>
        public const ushort MsgPush = 0x0304;

        /// <summary>Ack do push (0x0305) — eco com os seats do acker.</summary>
        public const ushort MsgAck = 0x0305;

        /// <summary>Marcador de OPEN no byte de srcSeat (captura l.12/14: canal ainda sem seat).</summary>
        public const byte OpenMarker = 0xff;

        /// <summary>OPEN do canal (12B) — captura l.12: <c>040304000000ff00e706f802</c>. O joiner manda DOIS
        /// (tokens distintos) antes dos pushes.</summary>
        public static byte[] BuildOpen(uint seq, byte dstSeat, uint token)
        {
            var p = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(p, MsgPush);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), seq);
            p[6] = OpenMarker;
            p[7] = dstSeat;
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), token);
            return p;
        }

        /// <summary>PUSH de sessão (13B) — captura l.16: <c>0403070000000a00f927f8020a</c>. O payload (1B) é o
        /// próprio seat do remetente (joiner manda 0x0a, host manda 0x00).</summary>
        public static byte[] BuildPush(uint seq, byte srcSeat, byte dstSeat, uint token)
        {
            var p = new byte[13];
            BinaryPrimitives.WriteUInt16LittleEndian(p, MsgPush);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), seq);
            p[6] = srcSeat;
            p[7] = dstSeat;
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), token);
            p[12] = srcSeat;
            return p;
        }

        /// <summary>ACK de um OPEN/PUSH recebido: eco do frame com opcode 0x0305 e bytes 6 e 7 = seat do
        /// ACKER (regra cravada da captura l.13/17/19); seq, token e payload ecoados intactos.</summary>
        public static byte[] BuildAck(ReadOnlySpan<byte> frame, byte ackerSeat)
        {
            var p = frame.ToArray();
            p[0] = (byte)(MsgAck & 0xff);
            p[6] = ackerSeat;
            p[7] = ackerSeat;
            return p;
        }

        /// <summary>Seat de DESTINO de um push/open (byte 7) — no host→bot é o seat do bot a ackear.</summary>
        public static byte DstSeat(ReadOnlySpan<byte> frame) => frame[7];

        /// <summary>True se o frame é um push/open 0x0304 com header completo.</summary>
        public static bool IsPush(ReadOnlySpan<byte> frame) =>
            frame.Length >= 12 && frame[0] == 0x04 && frame[1] == 0x03;

        /// <summary>Token opaco por-frame do bot (o cliente real usa ponteiros; o ack só ecoa). Derivado do
        /// seq p/ ser único e determinístico nos testes.</summary>
        public static uint NextToken(uint seq) => 0x02f80000u + seq * 0x9cu;
    }
}
