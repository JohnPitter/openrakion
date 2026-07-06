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

        /// <summary>RET_LOGIN (0x1011) de sucesso — HEADER DE 8 BYTES: <b>[u16 result=0][u16 token][u16 _][u16 count]
        /// [count × registro 0x94]</b>. CRAVADO (2026-07-05, medição de 2 sessões in-game): o cliente lê a base do
        /// registro em <c>payload+8</c> e a contagem em <c>payload+6</c> (loop @100075a4 com EBX=payload+2 →
        /// count@[EBX+4], registros@[EBX+6]). Prova: com header de 6B o nome UTF-16 saía truncado em N chars e o
        /// count (lido dos bytes do próprio registro) inflava a lista com lixo + crash; os dois testes convergiram
        /// em record_base=payload+8. Os 2 bytes @+4 (o "_") completam o header p/ o count cair em +6. Amigos entram
        /// OFFLINE (endereço P2P 0); a presença vem pelo NTF_USER_STATE. token (@+2) ecoado via UDP (brokering P2P).
        /// count cap 500 (@100075ae).</summary>
        public static byte[] LoginList(ushort token, IReadOnlyList<BuddyEntry> buddies)
        {
            int count = Math.Min(buddies.Count, 500);
            using var w = new PacketWriter();
            w.WriteWord(0);          // result = 0 (sucesso) @+0
            w.WriteWord(token);      // @+2 — ecoado via UDP (brokering P2P)
            w.WriteWord(0);          // @+4 — completa o header de 8B (o cliente lê count@+6, registros@+8)
            w.WriteWord(count);      // count @+6
            for (int i = 0; i < count; i++)
                w.WriteBytes(BuddyRecord(buddies[i].Nick, buddies[i].Category));   // registros @+8
            // caminho de SUCESSO do cliente exige payload > 7 bytes; sem amigos o header (8B) já basta, + pad p/ 10.
            if (count == 0) w.WriteWord(0);
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

        /// <summary>RET_REMOVE_BUDDY (0x3003): [u16 result=0][id ASCII 0x14 do amigo removido]. O cliente
        /// (OnMsg @0x3003) casa esse id contra cada linha da lista (strnicmp 0x14) e REMOVE a que bate
        /// (FUN_100088a0). Sem o id (só [u16 0]) o cliente lê lixo do buffer, não casa e NÃO tira a linha da UI —
        /// era a causa do "deletar 2x p/ sumir".</summary>
        public static byte[] RemoveResult(string nick)
        {
            using var w = new PacketWriter();
            w.WriteWord(0);                    // result = 0 (sucesso) @+0
            byte[] id = new byte[IdLen];
            WriteAscii(id, 0, IdLen, nick);    // id ASCII @+2 (0x14) — chave da remoção na UI
            w.WriteBytes(id);
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
