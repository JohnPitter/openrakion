using System;
using System.Text;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Renderização do bot no ROSTER da sala (os slots RED/BLUE do game room). SÍNTESE do member-join do
    /// worldserv ORIGINAL (RE em rakion-work/ghidra-proj/joinflow*.out.txt + joinasm.out.txt):
    ///   - broadcast 0x38 montado em FUN_00406f40 @0x40735a (header 8B + registro);
    ///   - registro de jogador serializado por FUN_0040b7f0 @0x40b7f0 ([nome\0][tag\0] + cauda fixa 0x49B).
    /// O cliente JÁ tem o caminho de RECEBER 0x38 (quando um humano entra na sala), então o frame sintetizado
    /// do estado do bot o desenha no slot — não é replay de captura.
    /// </summary>
    public sealed partial class WorldServer
    {
        /// <summary>userid sintético do bot no frame (faixa alta p/ não colidir com usuários reais 1..MaxUser).</summary>
        private static ushort BotUserId(int seat) => (ushort)(0xB000 + seat);

        /// <summary>
        /// Member-join 0x38 ao host: faz o cliente desenhar o bot no slot. RE (FUN_00406f40 @0x40735a, offsets
        /// confirmados pelas anotações Stack[] do disassembly): [38 00][status][slot][state][uid:u16][slotFlag]
        /// [registro], len = registroLen + 8.
        /// </summary>
        private void NotifyBotJoinedRoom(Domain.Field f, BotPlayer bot, int seat, ClientSession host)
        {
            try { host.SendLobby(BuildRoomMemberJoin(bot, seat)); }
            catch (Exception ex) { Log.Debug("bot", "roster 0x38 falhou: {0}", ex.Message); return; }
            Log.Ok("bot", "roster: 0x38 member-join '{0}' seat {1} (cls {2} lvl {3}) -> host [{4}]",
                bot.Name, seat, bot.CharClass, bot.Level, host.Slot);
        }

        /// <summary>Member-leave 0x3a ao host: esvazia o slot do bot. RE FUN_004091e0 @0x409403: [3a 00][slot], len 3.</summary>
        private void NotifyBotLeftRoom(Domain.Field f, int seat, ClientSession host)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3a);
            w.WriteByte((byte)seat);
            try { host.SendLobby(w.ToArray()); } catch { }
            Log.Debug("bot", "roster: 0x3a member-leave seat {0} -> host [{1}]", seat, host.Slot);
        }

        /// <summary>
        /// Spawn 3D do bot no STAGE — emite o 0x4b AddPlayer (canal FIELD via SendMessage; NÃO o LOBBY) que
        /// INSTANCIA o avatar no host. O corpo (decode byte-a-byte da captura MITM) é sintetizado pelo codec
        /// <see cref="BotMovement.BuildStageAddPlayer"/>; aqui só se traduz estado→chamada e serializa.
        /// </summary>
        private void NotifyBotAddPlayer(Domain.Field f, int seat, BotPlayer bot)
        {
            var host = f.Master;
            if (host == null) return;
            try { host.SendMessage(0x4b, BotMovement.BuildStageAddPlayer(bot, seat)); } catch { return; }
            Log.Ok("bot", "stage: 0x4b AddPlayer seat {0} (cls {1} lvl {2}) -> host [{3}]",
                seat, bot.CharClass, bot.Level, host.Slot);
        }

        /// <summary>
        /// Spawna os bots no STAGE no load do host (chamado do StartGameClock/0x4b). Sequência CRAVADA da
        /// captura MITM real (stage_capture.txt): o servidor manda 0x48 FieldGameStart UMA vez e, em seguida,
        /// 0x4b AddPlayer por bot — no canal FIELD. O 0x4b é o que INSTANCIA o avatar 3D do bot no mundo do
        /// host. Gated por ClientFramesEnabled.
        /// </summary>
        public void SpawnFieldBotsInStage(Domain.Field f)
        {
            if (!BotMovement.ClientFramesEnabled || f.BotCount == 0) return;
            var host = f.Master;
            if (host == null) return;
            try { host.SendMessage(0x48, BotMovement.BuildFieldGameStart()); } catch { }   // round-load (1x)
            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot!;
                bot.SpawnedThisRound = true;
                bot.SpawnedMs = Environment.TickCount64;
                bot.InitStagePosition();
                rec.State = 4; rec.Dead = false; bot.Dead = false;
                bot.SpawnGen++;                                  // nova entidade na engine -> a ponte invalida o cache e re-acha (anti-crash)
                if (BotMovement.UseNpcAvatar)
                {
                    SpawnBotAsNpc(f, rec, bot);                  // 0x307 NPC (descartado: classes auto-vivas não registram)
                }
                else
                {
                    NotifyBotAddPlayer(f, rec.Slot, bot);        // 0x45 fantasma CPlayer (RENDERIZA) — movido pela ponte de injeção
                    EnsureBotPeerConnected(f, rec, bot);
                }
            }
        }

        /// <summary>Header do 0x38 (8B) + registro do jogador. Veja o disassembly em joinasm.out.txt.</summary>
        private static byte[] BuildRoomMemberJoin(BotPlayer bot, int seat) =>
            BuildMemberJoin(bot.Name, (byte)bot.CharClass, (byte)bot.Level, BotUserId(seat), seat);

        /// <summary>Userid do ocupante no frame (bot = faixa alta 0xB000+seat; humano = usergameinfo.id).</summary>
        private static ushort RecUid(Domain.PlayerRec rec) =>
            rec.Bot != null ? BotUserId(rec.Slot) : (ushort)(rec.Session?.GameInfoId ?? 0);

        /// <summary>Registro de jogador (FUN_0040b7f0) de QUALQUER ocupante (host/humano/bot) NO ROSTER do 0x37.
        /// rosterForm=true: 10 bytes MAIS CURTO que o registro do 0x38 member-join (78B vs 88B p/ nome de 2 chars),
        /// cravado da captura (o parse do roster no 0x37 e do member-join no 0x38 sao funcoes diferentes com caudas
        /// diferentes). Usar a forma de 88B aqui alonga cada slot -> desalinha os slots seguintes = card fantasma.</summary>
        private static byte[] RecordFor(Domain.PlayerRec rec) =>
            rec.Bot != null
                ? BuildPlayerRecord(rec.Bot.Name, (byte)rec.Bot.CharClass, (byte)rec.Bot.Level, rosterForm: true)
                : BuildPlayerRecord(rec.Session?.CharName ?? "", (byte)(rec.Session?.CharClass ?? 0),
                                    (byte)(rec.Session?.CharLevel ?? 1), 0, rec.Session?.UdpEndpoint, rosterForm: true);   // endereço P2P do peer

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
        /// + class/level + cauda de equip (zerada). O ENDEREÇO (4 campos +0x1450/+0x1458/+0x1454/+0x145a) É o que o
        /// original preenche p/ o P2P direto (ver <see cref="WritePeerAddr"/>); eu zerava e por isso os 2 humanos
        /// nunca se achavam. Os 2 blocos de equip vão ZERADOS (sem gear; item id 0 = slot vazio, evita AV no render).
        /// </summary>
        /// <summary>Tamanho FIXO do registro de jogador: o cliente avança um passo constante por slot (cravado da
        /// captura: "JP2"/nome-3 e "JP"/nome-2 têm AMBOS 78B no roster e 88B no member-join). ROSTER (0x37) = 78B;
        /// MEMBER-JOIN (0x38) = 88B (os 10B extras = bloco de equip default nos últimos 11B, do observer-fix).</summary>
        private const int RosterRecordLen = 78, MemberJoinRecordLen = 88;

        internal static byte[] BuildPlayerRecord(string name, byte charClass, byte level, byte slotInBlob = 0,
                                                 System.Net.IPEndPoint? peerEp = null, bool rosterForm = false)
        {
            // Registro de TAMANHO FIXO. O conteúdo (nome\0 + campos) vai no INÍCIO e o resto é zero-pad até o total.
            // CRÍTICO: escrever tamanho VARIÁVEL (nome + pad fixo) alongava o registro p/ nomes >2 chars (ex.:
            // "Heroi2" -> 82B) e DESALINHAVA os slots seguintes do roster 0x37 -> card FANTASMA. O passo por slot
            // tem de ser constante (78/88), independente do tamanho do nome.
            int total = rosterForm ? RosterRecordLen : MemberJoinRecordLen;
            using var w = new PacketWriter();
            w.WriteBytes(Encoding.ASCII.GetBytes(name ?? "")); w.WriteByte(0);  // nome + NUL  (+0x14a8)
            w.WriteByte(0);                     // tag/clan vazio + NUL        (+0x14c2)
            w.WriteByte(slotInBlob);            // +0x1478 (slot p/ o 0x4b; 0 no 0x38/0x4c, onde o slot vai no header)
            WritePeerAddr(w, peerEp);           // +0x1450 [IP1 u32][port1 u16] +0x1454 [IP2 u32][port2 u16] = ENDEREÇO P2P
            w.WriteByte(charClass);             // +0x1530  CLASSE
            w.WriteByte(level);                 // +0x1531  LEVEL
            w.WriteByte(0);                     // +0x1473
            byte[] content = w.ToArray();
            var rec = new byte[total];
            Array.Copy(content, rec, Math.Min(content.Length, total));   // conteúdo no início; resto = zero-pad
            if (!rosterForm)
            {
                // 0x38 MEMBER-JOIN: bloco de equip DEFAULT nos ÚLTIMOS 11B (offset fixo 77). Sem ele o cliente lê o
                // 0x38 curto e o 2º humano vira "observador" no stage (observer-fix). No ROSTER o final é só zero.
                byte[] tail = { 0x11, 0x00, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };
                Array.Copy(tail, 0, rec, total - tail.Length, tail.Length);
            }
            return rec;
        }
    }
}
