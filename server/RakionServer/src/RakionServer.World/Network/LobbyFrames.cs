using System;
using System.Collections.Generic;
using System.Text;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Síntese dos frames W->C da cadeia LOBBY -> CANAL -> SALA -> STAGE. Funções PURAS
    /// (parâmetros -> bytes): sem estado de sessão e sem I/O, montadas com <see cref="PacketWriter"/>
    /// a partir da ESTRUTURA documentada + CONSTANTES DE PROTOCOLO + DADO DE SESSÃO por parâmetro.
    /// Não há blob de captura: nenhum byte é replay de uma sessão gravada.
    ///
    /// REGRA-MESTRA (RE de worldserv.exe via objdump, 2026-06-18): a cifra de lobby empacota em
    /// BLOCOS DE 12 BYTES e o cliente lê cada frame pelo comprimento REAL que o servidor enviou
    /// (3º arg dos sends FUN_004038e0/FUN_0041b8a0/FUN_0041b940). Os bytes além desse LEN, dentro do
    /// bloco cifrado, são LIXO DE STACK do buffer de envio — VARIAM entre sessões (provado por diff do
    /// golden vs capture_field_entry/mitm_full_113423.log) e o cliente os ignora. Por isso TODO frame é
    /// emitido com seu LEN real + zero-pad até o bloco; nenhum "handle/token" capturado sobrevive.
    ///
    /// Builders cravados da decompilação (FUN @ endereço, LEN real):
    ///   0x10 GameGuard  : nonce de handshake estático (16B); GG neutralizado não valida.
    ///   0x14 SpawnAck   : FUN_0041fef0  LEN=3  [14 00][status].
    ///   0x1e ChannelList: FUN_00404da0  LEN var [1e 00][type][count][nome1\0][nome2\0][N registros]; solo=28.
    ///   0x1f SessionInfo: FUN_00404fc0  erro LEN=3 [1f 00][status]; ok LEN=15 [1f 00][00][00][uid:u16][registro].
    ///   0x36 GameList   : FUN_00422c90 e FUN @0x41c0b7 (FieldPlayerList); LEN começa em 3 e cresce com a lista.
    ///                     Solo = lista vazia (count=0) = LEN=3 [36 00][00].
    ///   0x3b RoomCreate : FUN_00423580  LEN=5  [3b 00][status][seat:u16].
    ///   0x43 MatchStart : FUN_004079d0  LEN=3  [43 00][status] (status 0 = partida inicia).
    ///   0x48 Remaining  : FUN_00408440  LEN=9  [48 00][01][dur+3][2c0][2c1][best t1][best t2].
    ///   0x4a StageEnd   : clear=FUN_00405a90 (2bd=2) / morte=FUN_004087d0 (2bd=1)  LEN=6  [4a 00][2bd][2bf][2c0][2c1].
    /// Registro por-player de 0x1e/0x1f (FUN_0040afb0): [nome\0][class@1531][team@146c][dword@14d0].
    ///
    /// DADO DE SESSÃO: userid e nome (do domínio); tempo restante do stage (duração da sala, dur+3).
    /// VOLTA-À-LISTA (pós-0x44/pós-clear): o original RE-MANDA os MESMOS 0x1f/0x1e/0x36 da entrada — confirmado
    /// em capture_field_entry/mitm_move_133859.log (l.460/461 == entrada l.19/20, só os bytes de lixo diferem).
    /// Não há frame "clear" distinto nem handle de sessão na cauda: tudo é o frame de entrada sintetizado.
    /// </summary>
    public static class LobbyFrames
    {
        /// <summary>Canal único do mundo offline (0x1e). Texto C terminado em nul.</summary>
        public const string ChannelName = "dchannel01";

        /// <summary>0x10 GameGuard challenge (16B). IDÊNTICO em A=B=C -> constante (cliente GG-neutralizado
        /// não valida o conteúdo; é um nonce de handshake de forma fixa).</summary>
        private static readonly byte[] GameGuardChallenge =
            { 0x4e, 0x95, 0xdd, 0x29, 0xce, 0x3a, 0x55, 0xdb, 0x20, 0xb6, 0xad, 0x97, 0xa6, 0x5c, 0xc0, 0x1c };

        /// <summary>Registro do player no 0x1f/0x1e (FUN_0040afb0), na ordem [nome-NUL][class@1531][team@146c]
        /// [dword@14d0]. O nome (2B) é escrito antes; aqui vêm o NUL terminador + os campos do char solo
        /// (class=1, team/stats=0).</summary>
        private static readonly byte[] PlayerRecordTail = { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };

        /// <summary>Resposta do 0x19 CharacterGetUserName (messenger "add buddy"): o WORLD informa o account-id
        /// do dono de um nick, que o cliente exige antes de adicionar amigo — sem isso ele trava em "Waiting for
        /// ID Information on account from server" (lang 599). O pedido carrega só o nick; o cliente NÃO valida o
        /// accountId, só o status byte.
        /// Layout: [u16 0x19 opcode][u16 0x0D subtype][byte status][accountId\0][buddyName\0]. status 0=ok, 1=erro, 2=nao existe.
        /// O opcode do frame ecoa o do pedido (no world, request==response: 0x14->0x14, 0x36->0x36; 0x0D sozinho
        /// colidiria com o char-data 0x0D do login). RE: DBCommandCharacterGetUserName @worldserv 0x413980.</summary>
        public static byte[] GetUserNameResult(byte status, string accountId, string buddyName)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x0019);   // opcode do frame (ecoa o pedido)
            w.WriteWord(0x000D);   // subtype interno (do worldserv 0x413980, +2 do buffer)
            w.WriteByte(status);
            w.WriteCString(accountId);
            w.WriteCString(buddyName);
            return w.ToArray();
        }

        /// <summary>Resposta do 0x15 CharacterChangeBuddyName (nick change do messenger): o WORLD confirma a troca
        /// do nick do messenger (usergameinfo.buddyname). Sem ela o cliente trava em "Waiting for change request of
        /// character's name for messenger from server" (lang 604). Layout (RE worldserv FUN_004137a0, tamanho =
        /// strlen+6 = 2+2+1+strlen+1): [u16 0x15 opcode][u16 0x0B subtype][byte status][nick\0]. status 0=ok, 1=erro.
        /// Espelha o 0x19 (opcode ecoa o pedido; o subtype 0x0B é o uStack_100c._2_2_ do worldserv).</summary>
        public static byte[] ChangeBuddyNameResult(byte status, string nick)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x0015);   // opcode do frame (ecoa o pedido)
            w.WriteWord(0x000B);   // subtype interno (worldserv FUN_004137a0: uStack_100c._2_2_ = 0xb)
            w.WriteByte(status);
            w.WriteCString(nick);
            return w.ToArray();
        }

        // ---- BUILDERS ----------------------------------------------------------------------------------

        /// <summary>0x10 GameGuard challenge: [10 00][nonce 16B][00 x6]. Tudo constante.</summary>
        public static byte[] GameGuard()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x10);
            w.WriteBytes(GameGuardChallenge);
            w.WriteBytes(new byte[6]);
            return w.ToArray();
        }

        /// <summary>0x14 spawn/start-ack. RE FUN_0041fef0 (@0x41fef0, linha 755): a resposta REAL tem LEN=3
        /// = [14 00][status=0]. O scoring é armado no HANDLER (FUN_0040ac30), NÃO neste frame; o
        /// [20000000][handle] do blob antigo era LIXO DE STACK (padding do bloco de 12B). 3 reais + zero-pad.</summary>
        public static byte[] SpawnAck()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x14);
            w.WriteByte(0);                 // status (0 = sucesso)
            w.WriteBytes(new byte[9]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x36 game-list (FieldPlayerList: FUN_00422c90 e o 2º builder @0x41c0b7). No original é a
        /// LISTA DE SALAS/PLAYERS de tamanho VARIÁVEL ([36 00][count][por entrada ...]); o LEN começa em 3 e
        /// só cresce com entradas. No mundo SOLO a lista está vazia -> count=0 -> LEN=3 = [36 00][00]. As caudas
        /// `20000000`/`648ce806`/`6a4dcaaf...` das 3 capturas (arm/extra/refresh) eram LIXO DE STACK distinto —
        /// não handle nem "token de arme": quem arma a game-list é o próprio count=0. 3 reais + zero-pad.</summary>
        public static byte[] GameListEmpty()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x36);
            w.WriteByte(0);                 // count = 0 (lista vazia no solo)
            w.WriteBytes(new byte[9]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>Game list 0x36 SINTETIZADA do worldserv (FUN_00422c90 monta [36 00][count][entradas];
        /// cada entrada = [u16 fieldIndex][corpo do FUN_00405790]). Corpo: [0]flag/senha [1]playing(state==2)
        /// [2]map(+0x118) [3]mode(+0x119) [4]minLvl(+0x111) [5]maxLvl(+0x112) [6](+0x113) [7](+0x2bc)
        /// [8]maxRounds(+0x11a) [9]curPlayers(+0x116+0x117) [10]maxPlayers(+0x114+0x115) [11..16]host IP:port
        /// [17..22]zeros [23..]nome\0 [fim]u16(+0x3a8=mapSlot). Validado in-game 2026-07-01 (renderizou
        /// Number/[Mapa] Nome/Lv min~max/n/N byte-a-byte). Lista vazia = count=0 + pad (arma o Create).</summary>
        public static byte[] GameList(IReadOnlyList<RoomListEntry> rooms)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x36);
            w.WriteByte((byte)Math.Min(rooms.Count, 255));        // count @off2
            foreach (var r in rooms)
            {
                w.WriteWord(r.FieldIndex);                        // [u16 fieldIndex] -> "Number" + echo no join 0x38
                w.WriteByte((byte)(r.HasPassword ? 1 : 0));       // [0]  flag senha (cadeado)
                w.WriteByte((byte)(r.Playing ? 1 : 0));           // [1]  playing -> "Status"
                w.WriteByte(r.Map);                               // [2]  map -> nome do mapa (ex.: [Gravity])
                w.WriteByte(r.Mode);                              // [3]  mode
                w.WriteByte(r.MinLevel);                          // [4]  minLevel -> "Level" (min)
                w.WriteByte(r.MaxLevel);                          // [5]  maxLevel -> "Level" (max)
                w.WriteByte(0);                                   // [6]  (+0x113)
                w.WriteByte(0);                                   // [7]  (+0x2bc)
                w.WriteByte(r.MaxRounds);                         // [8]  maxRounds
                w.WriteByte(r.CurPlayers);                        // [9]  curPlayers -> "n/N" (esq)
                w.WriteByte(r.MaxPlayers);                        // [10] maxPlayers -> "n/N" (dir)
                w.WriteUInt32(r.HostIp);                          // [11..14] host IP -> "Network"
                w.WriteWord(r.HostPort);                          // [15..16] host port
                w.WriteUInt32(0);                                 // [17..20] zeros
                w.WriteWord(0);                                   // [21..22] zeros
                foreach (char c in r.Name ?? "") w.WriteByte((byte)c); // [23..] nome -> "Game title"
                w.WriteByte(0);                                   // NUL
                w.WriteWord(r.MapSlot);                           // [fim] u16 (+0x3a8) = mapSlot
            }
            if (rooms.Count == 0) w.WriteBytes(new byte[9]);      // bloco vazio (= GameListEmpty; arma o Create)
            return w.ToArray();
        }

        /// <summary>0x1f info de sessão/char. RE FUN_00404fc0: a ENTRADA (sucesso) tem LEN=15 =
        /// [1f 00][00][00][userid:u16][registro do player]. O registro (FUN_0040afb0) = [nome\0][class][team]
        /// [dword]. O byte 15+ do bloco era LIXO DE STACK — VARIOU entre sessões (06/64/08 no diff golden×log),
        /// logo zero-pad (LEN real=15). Userid e nome vêm do domínio. A volta-à-lista pós-clear re-manda ESTE
        /// MESMO frame (mitm_move_133859 l.460 == entrada l.19; só o byte de lixo difere — sem handle/marker).</summary>
        public static byte[] SessionInfo(ushort userId, string name)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1f);
            w.WriteWord(0);
            w.WriteWord(userId);
            WriteName(w, name);
            w.WriteBytes(PlayerRecordTail);
            w.WriteBytes(new byte[9]);              // bloco de 24B: byte 15+ era lixo de stack (LEN real=15)
            return w.ToArray();
        }

        /// <summary>Uma entrada da user list do canal (0x1e) — DTO de borda: userid + nome do char + classe.</summary>
        public readonly record struct UserListEntry(ushort UserId, string Name, byte CharClass);

        /// <summary>0x1e lista de USUÁRIOS ONLINE do canal ("dchannel01"). RE FUN_00404da0 + FUN_0040afb0:
        /// [1e 00][type=0][count][nome-canal\0] + count×[relIdx u16][uid u16][nome\0][classe][time][dword u32].
        /// Cravado da captura (orig_capture2, S>C 0x1e ao logar): 1 user "JP2" uid6 = `...00 00 06 00 4a503200 01
        /// 00 00000000` (a cauda após o LEN real é lixo de stack, ignorada). O nome vai COMPLETO (nul-terminado) —
        /// o `WriteName` de 2 bytes cortava o nome ("Heroi2"→"He" na tela). userid/nome/classe vêm do domínio.</summary>
        public static byte[] ChannelList(IReadOnlyList<UserListEntry> users)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x1e);
            w.WriteByte(0);                                  // type
            w.WriteByte((byte)Math.Min(users.Count, 255));   // count
            w.WriteCString(ChannelName);
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                w.WriteWord((ushort)i);          // relIdx (posição no canal)
                w.WriteWord(u.UserId);           // uid u16
                w.WriteCString(u.Name);          // nome COMPLETO + NUL (FUN_0040afb0 user+0x14a8)
                w.WriteByte(u.CharClass);        // classe (user+0x1531)
                w.WriteByte(0);                  // time (user+0x146c) — 0 no canal (sem times fora do field)
                w.WriteInt32(0);                 // dword (user+0x14d0)
            }
            if (users.Count == 0) w.WriteBytes(new byte[8]);   // canal vazio: pad mínimo
            return w.ToArray();
        }

        /// <summary>0x3b ack de criação de sala. RE FUN_00423580 (@0x423580, linha 264): LEN=5 =
        /// [3b 00][status=0][seat:u16] (seat = slot do field-objeto, 0 no solo). O [538b003600007f] do blob
        /// antigo era LIXO DE STACK. 5 reais + zero-pad.</summary>
        public static byte[] RoomCreateAck()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3b);
            w.WriteByte(0);                 // status (0 = sucesso)
            w.WriteWord(0);                 // seat:u16 (slot do field-objeto; 0 no solo)
            w.WriteBytes(new byte[7]);      // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x43 ack de start do match. status 0 = partida inicia (seta o timer this+0x2b8). O original
        /// manda um body estruturado [00 01 42 00 0a sess 00 01] (diff verificado 2026-07-03), mas a sonda runtime
        /// PROVOU que replicá-lo NÃO muda o observador: os 2 humanos entram no stage com ctLocalPlayers=1 (=JOGADOR,
        /// não observador) de qualquer forma — o gap real é a sync do game-stream SE1 (7B UDP de :2302/:2303
        /// descartados + 0x4C sem resposta), não este frame. Semântica per-byte por RE (FUN_004079d0) fica pendente;
        /// zerado até então (não shippar constante capturada com o byte per-sessão).</summary>
        public static byte[] MatchStartAck()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x43);
            w.WriteByte(0);                 // status (0 = partida inicia)
            w.WriteBytes(new byte[9]);      // body: semântica pendente de RE; zerado (não é lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x48 tempo restante do stage. RE FUN_00408440 (@0x408440, linha 1696): LEN=9 =
        /// [48 00][01][RemainingSec=dur+3 u16][this+0x2c0=0][this+0x2c1=0][this+0x122][this+0x123]. Os 2
        /// últimos são índices de best-player (14 14 na captura). O [a0 0f] final era LIXO DE STACK.
        /// RemainingSec vem do domínio (duração da sala). Referência: 432s -> 435 (0x01b3). 9 reais + zero-pad.</summary>
        public static byte[] RemainingTime(int durationSec)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x48);
            w.WriteByte(1);
            w.WriteWord(durationSec + 3);
            w.WriteWord(0);                         // this+0x2c0 / this+0x2c1
            w.WriteByte(0x14); w.WriteByte(0x14);   // this+0x122 / this+0x123 = índices best-player (LEN=9 acaba aqui)
            w.WriteBytes(new byte[3]);              // padding do bloco de 12B (era lixo de stack: 00 a0 0f)
            return w.ToArray();
        }

        /// <summary>0x4a resultado de FIM de stage (tela de resumo). RE de DOIS builders que montam a MESMA
        /// forma de 6 bytes [4a 00][this+0x2bd][this+0x2bf][this+0x2c0][this+0x2c1], diferindo só no this+0x2bd:
        ///  - CLEAR (FUN_00405a90 @0x405a90): 2bd=2 (eco do request StageClear) -> tela de Rank.
        ///  - MORTE (GameDiePlayer FUN_004087d0 @0x4087d0, modo survival/case 2): 2bd=1 + timer 15s -> resumo de fim.
        /// <paramref name="resultType"/> = this+0x2bd (2=clear, 1=morreu). Os 6 bytes seguintes do bloco de 12B
        /// eram LIXO DE STACK na captura -> zero-pad determinístico.</summary>
        public static byte[] StageEndResult(byte resultType)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x4a);
            w.WriteByte(resultType);                              // this+0x2bd (2=stage clear, 1=morte)
            w.WriteByte(0x01); w.WriteByte(0x01); w.WriteByte(0); // estado do field (this+0x2bf/0x2c0/0x2c1)
            w.WriteBytes(new byte[6]);                            // padding do bloco de 12B (era lixo de stack)
            return w.ToArray();
        }

        /// <summary>0x44 fim de partida (volta ao game room): [44 00][reason][00][01 00 00 00][nome da sala].
        /// O nome vem do domínio (último campo, tamanho variável seguro).</summary>
        public static byte[] MatchEnd(byte reason, string roomName)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x44);
            w.WriteByte(reason);
            w.WriteByte(0);
            w.WriteUInt32(1);
            w.WriteBytes(Encoding.ASCII.GetBytes(roomName ?? ""));
            return w.ToArray();
        }

        /// <summary>0x0e OnRecvSuccessUDP: ecoa o ENDPOINT DO CLIENTE (ip+porta big-endian) nos dois slots
        /// + trailer zeros. Modo local-scored (suprime o combo se mandarmos as portas do server).</summary>
        public static byte[] Endpoints(byte[] ip, ushort port)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x0e);
            w.WriteByte(0);
            for (int slot = 0; slot < 2; slot++)
            {
                w.WriteBytes(ip);
                w.WriteByte((byte)(port >> 8));     // big-endian
                w.WriteByte((byte)(port & 0xff));
            }
            w.WriteBytes(new byte[9]);              // trailer zeros
            return w.ToArray();
        }

        private static void WriteName(PacketWriter w, string name)
        {
            byte[] nb = Encoding.ASCII.GetBytes(name ?? "");
            w.WriteByte(nb.Length > 0 ? nb[0] : 0);
            w.WriteByte(nb.Length > 1 ? nb[1] : 0);
        }
    }
}
