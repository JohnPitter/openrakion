using System;
using System.Text;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World
{
    /// <summary>
    /// SERIALIZAÇÃO dos frames de ROSTER da sala (0x37 room-state, 0x38 member-join, registro de jogador
    /// FUN_0040b7f0) — golden source, usada por QUALQUER ocupante (host/humano/bot). NÃO é bot-específico: o
    /// join de humano (<see cref="TryJoinRoom"/>) e o render do bot (<see cref="BotManager"/>) montam os MESMOS
    /// frames a partir do estado do <see cref="Domain.Field"/>. Layout cravado byte-a-byte da captura do original.
    /// </summary>
    public sealed partial class WorldServer
    {
        /// <summary>Tamanho FIXO do registro de jogador = 88B, o MESMO no member-join (0x38) e no roster (0x37): o
        /// cliente usa o mesmo parser e avança um passo constante por slot. Encurtar o roster p/ 78B CRASHAVA o
        /// cliente in-game (2026-07-03); record variável (crescia com o nome) desalinhava (card fantasma).</summary>
        private const int PlayerRecordLen = 88;

        /// <summary>userid sintético do bot no frame (faixa alta p/ não colidir com usuários reais 1..MaxUser). É
        /// detalhe de SERIALIZAÇÃO (como um bot é codificado no frame), por isso mora aqui e não no BotManager.</summary>
        internal static ushort BotUserId(int seat) => (ushort)(0xB000 + seat);

        /// <summary>Userid do ocupante no frame (bot = faixa alta 0xB000+seat; humano = usergameinfo.id).</summary>
        private static ushort RecUid(Domain.PlayerRec rec) =>
            rec.Bot != null ? BotUserId(rec.Slot) : (ushort)(rec.Session?.GameInfoId ?? 0);

        /// <summary>Registro de jogador (FUN_0040b7f0) de QUALQUER ocupante (host/humano/bot) NO ROSTER do 0x37.
        /// MESMO formato FIXO de 88B do member-join (0x38): o cliente usa o MESMO parser p/ os dois. A tentativa
        /// de encurtar p/ 78B desalinhava e CRASHAVA o cliente in-game (2026-07-03) — o record do roster é 88B.</summary>
        private static byte[] RecordFor(Domain.PlayerRec rec) =>
            rec.Bot != null
                ? BuildPlayerRecord(rec.Bot.Name, (byte)rec.Bot.CharClass, (byte)rec.Bot.Level)
                : BuildPlayerRecord(rec.Session?.CharName ?? "", (byte)(rec.Session?.CharClass ?? 0),
                                    (byte)(rec.Session?.CharLevel ?? 1), 0, rec.Session?.UdpEndpoint);   // endereço P2P do peer

        /// <summary>
        /// 0x37 = estado COMPLETO da sala p/ o JOINER (worldserv FUN_00406f40 @0x407280..0x407360). É o frame
        /// que TRANSICIONA o cliente do game-list p/ a tela da sala (RED/BLUE). Enviado SÓ ao joiner (LOBBY).
        /// Header 16B + nome\0 + senha\0 + desc\0 + roster (20 slots: [state]; se ocupado += [uid u16][team]
        /// [registro FUN_0040b7f0]).
        /// </summary>
        internal static byte[] BuildRoomState(Domain.Field f)
        {
            using var w = new PacketWriter();
            // Header de 16B CRAVADO da captura do original (orig_capture2, S>C 0x37 ao joiner):
            //   37 00 | 00 00 | 01 00 | d2 03 | 01 | 0a | 00 | 01 | 0b | 2c 01 | 14
            // O MASTER SLOT vem em +2 (NÃO o fieldId, que fica em +6). Isto é o que conserta o bug do "joiner vira
            // master": o joiner processa o PRÓPRIO 0x38 (master=0x14 sem-init -> self-designa) ANTES do 0x37; o 0x37
            // é quem CORRIGE o master lendo-o de +2. Eu escrevia o fieldId em +2, então o cliente nunca lia o master
            // real (0) e o joiner ficava com START. Campos +8.. inferidos do bloco de params compartilhado 0x36/0x37
            // (`d2 03 01 0a 00 01 0b` = id, mode, maxPlayers, map, minLvl, maxLvl); todos vêm do DOMÍNIO.
            w.WriteWord(0x37);                          // +0  opcode
            w.WriteByte((byte)(f.MasterSlot & 0xff));   // +2  MASTER slot (this+0x121) — cap 00
            w.WriteByte(0);                             // +3  pad (high byte do master u16)
            w.WriteByte(f.State);                       // +4  field state (this+8) — cap 01
            w.WriteByte(0);                             // +5  pad
            w.WriteWord((ushort)f.Id);                  // +6  fieldId (*(u16)this) — cap d2 03
            w.WriteByte(f.Mode);                        // +8  mode (this+0x119) — cap 01
            w.WriteByte(f.MaxPlayers);                  // +9  maxPlayers (this+0x114) — cap 0a
            w.WriteByte(f.MapId);                       // +a  map (this+0x118) — cap 00
            w.WriteByte(f.MinLevel);                    // +b  minLevel (this+0x111) — cap 01
            w.WriteByte(f.MaxLevel);                    // +c  maxLevel (this+0x112) — cap 0b
            w.WriteWord(f.MapSlot);                     // +d  mapSlot (this+0x11c) — cap 2c 01
            w.WriteByte(f.FragLimit);                   // +f  fragLimit (this+0x11e) — cap 14
            w.WriteCString(f.Name);                     // nome\0  (this+0x16)
            w.WriteCString(f.Password);                 // senha\0 (this+0x3f)
            w.WriteCString("");                         // desc\0  (this+0x48) vazio
            foreach (var rec in f.Slots)                // roster: 20 slots (this+0x126, stride 0x14)
            {
                // Ocupante REAL: sessão CONECTADA com char carregado (nome não-vazio) OU bot. Sessão desconectada/sem
                // char não conta (senão vira card FANTASMA Lv0/vazio no roster).
                bool occ = rec.State != 0 && rec.State != 5 &&
                           ((rec.Session != null && rec.Session.Connected && !string.IsNullOrEmpty(rec.Session.CharName)) || rec.Bot != null);
                // wire: 5 = locked; 1 = ocupado (com registro a seguir); 0 = vazio. TEM de bater com occ — escrever
                // wire=1 sem registro desalinhava o cliente (lia o próximo slot como registro) = card fantasma.
                byte wire = rec.State == 5 ? (byte)5 : occ ? (byte)1 : (byte)0;
                w.WriteByte(wire);                      // [state] sempre
                if (occ)
                {
                    w.WriteWord(RecUid(rec));           // [uid u16]  (this+0x124)
                    w.WriteByte(rec.Team);              // [team/flag] (this+0x127)
                    w.WriteBytes(RecordFor(rec));       // [registro] (FUN_0040b7f0)
                }
            }
            return w.ToArray();
        }

        /// <summary>
        /// Member-join 0x38 GENÉRICO (bot OU humano — golden source): [38 00][status][slot][state][uid:u16]
        /// [slotFlag][registro], len = registroLen + 8. Usado tanto pelo roster do bot quanto pelo join
        /// (0x38) de um humano que entra na sala (ver <see cref="TryJoinRoom"/>).
        /// </summary>
        internal static byte[] BuildMemberJoin(string name, byte charClass, byte level, ushort uid, int seat,
                                               byte state = 1, byte slotFlag = 0, System.Net.IPEndPoint? peerEp = null)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x38);              // +0  opcode (u16)
            w.WriteByte(0);                 // +2  status = 0 (sucesso)
            w.WriteByte((byte)seat);        // +3  slot (0..0x13; 10..0x13 = BLUE)
            w.WriteByte(state);             // +4  state (1 = na sala; Ready exige player+0x1ac ∈ {1,2})
            w.WriteWord(uid);               // +5  userid (u16 LE)
            w.WriteByte(slotFlag);          // +7  slotFlag (game+0x127; 0 = não travado)
            w.WriteBytes(BuildPlayerRecord(name, charClass, level, 0, peerEp));   // +8  registro (FUN_0040b7f0) — c/ endereço P2P do peer
            return w.ToArray();
        }

        /// <summary>Endereço UDP do peer no registro (P2P DIRETO) — 2 pares [IP u32][port u16] em NETWORK ORDER
        /// (big-endian): externo + loopback. CRAVADO da captura do original (o 0x38/0x37 carregam
        /// `ac110001 d631 7f000001 08fd(=2301)`). Sem isto o cliente não sabe onde está o outro e fica "fantasma"
        /// (o movimento 0x30a é P2P direto 2301↔2302, NÃO passa pelo servidor). null = zeros (bot/sem endpoint).</summary>
        private static void WritePeerAddr(PacketWriter w, System.Net.IPEndPoint? ep)
        {
            if (ep == null) { w.WriteBytes(new byte[12]); return; }
            byte[] ip = ep.Address.MapToIPv4().GetAddressBytes();       // 4B network order
            byte hi = (byte)(ep.Port >> 8), lo = (byte)ep.Port;         // porta big-endian (network order)
            w.WriteBytes(ip); w.WriteByte(hi); w.WriteByte(lo);         // IP1:port1 = endereço do peer
            w.WriteBytes(new byte[] { 127, 0, 0, 1 }); w.WriteByte(hi); w.WriteByte(lo);  // IP2:port2 = loopback (mesma máquina)
        }

        /// <summary>
        /// Registro de jogador — espelho EXATO de FUN_0040b7f0: [nome\0][tag\0][slotInBlob] + [ENDEREÇO UDP do peer]
        /// + class/level + cauda de equip default (11B). TAMANHO FIXO de <see cref="PlayerRecordLen"/>=88B: o mesmo
        /// parser do cliente lê o record no 0x38 (member-join) E no 0x37 (roster), então o passo por slot tem de ser
        /// CONSTANTE 88B, independente do nome — record variável (crescia com o nome) desalinhava os slots seguintes
        /// (card FANTASMA); encurtar p/ 78B CRASHAVA o cliente (2026-07-03). O ENDEREÇO P2P (ver
        /// <see cref="WritePeerAddr"/>) é o que o cliente usa p/ o 0x30a direto; a cauda de 11B é o observer-fix
        /// (sem ela o 2º humano vira "observador" no stage).
        /// </summary>
        internal static byte[] BuildPlayerRecord(string name, byte charClass, byte level, byte slotInBlob = 0,
                                                 System.Net.IPEndPoint? peerEp = null)
        {
            using var w = new PacketWriter();
            w.WriteBytes(Encoding.ASCII.GetBytes(name ?? "")); w.WriteByte(0);  // nome + NUL  (+0x14a8)
            w.WriteByte(0);                     // tag/clan vazio + NUL        (+0x14c2)
            w.WriteByte(slotInBlob);            // +0x1478 (slot p/ o 0x4b; 0 no 0x38/0x4c, onde o slot vai no header)
            WritePeerAddr(w, peerEp);           // +0x1450 [IP1 u32][port1 u16] +0x1454 [IP2 u32][port2 u16] = ENDEREÇO P2P
            w.WriteByte(charClass);             // +0x1530  CLASSE
            w.WriteByte(level);                 // +0x1531  LEVEL
            w.WriteByte(0);                     // +0x1473
            byte[] content = w.ToArray();
            var rec = new byte[PlayerRecordLen];
            Array.Copy(content, rec, Math.Min(content.Length, PlayerRecordLen));   // conteúdo no início; resto = zero-pad
            // bloco de equip DEFAULT nos ÚLTIMOS 11B (offset fixo) — observer-fix.
            byte[] tail = { 0x11, 0x00, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Array.Copy(tail, 0, rec, PlayerRecordLen - tail.Length, tail.Length);
            return rec;
        }
    }
}
