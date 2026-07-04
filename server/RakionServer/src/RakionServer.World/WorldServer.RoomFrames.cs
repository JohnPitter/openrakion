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
        /// <summary>userid sintético do bot no frame (faixa alta p/ não colidir com usuários reais 1..MaxUser). É
        /// detalhe de SERIALIZAÇÃO (como um bot é codificado no frame), por isso mora aqui e não no BotManager.</summary>
        internal static ushort BotUserId(int seat) => (ushort)(0xB000 + seat);

        /// <summary>Userid do ocupante no frame (bot = faixa alta 0xB000+seat; humano = usergameinfo.id).</summary>
        private static ushort RecUid(Domain.PlayerRec rec) =>
            rec.Bot != null ? BotUserId(rec.Slot) : (ushort)(rec.Session?.GameInfoId ?? 0);

        /// <summary>Registro de jogador NO ROSTER do 0x37 (host/humano/bot). MESMA forma VARIÁVEL do 0x38, mas SEM
        /// a cauda de equip de 11B — cravado do 0x37 de 216B da captura: record do roster = nome\0 + 74B fixos
        /// (JP=77B), enquanto o do member-join 0x38 tem os mesmos 74B + cauda (JP=88B). Mandar a cauda no 0x37
        /// alongava cada slot ocupado em 11B → o parse do roster do joiner desalinhava (lia lixo como slot-state).
        /// NOTA: tamanho FIXO por buffer (78/88B) também crashava — a forma é VARIÁVEL (nome até \0).</summary>
        private static byte[] RecordFor(Domain.PlayerRec rec) =>
            rec.Bot != null
                ? BuildPlayerRecord(rec.Bot.Name, (byte)rec.Bot.CharClass, (byte)rec.Bot.Level, equipTail: false)
                : BuildPlayerRecord(rec.Session?.CharName ?? "", (byte)(rec.Session?.CharClass ?? 0),
                                    (byte)(rec.Session?.CharLevel ?? 1), 0, rec.Session?.UdpEndpoint, equipTail: false);

        /// <summary>
        /// 0x37 = estado COMPLETO da sala p/ o JOINER (worldserv FUN_00406f40 @0x407280..0x407360). É o frame
        /// que TRANSICIONA o cliente do game-list p/ a tela da sala (RED/BLUE). Enviado SÓ ao joiner (LOBBY).
        /// Header 16B + nome\0 + senha\0 + desc\0 + 20 slots ([state]; ocupado += [uid u16][slotFlag][registro
        /// SEM cauda]) + 3 slots observer trancados + u32 0. Cravado do 0x37 de 216B da captura.
        /// </summary>
        internal static byte[] BuildRoomState(Domain.Field f)
        {
            using var w = new PacketWriter();
            // Header de 16B CRAVADO da captura (orig_capture2, S>C 0x37 ao joiner; sala Gravity mode3 1~10 12max):
            //   37 00 | 00 00 | 01 00 | d2 | 03 | 01 | 0a | 00 | 01 | 0b | 2c 01 | 14
            // +2 = MASTER slot (validado in-game: conserta o "joiner vira master"/START). A SEMÂNTICA de +6.. foi
            // cravada cruzando o C>S 0x3b (map d2, mode 03, rounds 0b, dur 2c01, frag 14, minLvl 01, maxLvl 0a) com
            // o S>C 0x36 (entry: map=d2, mode=03, 1~10, max=0x0c): **+6 é o MAPA (byte) e +7 o MODE** — NÃO um
            // fieldId u16! Escrever o f.Id ali fazia o joiner ler map=idLow/mode=idHigh (sala Gravity/PvP virava
            // "map 0 mode 0") → cliente TRAVAVA segundos após entrar na sala.
            w.WriteWord(0x37);                          // +0  opcode
            w.WriteByte((byte)(f.MasterSlot & 0xff));   // +2  MASTER slot (this+0x121) — cap 00
            w.WriteByte(0);                             // +3  pad (high byte do master u16)
            w.WriteByte(f.State);                       // +4  field state (this+8) — cap 01
            w.WriteByte(0);                             // +5  pad
            w.WriteByte(f.MapId);                       // +6  map (this+0x118) — cap d2 (210 = Gravity)
            w.WriteByte(f.Mode);                        // +7  mode (this+0x119) — cap 03
            w.WriteByte(f.MinLevel);                    // +8  minLevel (b4 do 0x3b) — cap 01
            w.WriteByte(f.MaxLevel);                    // +9  maxLevel (b5 do 0x3b) — cap 0a
            w.WriteByte(0);                             // +a  cap 00 (mesmo par desconhecido do entry 0x36 [6][7])
            w.WriteByte(1);                             // +b  cap 01
            w.WriteByte(f.MaxRounds);                   // +c  maxRounds (rounds do 0x3b) — cap 0b
            w.WriteWord(f.MapSlot);                     // +d  u16 do 0x3b (duração/level-slot) — cap 2c 01 (300)
            w.WriteByte(f.FragLimit);                   // +f  frag/points limit (b3 do 0x3b) — cap 14
            w.WriteCString(f.Name);                     // nome\0  (this+0x16)
            w.WriteCString(f.Password);                 // senha\0 (this+0x3f)
            w.WriteCString("");                         // desc\0  (this+0x48) vazio
            int openPerTeam = Math.Max(1, f.MaxPlayers / 2);   // cap: max=12 → 6 abertos/time (locked 6-9 e 16-19)
            for (int i = 0; i < f.Slots.Length; i++)    // roster: 20 slots (this+0x126, stride 0x14)
            {
                var rec = f.Slots[i];
                // Ocupante REAL: sessão CONECTADA com char carregado (nome não-vazio) OU bot. Sessão desconectada/sem
                // char não conta (senão vira card FANTASMA Lv0/vazio no roster).
                bool occ = rec.State != 0 && rec.State != 5 &&
                           ((rec.Session != null && rec.Session.Connected && !string.IsNullOrEmpty(rec.Session.CharName)) || rec.Bot != null);
                // wire: 5 = locked; 1 = ocupado (com registro a seguir); 0 = vazio. TEM de bater com occ — escrever
                // wire=1 sem registro desalinhava o cliente (lia o próximo slot como registro) = card fantasma.
                // Slots além da capacidade (índice no bloco do time >= MaxPlayers/2) vêm TRANCADOS no original.
                bool beyondCap = (i % 10) >= openPerTeam;
                byte wire = occ ? (byte)1 : (rec.State == 5 || beyondCap) ? (byte)5 : (byte)0;
                w.WriteByte(wire);                      // [state] sempre
                if (occ)
                {
                    w.WriteWord(RecUid(rec));           // [uid u16]  (this+0x124)
                    w.WriteByte(0);                     // [slotFlag] (this+0x127) — cap 00 MESMO p/ o joiner do BLUE
                    w.WriteBytes(RecordFor(rec));       // [registro] (FUN_0040b7f0, SEM cauda de 11B)
                }
            }
            // Cauda do frame (captura): 3 slots extra trancados (observers) + u32 zero.
            w.WriteByte(5).WriteByte(5).WriteByte(5);
            w.WriteInt32(0);
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
        /// Registro de jogador — espelho de FUN_0040b7f0: [nome\0][tag\0][slotInBlob] + [ENDEREÇO UDP do peer] +
        /// class/level + 2 blocos de equip (zerados) [+ cauda de equip default de 11B]. O cliente lê o record de
        /// forma VARIÁVEL (nome até \0 + resto de tamanho FIXO), então NÃO forçar um total fixo por buffer — tentar
        /// isso (88/78B com a cauda em offset fixo) CRASHAVA o cliente in-game p/ nomes longos (2026-07-04). O
        /// ENDEREÇO P2P (ver <see cref="WritePeerAddr"/>) é o que o cliente usa p/ o 0x30a direto. A cauda de 11B
        /// (observer-fix: sem ela o 2º humano vira "observador" no stage) existe SÓ no record do 0x38 — o roster do
        /// 0x37 vai SEM ela (<paramref name="equipTail"/>=false), cravado da captura (77B vs 88B p/ "JP").
        /// </summary>
        internal static byte[] BuildPlayerRecord(string name, byte charClass, byte level, byte slotInBlob = 0,
                                                 System.Net.IPEndPoint? peerEp = null, bool equipTail = true)
        {
            using var w = new PacketWriter();
            w.WriteBytes(Encoding.ASCII.GetBytes(name ?? "")); w.WriteByte(0);  // nome + NUL  (+0x14a8)
            w.WriteByte(0);                     // tag/clan vazio + NUL        (+0x14c2)
            w.WriteByte(slotInBlob);            // +0x1478 (slot p/ o 0x4b; 0 no 0x38/0x4c, onde o slot vai no header)
            WritePeerAddr(w, peerEp);           // +0x1450 [IP1 u32][port1 u16] +0x1454 [IP2 u32][port2 u16] = ENDEREÇO P2P
            w.WriteByte(charClass);             // +0x1530  CLASSE
            w.WriteByte(level);                 // +0x1531  LEVEL
            w.WriteByte(0);                     // +0x1473
            w.WriteBytes(new byte[0x26]);       // +0x1da4  equip/aparência (38B) — sem gear
            w.WriteBytes(new byte[0x13]);       // +0x1dca  equip2/stat (19B) — sem gear
            if (equipTail)                      // cauda de equip DEFAULT (11B) — SÓ no 0x38 (observer-fix)
                w.WriteBytes(new byte[] { 0x11, 0x00, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 });
            return w.ToArray();
        }
    }
}
