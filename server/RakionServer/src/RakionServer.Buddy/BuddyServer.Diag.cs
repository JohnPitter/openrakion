using System;
using System.Buffers.Binary;
using System.Text;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Diagnóstico do fluxo de convite/add P2P (SEM relay). O add retail é P2P: A manda
    /// P2P_SVC_SEND_INVITATION (0xc012) / P2P_SVC_ADDBUDDY (0xc041) DIRETO a B; quando não alcança o peer,
    /// o cliente embrulha o payload num SVC_TUNNEL_PACKET (0x2020) p/ o servidor relayar (0x2021) — que a
    /// gente NÃO faz (regra "sem relay"). Este decode NÃO relaya: só REVELA o que o tunnel carrega (opcode
    /// P2P + nick alvo + endpoint), p/ cravar por que o P2P direto falhou e como A obtém o endpoint de B.
    /// Ver docs/protocol-buddy.md (§ Convite/add P2P) e [[messenger-pm-p2p-nada-de-relay]].
    /// </summary>
    public sealed partial class BuddyServer
    {
        /// <summary>Nomes dos opcodes P2P (CCommP2P dispatch do Buddy2.dll, switch(op&0xffff)).</summary>
        private static string P2pName(ushort op) => op switch
        {
            0xc011 => "P2P_SVC_SEND_MSG",   0xc012 => "P2P_SVC_SEND_INVITATION",
            0xc013 => "P2P_RET_SEND_INVITATION", 0xc015 => "P2P_SVC_SEND_SMS",
            0xc018 => "P2P_SVC_SEND_GIFTMSG", 0xc041 => "P2P_SVC_ADDBUDDY",
            0xc042 => "P2P_RET_ADDBUDDY",   0xc043 => "P2P_SVC_REMOVEBUDDY",
            0xc051 => "P2P_NTF_STATE",      0xc053 => "P2P_SVC_STATE",
            _ => $"P2P_0x{op:x4}",
        };

        /// <summary>Loga o conteúdo de um SVC_TUNNEL_PACKET (0x2020) sem relayar: full hex + o 1º opcode P2P
        /// (0xc0xx) achado + o nick ASCII embutido + qualquer par ip:port plausível. É a sonda que crava o
        /// destino que o P2P direto tentou (e por que caiu no tunnel).</summary>
        private void DiagTunnelPacket(BuddyConn conn, byte[] payload)
        {
            Log.Info("buddy", "[{0}] TUNNEL(0x2020) {1}B hex={2}", conn.Ip, payload.Length,
                Convert.ToHexString(payload));

            for (int i = 0; i + 2 <= payload.Length; i++)
            {
                ushort op = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(i));
                if ((op & 0xff00) == 0xc000)
                {
                    Log.Info("buddy", "[{0}] TUNNEL -> P2P op=0x{1:x4} ({2}) @off {3}", conn.Ip, op, P2pName(op), i);
                    break;
                }
            }

            string ascii = AsciiRun(payload);
            if (ascii.Length >= 2) Log.Info("buddy", "[{0}] TUNNEL -> nick/ascii='{1}'", conn.Ip, ascii);

            // par ip:port plausível (4B ip com 1º octeto não-zero + porta 1024..65535 BE ou LE)
            for (int i = 0; i + 6 <= payload.Length; i++)
            {
                byte a = payload[i];
                if (a is 10 or 127 or 172 or 192)   // faixas privadas/loopback comuns no teste
                {
                    ushort be = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(i + 4));
                    ushort le = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(i + 4));
                    Log.Info("buddy", "[{0}] TUNNEL -> ip?={1}.{2}.{3}.{4} port(be)={5} port(le)={6} @off {7}",
                        conn.Ip, payload[i], payload[i + 1], payload[i + 2], payload[i + 3], be, le, i);
                }
            }
        }

        /// <summary>Maior sequência ASCII imprimível (nick alvo embutido no tunnel).</summary>
        private static string AsciiRun(byte[] b)
        {
            int bestStart = 0, bestLen = 0, curStart = 0, curLen = 0;
            for (int i = 0; i < b.Length; i++)
            {
                if (b[i] >= 0x20 && b[i] < 0x7f) { if (curLen++ == 0) curStart = i; }
                else { if (curLen > bestLen) { bestLen = curLen; bestStart = curStart; } curLen = 0; }
            }
            if (curLen > bestLen) { bestLen = curLen; bestStart = curStart; }
            return bestLen >= 2 ? Encoding.ASCII.GetString(b, bestStart, bestLen) : "";
        }
    }
}
