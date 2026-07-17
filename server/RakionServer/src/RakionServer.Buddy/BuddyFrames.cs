using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Síntese pura dos frames do messenger (servidor->cliente), com o layout CRAVADO por RE do Buddy2.dll
    /// (dispatcher CBuddy2::OnMsg FUN_10007420 + auxiliares). Classe sem estado/IO -> golden-testável
    /// (ver BuddyFrameGoldenTests). O messenger é P2P puro (SEM relay): o servidor só faz BROKERING de
    /// endereços — entrega a lista de amigos e anuncia o endpoint UDP de cada um; as mensagens correm
    /// direto cliente-a-cliente (UDP cifrado), nunca pelo servidor.
    /// </summary>
    public static class BuddyFrames
    {
        public const int BuddyRecordSize = 0x94;   // registro de amigo do RET_LOGIN

        /// <summary>RET_LOGIN: [u16 result=0][u32 token][u16 count][count x registro 0x94]. RE: FUN_10007420
        /// @100075a4 (count@+6) e o loop @100075d0 (registros@+8). O <paramref name="token"/> (body[2:6]) é o
        /// que o cliente ECOA via sendto UDP (RET_LOGIN @100759e) -> registra o endpoint P2P no servidor.
        /// Cap 500 (o cliente clampa AX a 0x1f4).</summary>
        public static byte[] LoginList(IReadOnlyList<BuddyEntry> buddies, uint token)
        {
            int n = Math.Min(buddies.Count, 500);
            byte[] body = new byte[8 + n * BuddyRecordSize];
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2), token);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), (ushort)n);
            for (int i = 0; i < n; i++)
                BuddyRecord(buddies[i], body.AsSpan(8 + i * BuddyRecordSize, BuddyRecordSize));
            return body;
        }

        /// <summary>Registro de amigo de 0x94 (RE: loop @100075d0): [0x00] id ASCII 0x14 (FUN_100034f0, byte
        /// a byte) · [0x14] nome UTF-16 0x14 (FUN_100097d0, wide = display) · [0x3c] grupo UTF-16 0x14 ·
        /// [0x64..0x94] endereço P2P = 0 (FUN_10009a40 registra o user OFFLINE; a presença vem no NTF). Id = nick.</summary>
        public static void BuddyRecord(BuddyEntry b, Span<byte> rec)
        {
            WriteAscii(rec, 0x00, b.Nick, 0x14);
            WriteWide(rec, 0x14, b.Nick, 0x14);
            WriteWide(rec, 0x3c, b.Category, 0x14);
            // 0x64..0x94 = 0 -> OFFLINE
        }

        /// <summary>NTF_USER_STATE (0x3fff): [u16 count=1][id ASCII 0x14][u8 online]. Se online, +bloco de
        /// endereço P2P de 12B (RE disasm @10008340): [ip1 4B][port1 2B][ip2 4B][port2 2B], todos NETWORK order
        /// (vão direto p/ sockaddr). SetUserOnline (FUN_100038e0) só ativa o P2P quando ip1==ip2 && port1==port2,
        /// então repetimos o endpoint nos dois pares. Offline = só id+flag (0x15B).</summary>
        public static byte[] UserState(string nick, bool online, byte[]? ip4 = null, ushort port = 0)
        {
            byte[] body = new byte[2 + 0x14 + 1 + (online ? 12 : 0)];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), 1);
            WriteAscii(body, 2, nick, 0x14);
            body[2 + 0x14] = (byte)(online ? 1 : 0);
            if (online && ip4 != null)
            {
                WriteAddr(body, 2 + 0x15, ip4, port);   // par 1 (ip1/port1)
                WriteAddr(body, 2 + 0x1b, ip4, port);   // par 2 (ip2/port2) == par 1 -> ativa P2P
            }
            return body;
        }

        /// <summary>Escreve [ip 4B network-order][port 2B network-order] — formato sockaddr que o cliente
        /// copia direto p/ o endereço do peer (FUN_100052e0). ip4 = IPAddress.GetAddressBytes() (já net order).</summary>
        private static void WriteAddr(Span<byte> dst, int off, byte[] ip4, ushort port)
        {
            dst[off + 0] = ip4[0]; dst[off + 1] = ip4[1]; dst[off + 2] = ip4[2]; dst[off + 3] = ip4[3];
            dst[off + 4] = (byte)(port >> 8); dst[off + 5] = (byte)(port & 0xff);
        }

        private static void WriteAscii(Span<byte> dst, int off, string s, int max)
        {
            for (int i = 0; i < s.Length && i < max - 1; i++) dst[off + i] = (byte)s[i];   // NUL-terminado (resto já 0)
        }

        private static void WriteWide(Span<byte> dst, int off, string s, int max)
        {
            for (int i = 0; i < s.Length && i < max - 1; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(off + i * 2), s[i]);
        }
    }
}
