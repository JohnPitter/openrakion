using System;
using System.Buffers.Binary;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Codec do LOCKSTEP de sessão P2P da engine (0x0304 push / 0x0305 ack) — usado pelo <see cref="UdpGameplay"/>
    /// para ackear um push endereçado a um BOT no lugar dele. Cravado byte-a-byte da captura de 2 humanos
    /// (docs/p2p-handshake-groundtruth.txt l.12-23): NÃO existe CONNECT SE1 (TAGV/32KB) no fio; o canal é
    /// minúsculo (push 13B + ack por eco).
    ///
    /// O canal reliable ENTRE HUMANOS corre P2P-DIRETO (wiretap 2026-07-10: 2 humanos sem bot = zero tráfego no
    /// servidor). O servidor só toca este canal p/ ackear um push endereçado ao BOT (que não tem cliente); relayar
    /// o canal humano↔humano pelo servidor era o SEQUESTRO que matava o HIT×N (dupla-entrega do estado reliable).
    ///
    /// ACK (captura l.13/17/19): frame ecoado, [0]=05, bytes 6 E 7 = seat do ACKER (não do remetente — o eco-clone
    /// com os seats do remetente fazia o host descartar o ack e re-pushar a cada 5s), token/payload intactos.
    /// </summary>
    public static class BotLockstep
    {
        public const ushort MsgReliableAck = 0x4000;
        public const ushort MsgPush = 0x0304;

        /// <summary>Ack do push (0x0305) — eco com os seats do acker.</summary>
        public const ushort MsgAck = 0x0305;

        /// <summary>Push reliable emitido somente pelo bot para um humano específico.</summary>
        public static byte[] BuildPush(uint sequence, byte botSeat, byte humanSeat, uint token)
        {
            byte[] packet = new byte[13];
            BinaryPrimitives.WriteUInt16LittleEndian(packet, MsgPush);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), sequence);
            packet[6] = botSeat;
            packet[7] = humanSeat;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), token);
            packet[12] = botSeat;
            return packet;
        }

        /// <summary>Confirma uma mensagem de aplicação reliable 0x83xx. Formato cravado da captura
        /// humano↔humano: [u16 0x4000][u32 seq do confirmador][u8 seat][u32 seq confirmada].</summary>
        public static byte[] BuildReliableAck(uint sequence, byte ackerSeat, uint confirmedSequence)
        {
            byte[] packet = new byte[11];
            BinaryPrimitives.WriteUInt16LittleEndian(packet, MsgReliableAck);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), sequence);
            packet[6] = ackerSeat;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(7), confirmedSequence);
            return packet;
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

        /// <summary>Seat de DESTINO de um push/open (byte 7).</summary>
        public static byte DstSeat(ReadOnlySpan<byte> frame) => frame[7];

        /// <summary>True se o frame é um push/open 0x0304 com header completo.</summary>
        public static bool IsPush(ReadOnlySpan<byte> frame) =>
            frame.Length >= 12 && frame[0] == 0x04 && frame[1] == 0x03;
    }
}
