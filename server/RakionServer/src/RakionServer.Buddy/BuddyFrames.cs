using System;
using System.Collections.Generic;
using System.Text;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    /// <summary>Presença de um amigo no NTF_USER_STATE: nick (id de rede), online e endereço P2P (4B network-order
    /// + porta). Ip4 nulo/≠4B = endereço 0 (online mas sem P2P ainda — o endpoint chega depois pelo token UDP).</summary>
    public readonly record struct UserPresence(string Nick, bool Online, byte[]? Ip4, ushort Port);

    /// <summary>
    /// Síntese PURA dos frames servidor->cliente do Buddy2.dll (RET_LOGIN, NTF_USER_STATE). Funções
    /// parâmetros->bytes, sem estado nem I/O — montadas da ESTRUTURA cravada no disasm do OnMsg (FUN_10007420)
    /// + constantes de protocolo. Golden-testável byte-a-byte.
    ///
    /// Refs RE (rakion-work/_dbg/buddyrec_out, buddystate_out): RET_LOGIN loop @100075a4 — count @+4, registros
    /// @+6, 0x94 (148B) cada; registro = id ASCII (FUN_100034f0, @0x00 ≤0x14) + nome UTF-16 (FUN_100097d0,
    /// @0x14 ≤0x14 wide) + grupo UTF-16 (@0x3c) + endereço P2P (@0x64, 0=offline; FUN_10009a40 registra addr 0
    /// sem crashar). NTF_USER_STATE parse @10008340: [u16 count] + por entry [id ASCII 0x14][u8 online]
    /// (+[ip1 4][port1 2][ip2 4][port2 2] se online, network-order).
    /// </summary>
    public static class BuddyFrames
    {
        public const int RecordSize = 0x94;   // 148 bytes por amigo no RET_LOGIN
        public const int IdLen = 0x14;        // id ASCII (20 bytes)
        public const int NameOff = 0x14;      // nome UTF-16
        public const int GroupOff = 0x3c;     // grupo UTF-16
        public const int AddrOff = 0x64;      // endereço P2P (0 = offline)

        /// <summary>RET_LOGIN (0x1011) de sucesso: [u16 result=0][u16 token][u16 count][count × registro 0x94].
        /// Os amigos entram OFFLINE (endereço P2P 0); a presença/endereço vem depois pelo NTF_USER_STATE. O token
        /// (u16 @+2) é ecoado pelo cliente via UDP p/ o brokering P2P aprender o endpoint (o cliente faz sendto dos
        /// 4 bytes iniciais — ver disasm @1000759e). count cap 500 (limite do cliente, @100075ae).</summary>
        public static byte[] LoginList(ushort token, IReadOnlyList<BuddyEntry> buddies)
        {
            int count = Math.Min(buddies.Count, 500);
            using var w = new PacketWriter();
            w.WriteWord(0);          // result = 0 (sucesso)
            w.WriteWord(token);      // token ecoado via UDP (brokering P2P)
            w.WriteWord(count);
            for (int i = 0; i < count; i++)
                w.WriteBytes(BuddyRecord(buddies[i].Nick, buddies[i].Category));
            // o caminho de SUCESSO do cliente (RET_LOGIN END) exige payload > 7 bytes; sem amigos o frame teria só
            // 6 bytes -> pad p/ 10 (o stub mandava 8). Com amigos, os registros de 148B já garantem o tamanho.
            if (count == 0) w.WriteUInt32(0);
            return w.ToArray();
        }

        /// <summary>Registro de amigo de 148 bytes (0x94): id ASCII @0x00, nome UTF-16 @0x14, grupo UTF-16 @0x3c,
        /// endereço P2P @0x64 = 0 (offline). O cliente lê id/nome até o NUL; o resto é zero-pad determinístico.</summary>
        public static byte[] BuddyRecord(string nick, string group)
        {
            byte[] rec = new byte[RecordSize];
            WriteAscii(rec, 0, IdLen, nick);
            WriteWide(rec, NameOff, GroupOff - NameOff, nick);   // nome UTF-16 (cabe até GroupOff)
            // grupo UTF-16: NUNCA vazio. A UI do messenger (F9) agrupa os amigos por grupo (FUN_10003330
            // "copy entry -> group node"); um grupo "" (len=0) é o único campo string que o servidor original nunca
            // mandava vazio -> default não-vazio p/ a árvore de amigos não cair ao renderizar a lista.
            WriteWide(rec, GroupOff, AddrOff - GroupOff, string.IsNullOrEmpty(group) ? DefaultGroup : group);
            return rec;   // AddrOff.. já é 0 (offline)
        }

        /// <summary>Grupo default do amigo quando a buddylist.Category está vazia (a UI não tolera grupo "").</summary>
        public const string DefaultGroup = "Friends";

        /// <summary>NTF_USER_STATE (0x3fff): [u16 count] + por amigo [id ASCII 0x14][u8 online]; se online, +
        /// [ip1 4][port1 2][ip2 4][port2 2] (network-order). O cliente (SetUserOnline FUN_100038e0) só ATIVA o P2P
        /// se ip1==ip2 && port1==port2 -> repetimos o MESMO endpoint nos 2 pares. entry online=0x21B / offline=0x15B.</summary>
        public static byte[] UserState(IReadOnlyList<UserPresence> entries)
        {
            using var w = new PacketWriter();
            w.WriteWord(entries.Count);
            foreach (var e in entries)
            {
                byte[] id = new byte[IdLen];
                WriteAscii(id, 0, IdLen, e.Nick);
                w.WriteBytes(id);
                w.WriteByte(e.Online ? 1 : 0);
                if (e.Online)
                    for (int pair = 0; pair < 2; pair++)   // 2 pares IGUAIS p/ ativar o P2P; network-order
                    {
                        w.WriteBytes(Ip4(e.Ip4));
                        w.WriteByte((byte)(e.Port >> 8));   // porta big-endian (network-order)
                        w.WriteByte((byte)(e.Port & 0xff));
                    }
            }
            return w.ToArray();
        }

        private static byte[] Ip4(byte[]? ip) => ip is { Length: 4 } ? ip : new byte[4];

        /// <summary>Escreve ASCII em [off..off+max), deixando ao menos 1 NUL terminador; resto fica 0.</summary>
        private static void WriteAscii(byte[] dst, int off, int max, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s ?? "");
            Array.Copy(b, 0, dst, off, Math.Min(b.Length, max - 1));
        }

        /// <summary>Escreve UTF-16LE em [off..off+max), deixando 2 bytes de NUL wide; resto fica 0.</summary>
        private static void WriteWide(byte[] dst, int off, int max, string s)
        {
            byte[] b = Encoding.Unicode.GetBytes(s ?? "");   // UTF-16LE (DISPLAY name, wide)
            Array.Copy(b, 0, dst, off, Math.Min(b.Length, max - 2));
        }
    }
}
