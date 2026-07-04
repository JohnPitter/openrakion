using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.CharSelect;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        // --- ENTRADA NO LOBBY/CAMPO (síntese do protocolo de canal/sala/stage) ------------------
        // Após o char-select, o cliente faz o handshake UDP (echo 0x0201) e manda esta cadeia TCP; o world
        // responde levando-o à LISTA DE CANAIS (0x1e "dchannel01") e à sala/stage. Cada frame é MONTADO do
        // estado por <see cref="LobbyFrames"/> (síntese pura, sem blob de captura): o userid/nome vêm do
        // domínio e casam com o 0x0C (senão o cliente trava re-mandando 0x14), o nome da sala vem do 0x3b,
        // o tempo do stage da duração da sala. Os tokens/handles autorais do servidor são constantes
        // nomeadas em LobbyFrames. A captura original vive só no golden test (LobbyFrameGoldenTests).
        private ushort LobbyUid => (ushort)GameInfoId;
        private string LobbyName => LoginCharList?.AccountName ?? CharName ?? "";

        /// <summary>0x0e OnRecvSuccessUDP: resolve o endpoint UDP aprendido do cliente (cai p/ 127.0.0.1:2301
        /// se ainda não conhecido) e delega a montagem do frame a <see cref="LobbyFrames.Endpoints"/>.</summary>
        private byte[] Endpoints0e()
        {
            var ep = UdpEndpoint;
            byte[] ip = ep != null ? ep.Address.MapToIPv4().GetAddressBytes() : new byte[] { 127, 0, 0, 1 };
            ushort port = (ushort)(ep != null ? ep.Port : 2301);
            return LobbyFrames.Endpoints(ip, port);
        }

        private bool TryHandleLobbyEntry(ushort opcode, byte[] data)
        {
            switch (opcode)
            {
                case 0x0e: // pedido de UDP-success -> endpoints + GG
                    SendEncryptedFrame(Endpoints0e());
                    SendEncryptedFrame(LobbyFrames.GameGuard());
                    Log.Ok("lobby", "[{0}] 0x0e (endpoints 40708/40709) + 0x10 enviados", Slot);
                    // DEAD-END FINAL (auto-render da poção no char-select — NÃO re-tentar): o paint do quickslot
                    // (0x31 -> FUN_0047d1d0) é gated por menu-state (só 0x19/1a/1b; char-select=0x16 sai cedo em
                    // rakion.bin@0x47d21d). Patch trocando `cmp 0x1b`->`cmp 0x16` (off 0x7c61c) CRASHA (AV in-game,
                    // testado): após o gate o handler escreve num WIDGET via FUN_0044deb0(*(DAT_004feed0+0x17c)) que
                    // assume o painel do INVENTÁRIO — no char-select é outro painel -> AV. E mesmo "pulando o
                    // widget" não renderiza: o char-select pinta o quickslot do AccountInfo no BUILD do painel
                    // (logo após o 0x0c), quando ainda está VAZIO (o 0x0c não carrega quickslot; o 0x31 chega
                    // depois). Teto = render no LOBBY (0x14 + fallback 0x2c), que persiste pro char-select depois.
                    return true;
                case 0x14: // -> ack + info sessao (0x1f) + LISTA DE CANAIS (0x1e). Tb marca o "channel lobby":
                    // InField/FieldSecondary/Status=2 (FieldLobby) p/ os opcodes de sala/shop (0x2d/2e/2f) passarem
                    // o gate a partir do Game List (Roomstate Fase A — habilita o inventario/shop).
                    InField = true; FieldSecondary = true; SecondActive = true; Status = 2;
                    SendEncryptedFrame(LobbyFrames.SpawnAck());
                    SendEncryptedFrame(LobbyFrames.SessionInfo(LobbyUid, LobbyName));
                    SendEncryptedFrame(LobbyFrames.ChannelList(LobbyUid, LobbyName));
                    Log.Ok("lobby", "[{0}] 0x14 + 0x1f + 0x1e (canais) + channel-lobby (Status=2)", Slot);
                    // POPULA o espelho do box (AccountInfo+0x78) JA no login — o painel do box no lobby (menu 0x14)
                    // pinta do espelho QUANDO E' CONSTRUIDO (igual o equip, carregado no 0x0C). O 0x13 da resposta
                    // ao 0x2d chega DEPOIS do painel construir (corrida) -> box vazio. Mandando o 0x13 aqui (antes
                    // de qualquer abertura do inventario), o espelho ja esta preenchido quando o painel monta.
                    // NADA alem de 14/1f/1e aqui: o original (mitm_full_113423) nao manda 0x13 nem
                    // pacotes de loja neste ponto. O box e' (re)pintado no open do inventario (0x2c/0x2d).
                    // AUTO-RENDER da poção: pinta o quickslot persistido JA aqui, p/ aparecer no relog SEM abrir
                    // o shop. Guard próprio (_potionLoginPainted) NÃO suprime o fallback do 0x2c — se o widget do
                    // potion-bar ainda não existir neste ponto, o 1o open do inventário repinta.
                    if (!_potionLoginPainted) { _potionLoginPainted = true; PaintQuickslot(); }
                    return true;
                case 0x36:
                {
                    // game list SINTETIZADA das salas REAIS registradas (0x3b). Formato do worldserv
                    // (FUN_00422c90/FUN_00405790), validado in-game. count=0 arma o Create.
                    var gl = LobbyFrames.GameList(_server.SnapshotRooms());
                    SendEncryptedFrame(gl);
                    // 2º 0x36 (a captura do original manda dois na fase de arme) SÓ na 1a vez. Remandar a
                    // cada poll re-armava a game-list e mantinha o cliente em polling (telas sobrepostas),
                    // travando o Previous.
                    if (!_r36bSent) { _r36bSent = true; SendEncryptedFrame(gl); }
                    Log.Info("lobby", "[{0}] 0x36 game list{1}", Slot, _r36bSent ? " (+0x36b 1a vez)" : "");
                    return true;
                }
                case 0x3b: // CRIAR SALA: room lobby = Status=2 (FieldLobby) + InField + FieldSecondary. (Era
                    // Status=3; 2 e' "sala montada antes do match" -> habilita shop 0x2d/2e/2f. O stage (0x4b)
                    // promove a Status=3 via CreateField.)
                    FieldSecondary = true; SecondActive = true; Status = 2; InField = true;
                    ParseRoomCreate(data); // guarda map/mode da sala -> decide stage (client-side) vs Battle (networked)
                    _server.RegisterRoom(this); // PUBLICA a sala na game list (0x36) p/ outros clientes verem+entrarem
                    // nova sala = novo match: rearma o StartGameClock (a trava e' por ENTRADA NO STAGE,
                    // nao por sessao — sem isto a 2a sala da mesma sessao ficava com o field morto)
                    System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
                    SendEncryptedFrame(LobbyFrames.RoomCreateAck());
                    Log.Ok("lobby", "[{0}] 0x3b sala criada -> room lobby (Status=2, FSec, map={1} mode={2} slot/u16={3}=0x{3:x4} rounds={4})",
                        Slot, PendingRoomMap, PendingRoomMode, PendingRoomDurationSec, PendingRoomRounds);
                    return true;
                case 0x43: // engage/start do match: rearma o clock p/ REMATCH na mesma sala
                {
                    System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
                    SendEncryptedFrame(LobbyFrames.MatchStartAck());
                    // START MULTIPLAYER: o host iniciou -> avisa os OUTROS membros da sala p/ entrarem no stage
                    // juntos (worldserv FUN_004061f0 broadcast 0x43 [0] a TODOS). Sem isto só o host entrava.
                    var sf = _server.GetField(FieldId);
                    int others = 0;
                    if (sf != null)
                    {
                        byte[] startFrame = LobbyFrames.MatchStartAck();
                        lock (sf.Players) foreach (var m in sf.Players)
                            if (m != this && m.Connected) { m.SendEncryptedFrame(startFrame); others++; }
                    }
                    Log.Ok("lobby", "[{0}] 0x43 start -> match iniciado (broadcast a {1} outro(s) membro(s))", Slot, others);
                    return true;
                }
                case 0x48: // tempo restante do stage = duração da sala (+3); cai p/ 432s se a sala não definiu
                    int durSec = PendingRoomDurationSec > 0 ? PendingRoomDurationSec : 432;
                    SendEncryptedFrame(LobbyFrames.RemainingTime(durSec));
                    Log.Info("lobby", "[{0}] 0x48 resp (RemainingSec={1}s)", Slot, durSec + 3);
                    return true;
                case 0x4A: // 0x4A com data[0]=0x02 = StageClear. Combate usa outras 0x4A -> nao intercepta.
                    if (data.Length >= 1 && data[0] == 0x02)
                    {
                        // Manda SO a tela de Rank agora; o 0x44 (match-end) + refresh do lobby vem com um
                        // pequeno DELAY p/ o overlay de Rank ter tempo de aparecer. (Mandar tudo junto, ou
                        // disparar no 0x53, fazia o Rank sumir.) O 0x53 GameResultReport que o cliente manda
                        // logo apos e' CONSUMIDO (case 0x53) p/ nao cair no handler que desconecta em solo.
                        SendEncryptedFrame(LobbyFrames.StageEndResult(2));   // tela de RANK (2bd=2 = clear)
                        Log.Ok("lobby", "[{0}] 0x4A StageClear -> Rank (0x44+refresh do lobby em ~5s)", Slot);
                        _ = ScheduleLobbyReturnAfterRankAsync();
                        return true;
                    }
                    return false;
                case 0x3A: // FieldLeaveGame: sair do game room -> volta a LISTA DE GAMES do canal. O handler
                    // real (Op_FieldLeaveGame) desconecta (DISC 0x50: guard InField&&FieldSecondary que o
                    // estado do solo nao satisfaz apos o stage). Tratamos aqui: reset + refresh da lista
                    // (1f/1e/36, os MESMOS frames capturados do original APOS o 0x44 = o "voltar pra lista").
                    // MEMBER-LEAVE da SALA pré-partida (multiplayer): libera o próprio seat e avisa os demais com
                    // 0x3a member-leave — sem isto o master mantém o card de quem saiu (fantasma pós-saída). Guard
                    // Settled=true (só a sala pré-partida; ResetMatch zera ao começar o match -> NÃO toca o fluxo
                    // pós-partida validado) + Count>1 (há quem notificar). Remoção inline (não LeaveField) p/ não
                    // acionar a lógica de W.O. do abandono de stage.
                    {
                        var lf = _server.GetField(FieldId);
                        var myRec = lf?.FindRec(this);
                        if (lf != null && myRec != null && lf.Settled && lf.Count > 1)
                        {
                            int leftSeat = myRec.Slot;
                            bool wasMaster = lf.MasterSlot == leftSeat;
                            lf.Remove(this);                                   // libera o rec (State=0, Session=null)
                            if (wasMaster)                                     // host saiu -> reatribui ao 1º ocupado
                            {
                                lf.MasterSlot = -1;
                                foreach (var r in lf.Slots) if (r.Occupied) { lf.MasterSlot = r.Slot; break; }
                            }
                            byte[] memberLeave = { 0x3a, 0x00, (byte)leftSeat };
                            lock (lf.Players) foreach (var m in lf.Players) if (m.Connected) m.SendLobby(memberLeave);
                            FieldId = -1;
                            Log.Ok("lobby", "[{0}] saiu da sala {1} seat {2} -> 0x3a member-leave a {3} restante(s){4}",
                                Slot, lf.Id, leftSeat, lf.Count, wasMaster ? " (novo master seat " + lf.MasterSlot + ")" : "");
                        }
                    }
                    InField = true; FieldSecondary = true; SecondActive = true; Status = 2; // volta ao channel lobby (shop segue ok)
                    // RE confirmada (mitm_move_133859 l.460/461 == entrada l.19/20): a volta-à-lista re-manda
                    // os MESMOS 0x1f/0x1e/0x36 da entrada, sintetizados do estado — sem frame "clear" distinto.
                    SendEncryptedFrame(LobbyFrames.SessionInfo(LobbyUid, LobbyName));
                    SendEncryptedFrame(LobbyFrames.ChannelList(LobbyUid, LobbyName));
                    SendEncryptedFrame(LobbyFrames.GameList(_server.SnapshotRooms()));
                    Log.Ok("lobby", "[{0}] 0x3A FieldLeaveGame -> lista de games (channel lobby Status=2)", Slot);
                    return true;
                case 0x3D: // READY toggle na tela da sala (room lobby, Status=2). data[0]=1 fica ready, 0 desfaz.
                    // Mesmo opcode do weapon-swap in-stage (Status=3 -> Op_0x3D_Recon); no room o worldserv exige
                    // Status=3 (FUN_00423ad0), mas nossa sala é Status=2 -> tratamos aqui. WeaponState = ready-state
                    // (1=não-ready, 2=ready = player+0x1ac do cliente). Broadcast [3d 00][seat][arg] a todos (LOBBY).
                    if (Status == 2 && FieldId >= 0)
                    {
                        var df = _server.GetField(FieldId);
                        var dr = df?.RecAt(FieldSeat);
                        if (df != null && dr != null)
                        {
                            byte arg = (data != null && data.Length > 0) ? data[0] : (byte)1;
                            dr.WeaponState = arg != 0 ? (byte)2 : (byte)1;   // ready = 2, não-ready = 1
                            using var w = new PacketWriter();
                            w.WriteWord(0x3d).WriteByte(FieldSeat).WriteByte(arg);
                            byte[] frame = w.ToArray();
                            lock (df.Players) foreach (var m in df.Players) m.SendLobby(frame);
                            int ready = 0, occ = 0;
                            foreach (var r in df.Slots) if (r.State != 0 && (r.Session != null || r.Bot != null))
                                { occ++; if (r.WeaponState == 2 || r.IsBot) ready++; }
                            Log.Ok("room", "[{0}] ready seat {1} = {2} ({3}/{4} prontos)", Slot, FieldSeat, arg, ready, occ);
                        }
                        return true;
                    }
                    return false;
                case 0x3E: // TROCAR DE TIME na tela da sala (room lobby, Status=2): move o slot p/ o outro bloco
                    // (0..9 vermelho <-> 10..0x13 azul) e faz broadcast [3e 00][old][new] a todos (LOBBY, como o
                    // member-join 0x38). O 0x3e IN-STAGE (Status=3, re-spawn) NÃO cai aqui (só trata Status==2).
                    if (Status == 2 && FieldId >= 0)
                    {
                        var tf = _server.GetField(FieldId);
                        if (tf != null)
                        {
                            byte old = FieldSeat;
                            int ns = tf.SwitchTeamBlock(old);
                            if (ns >= 0)
                            {
                                // 0x3a esvazia o slot antigo + 0x38 member-join no novo (frames PROVADOS que o
                                // cliente aplica; o [3e][old][new] era ignorado). Broadcast a TODOS os membros.
                                using var lv = new PacketWriter(); lv.WriteWord(0x3a).WriteByte(old);
                                byte[] leave = lv.ToArray();
                                byte[] join = WorldServer.BuildMemberJoin(CharName, CharClass, CharLevel, (ushort)GameInfoId, ns, peerEp: UdpEndpoint);
                                lock (tf.Players) foreach (var m in tf.Players) { m.SendLobby(leave); m.SendLobby(join); }
                                Log.Ok("room", "[{0}] troca de time: seat {1} -> {2} (0x3a+0x38)", Slot, old, ns);
                            }
                            else Log.Info("room", "[{0}] troca de time negada (outro bloco cheio, seat {1})", Slot, old);
                        }
                        return true;
                    }
                    return false;
                // 0x2E (Shop Buy) NAO e' mais interceptado aqui -> cai no WorldHandlers.Dispatch (Op_RoomMemberQuery
                // = compra real). Removido o intercept de falha graciosa. (Ver default: 0x2e na lista de excecoes.)
                case 0x2C: // Inventory enter. O cliente manda no body o SEU [SlotActive:u32][user14a4:u32]
                    // (contexto/ponteiro da UI). O 0x12 (enter-ack, FUN_00420de0) deve ECOAR esses valores —
                    // com (1,1) o cliente nao reconhece e trata como "char created". Guardo o body p/ ecoar
                    // tambem no user14a4 do 0x13. (No MEU world a previa do char-select e' vazia => sem "varios armours".)
                    _invReqBody = data;
                    SendInventoryEnterAck(data);
                    // Pinta o box na abertura: no cliente no-GG o grid só pinta via 0x31 (FUN_0047d1d0),
                    // nunca via 0x13. Mandados UMA vez aqui (não a cada poll de 0x2d, que era o que prendia
                    // o cliente e quebrava o Previous) — a propria compra (0x2e) ja re-pinta com 0x31 sem
                    // quebrar o Previous, entao 0x31 na abertura tb e' seguro.
                    // item 0 = celula esvaziada por um move: NAO pintar (frame nunca validado; o cliente
                    // ja processou o move local e tem o buraco)
                    for (int i = 0; i < BoxItems.Count && i < 0x78; i++)
                        if (BoxItems[i] != 0) SendBoxAdd(BoxItems[i], (byte)i, (byte)(1 + BoxLevel[i]), BoxCount[i]); // 1 + nível de refino (+N)
                    // quickslot: FALLBACK do auto-render. Se a pintura na entrada do lobby (0x14) ainda não
                    // pegou (widget do potion-bar não construído na hora), pinta aqui no 1o open. Guard
                    // próprio (_potionPainted) -> não repinta nas reentradas.
                    if (!_potionPainted) { _potionPainted = true; PaintQuickslot(); }
                    return true;
                case 0x2D: // FIEL à captura: o 0x2d responde SEMPRE o ack curto (handle), NUNCA 0x13.
                    // O original tem uma máquina de estado do inventário em user+0x144c (FUN_0040b000 seta no
                    // 0x2c, FUN_0040c960 lê no 0x2d): 0=fechado, 1=aberto, 2=loja — manda a LISTA 0x13 só na 1a
                    // chamada (estado 1->0), depois o ack. Aqui DIVERGIMOS por simplicidade: o grid é pintado 1x
                    // na ENTRADA (0x2c, via 0x31) e o 0x2d é sempre-ack. Mandar 0x13 no 0x2d prendia o cliente
                    // re-pedindo 0x2d (telas sobrepostas) e o Previous nao voltava p/ a lista de salas.
                    SendInventoryAck(1);
                    return true;
                case 0x53: // GameResultReport pos-clear (SOLO PvE). O handler real (Op_0x53_Recon) exige o
                    // field-record do match-engine, que esta OFF no solo (combate client-side) e DESCONECTAVA.
                    // Consumimos aqui (regra de negocio = WorldServer.ApplyStageResult); antes o stage clear
                    // nao dava progresso nenhum.
                    OnStageResult(data);
                    return true;
                case 0x4b: // SPAWN no stage (72B). Inicia o relogio da partida: um timer incrementa
                    // GameSeq e manda o tick 1583 (o cliente ecoa o seq; seq avancando = timer corre).
                    InField = true;
                    Status = 3;            // estado de CAMPO (Op_FieldUseItem exige Status==3; o 0x44/0x3A revertem p/ 2)
                    StartGameClock();
                    PaintQuickslot();      // re-registra as poções no campo — hipótese: o cliente zera o contador ao entrar no stage
                    // SPAWN no stage (0x4b): guarda o blob REAL que este humano subiu (posição/stats) e faz o spawn
                    // MÚTUO humano↔humano relayando o blob real de cada peer (cada um vê o avatar do outro no lugar certo).
                    StageSpawnUpload = data;
                    { var stf = _server.GetField(FieldId); if (stf != null) _server.SpawnStageForHumans(stf, this); }
                    Log.Ok("lobby", "[{0}] 0x4b (spawn) -> STAGE (Status=3, poções re-enviadas); relogio iniciado (udp={1})", Slot, UdpEndpoint?.ToString() ?? "-");
                    return true;
                case 0x0f: return true; // keepalive do cliente: sem resposta TCP
                // (0x4b acima inicia o relogio)
                case 0x31: // potion slot: mover/trocar item entre o BOX e o quickslot de pocao
                    HandlePotionSlot(data);
                    return true;
                case 0x73: // mudar slot de item no BOX (empilhar pocoes iguais). Sem isso o cliente
                    // trava em "Changing item slot" (caia no catch-all 'em campo (sem resp)' abaixo).
                    HandleItemSlotChange(data);
                    return true;
                case 0x34: // Buy Power User: concede o PU + destrava o popup (testando status=0 p/ sem erro)
                    HandleBuyPowerUser();
                    return true;
                case 0x46: // o cliente manda 0x46 ao SAIR/dar giveup no stage. Sem o eco FIELD 0x46 [seat]
                    // (+ 0x44 fim) ele trava "waiting for world" — MESMO motivo documentado no Op_0x46_Recon
                    // (PvP). Em SOLO o opcode caía no catch-all e era ENGOLIDO -> o "sair do stage" não voltava.
                    {
                        var fld = _server.GetField(FieldId);
                        if (fld == null || fld.Mode != 0) return false;   // PvP/sem field -> dispatch real (Op_0x46_Recon)
                        fld.BroadcastField(0x46, new[] { FieldSeat });    // eco que destrava o "waiting for world"
                        fld.OnPlayerDeath(FieldSeat, killerSeat: -1, cause: 0);
                        if (fld.CountAlive(0) + fld.CountAlive(1) == 0)
                        {
                            fld.EndMatch(0);
                            fld.BroadcastLobby(fld.BuildMatchEnd(2));      // 0x44 -> volta ao game room (= fim do clear)
                        }
                        Log.Ok("lobby", "[{0}] 0x46 saída do stage solo -> eco FIELD 0x46 + 0x44 (destrava waiting-world)", Slot);
                        return true;
                    }
                case 0x4F: // player MORREU no stage (DIE: data=[cause][killer]). No SOLO o combate é client-side e
                    // o 0x4f era ENGOLIDO -> o cliente ficava "esperando outros players". RE GameDiePlayer
                    // (FUN_004087d0 @0x4087d0, modo survival/case 2): na morte que encerra a partida o original
                    // manda o eco 0x4f + o 0x4a de RESUMO com this+0x2bd=1 (morreu) + timer 15s -> fim. Replicamos:
                    // eco + 0x4a(tipo=1) + volta ao room com delay (mesma malha do clear, que manda 0x4a tipo=2).
                    {
                        var fld = _server.GetField(FieldId);
                        if (fld == null || fld.Mode != 0) return false;   // PvP/sem field -> dispatch real (Op_0x4F_Recon)
                        byte cause = data.Length > 0 ? data[0] : (byte)0;
                        byte killer = data.Length > 1 ? data[1] : (byte)0;
                        // eco da morte (mesmo layout do Op_0x4F_Recon: [seat][cause][killer][score0][score1])
                        fld.BroadcastField(0x4f, new byte[] { FieldSeat, cause, killer, fld.Score0, fld.Score1 });
                        fld.OnPlayerDeath(FieldSeat, killerSeat: -1, cause: cause);
                        SendEncryptedFrame(LobbyFrames.StageEndResult(1)); // RESUMO de fim por MORTE (0x4a, 2bd=1)
                        _ = ScheduleLobbyReturnAfterRankAsync();           // delay -> 0x44 (volta ao game room)
                        Log.Ok("lobby", "[{0}] 0x4f morte no stage solo -> eco FIELD 0x4f + resumo 0x4a(2bd=1) + 0x44 (delay)", Slot);
                        return true;
                    }
                case 0x15: // CharacterChangeBuddyName (messenger "nick change"): o cliente manda só [novoNick\0] e
                    // trava em "Waiting for change request of character's name for messenger from server" (lang 604)
                    // até a resposta 0x15/0x0B. Sincroniza usergameinfo.buddyname com o nick escolhido.
                    _ = HandleChangeBuddyNameAsync(data);
                    return true;
                case 0x19: // CharacterGetUserName (messenger "add buddy"): o cliente pede ao WORLD o account-id
                    // do dono do nick digitado e trava em "Waiting for ID Information on account from server"
                    // (lang 599) ate' a resposta 0x0D. Sem isso o "add" do messenger nao avanca.
                    _ = HandleGetUserNameAsync(data);
                    return true;
                case 0x47: // chat DA SALA (3D chat). Intercepta comando de bot (/addbot, /removebot). Sem
                    // isto o comando caía no catch-all "em campo (sem resp)" e era engolido — o gatilho
                    // antigo estava no 0x56, que a tabela mapeia p/ Stub (nunca roda). data = "<nome> : <texto>".
                    // Chat normal segue engolido no solo (o cliente desenha o próprio chat local).
                    WorldHandlers.TryHandleBotChatCommand(this, _server, data);
                    return true;
                default:
                    // Shop list/loadout (0x2d/0x2f) E buy (0x2e) DEVEM chegar aos handlers reais
                    // (Op_RoomRosterSync/Op_GroupMemberInfo/Op_RoomMemberQuery) -> NAO consumir aqui, deixa o
                    // Dispatch rodar (gate passa: channel/room lobby = InField+FSec+Status=2). Idem 0x39
                    // (FieldListReq = Quick Start / lista de salas, FUN_00423300) e 0x74 (RoomMoveAction =
                    // UPGRADE do refino, FUN_00421e10 -> reply SendMessage 0x28): sem o reply o cliente trava
                    // ("esperando a resposta do world" / "Upgrading Now").
                    // 0x38 = JOIN de sala pela game list (Op_RoomJoin). O joiner tem FieldId=-1 (só navegando),
                    // então o catch-all "em campo" abaixo o engolia -> "entering field" travava. Rotear ao dispatch.
                    if (opcode == 0x2d || opcode == 0x2e || opcode == 0x2f || opcode == 0x33 || opcode == 0x38 || opcode == 0x39 || opcode == 0x74) return false;
                    // Salas BATTLE/PvP (Mode != 0): os opcodes de COMBATE vao aos handlers reais do
                    // motor de partida — 0x4d (par golem/facing: y==0 = golem inimigo destruido ->
                    // fim de round), 0x3d (troca de arma), 0x50 (reporte de exp/gold do fim de partida ->
                    // level-up). No SOLO (Mode 0) continuam engolidos: o combate e' client-side e o 0x4d
                    // (y==0) encerraria o stage. (0x46 saída e 0x4f morte têm case próprio acima — solo tb.)
                    if (opcode == 0x4d || opcode == 0x3d || opcode == 0x50)
                    {
                        var bf = _server.GetField(FieldId);
                        if (bf != null && bf.Mode != 0) return false; // dispatch -> Op_0xNN_Recon
                    }
                    // No lobby/stage o cliente manda varios opcodes que o world original so consome
                    // sem responder. Evita DISC enquanto a fase de gameplay nao esta toda portada.
                    if (InField) { Log.Debug("lobby", "[{0}] opcode {1:X2} em campo (sem resp)", Slot, opcode); return true; }
                    return false;
            }
        }

        /// <summary>0x19 CharacterGetUserName: traduz o pedido (nick + opcode/seq da resposta) e responde o 0x0D
        /// com o account-id do dono do nick (busca no DB). data = [nick][u32 respOpcode][u32 respSeq]; o cliente
        /// dita o opcode/seq da resposta e nao valida o account-id, so' o status byte. RE: worldserv 0x413980.</summary>
        private async Task HandleGetUserNameAsync(byte[] data)
        {
            // data = nick null-terminated; apos o NUL pode vir lixo de stack do buffer do cliente (varia
            // entre sessoes) -> ignorado. Le ate' o primeiro NUL.
            int nul = Array.IndexOf(data, (byte)0);
            int nickLen = nul >= 0 ? nul : data.Length;
            if (nickLen == 0) { Log.Warn("lobby", "[{0}] 0x19 GetUserName: nick vazio", Slot); return; }
            string nick = Encoding.ASCII.GetString(data, 0, nickLen);

            // Domínio: resolve o dono do nick E persiste a amizade RECÍPROCA (o AddBuddy do cliente é mudo, então
            // a amizade nasce aqui). O cliente não valida o accountId, só o status byte (0=ok, 2=não existe).
            var (status, account) = await _server.Buddy.ResolveAndAddBuddyAsync(this, nick);
            SendEncryptedFrame(LobbyFrames.GetUserNameResult(status, account, nick));
            Log.Ok("lobby", "[{0}] 0x19 GetUserName('{1}') -> status={2} account='{3}'", Slot, nick, status, account);
        }

        /// <summary>0x15 CharacterChangeBuddyName (nick change do messenger): o cliente manda só [novoNick\0]; o
        /// world persiste em usergameinfo.buddyname (pela conta da sessão, gameinfo.id) e confirma com o frame
        /// 0x15/0x0B. Sem a confirmação o cliente trava em "Waiting for change request of character's name for
        /// messenger from server" (lang 604). RE: worldserv FUN_004137a0. Lê só até o 1º NUL (resto = lixo de stack).</summary>
        private async Task HandleChangeBuddyNameAsync(byte[] data)
        {
            int nul = Array.IndexOf(data, (byte)0);
            int len = nul >= 0 ? nul : data.Length;
            string nick = len > 0 ? Encoding.ASCII.GetString(data, 0, len) : "";
            byte status = 0;
            if (GameInfoId > 0 && nick.Length > 0) await _server.Db.UpdateBuddyNameAsync(GameInfoId, nick);
            else status = 1;   // sem conta resolvida ou nick vazio -> erro (o cliente mostra a falha)
            SendEncryptedFrame(LobbyFrames.ChangeBuddyNameResult(status, nick));
            Log.Ok("lobby", "[{0}] 0x15 ChangeBuddyName('{1}') -> status={2} (gi={3})", Slot, nick, status, GameInfoId);
        }

        /// <summary>LoginComplete (msgType 2) — sucesso de FUN_0041f6c0.</summary>
        public void SendLoginComplete(string field2, string field3, ushort tail)
        {
            using var p = new PacketWriter();
            p.WriteCString(field2);
            p.WriteCString(field3);
            p.WriteWord(tail);
            p.WriteInt32(0);
            p.WriteByte(1);
            SendMessage(Protocol.SubType.LoginComplete, p.ToArray());
            Log.Ok("login", "[{0}] LoginComplete enviado (field2='{1}')", Slot, field2);
        }

        // ---- Resposta de login: 0x0C (lista de chars do char-select) + 0x0D (tabela zerada) ----
        /// <summary>
        /// Envia a resposta de login SINTETIZADA de raiz: 0x0C (char-select, serializado de
        /// <see cref="LoginCharList"/> pelo <see cref="LoginCharListWriter"/>) + 0x0D (tabela zerada, 1332B).
        /// Sem replay de captura. O 0x10 (GameGuard) vai SÓ após o handshake UDP (ver NotifyUdpReady). O handle
        /// de sessão (@13 do 0x0C) é o _invHandle, ecoado nos acks 0x2c/0x2d.
        /// </summary>
        public void SendLoginResponse()
        {
            // Handle de sessão do PEER (@9-10 do 0x0C) ÚNICO por conexão: o cliente o ecoa no connect P2P e na
            // abertura do canal reliable. Fixo, os 2 humanos colidiam e o 2º virava observador — deriva do Slot
            // (único por sessão conectada) mantendo o low-byte 0x2E conhecido-bom.
            var list = (LoginCharList ?? new CharList()) with
            {
                SessionHandle = _invHandle,
                SessionPeerHandle = (ushort)(0x042E + (Slot << 8)),
            };
            byte[] f0c = LoginCharListWriter.Build(list);
            SendEncryptedFrame(f0c);
            byte[] f0d = new byte[1332]; f0d[0] = 0x0d; f0d[3] = 0x01;   // 0x0D = tabela zerada (sintetizada)
            SendEncryptedFrame(f0d);
            Log.Ok("login", "[{0}] login sintetizado: 0x0C {1}B ({2} chars) + 0x0D {3}B (0x10 após o UDP)",
                Slot, f0c.Length, list.Chars.Count, f0d.Length);
        }

        /// <summary>
        /// Spawn no stage (0x4b da cadeia de entrada): garante que a sessao tem um Field real com
        /// seat alocado, marca o player como ready e delega ao MOTOR DA PARTIDA por-field
        /// (WorldServer.FieldEngine / FUN_00409940). O 1583 idle e o broadcast 0x48 sao do motor
        /// global, NAO mais por-sessao (substitui o StartGameClock parcial).
        /// </summary>
        private void StartGameClock()
        {
            if (System.Threading.Interlocked.Exchange(ref _gameClockStarted, 1) != 0) return;
            GameSeq = 5;
            var f = _server.EnsureFieldForSession(this);
            if (f.Settled) f.ResetMatch(); // field reaproveitado de um match concluido: zera Round/Wins/golens
            if (PendingRoomMode != 0) { f.Mode = PendingRoomMode; f.MapId = PendingRoomMap; }
            if (PendingRoomDurationSec != 0) f.RoundDurationSec = PendingRoomDurationSec; // tempo configurado na sala
            if (PendingRoomRounds != 0) f.MaxRounds = PendingRoomRounds;                  // rounds configurados na sala
            _server.NotifyPlayerReady(f, this);
            // PvP COM BOTS: inicia o 1º round (Phase=Pre→Playing) p/ o motor rodar o BotTick (spawn 0x45 + IA).
            // O MatchTick só roda BotTick na fase Playing; StartGameClock (guardado por _gameClockStarted, 1x)
            // deixava Phase=Pre → o bot nunca spawnava. Com bots o host é o único humano, então o round começa
            // no load dele (2 humanos usariam o all-loaded 0x54 do original).
            if (f.Mode != 0 && f.BotCount > 0 && f.Phase != Domain.MatchPhase.Playing) f.StartRound();
            _server.Bots.SpawnFieldBotsInStage(f); // 0x45 dos bots + 0x54 begin AGORA (no load do host): timing + o begin que faltava
            Log.Ok("field", "[{0}] sala aplicada ao field {1}: mode={2} map={3} dur={4}s rounds={5}",
                Slot, f.Id, f.Mode, f.MapId, f.RoundDurationSec, f.MaxRounds);
            // Sala Battle/PvP (mode != 0) = fluxo NETWORKED: o SERVER inicia o loop UDP com o 1o
            // tick 1583 (mitm_full_113423: o original manda 1583 ANTES do 1o 0040 do cliente; sem
            // ele o input fica congelado). Stage solo (mode 0) fica client-side: sem tick.
            // 2+ HUMANOS = P2P: os CLIENTES dirigem o clock 1583 direto (captura: 20 pkts 2301<->2302,
            // ZERO do servidor). Se o servidor injeta o priming tick aqui, o HOST vê um clock externo e
            // NÃO assume a autoridade (ga_IsServer) -> ninguém dirige o lockstep -> "modo observação".
            // Só solo/bot (HumanCount<2) precisa do tick do servidor. Ver docs/pvp-stage-re.md §10.
            if (f.Mode != 0 && f.HumanCount < 2 && UdpEndpoint != null) _server.SendGameplayTick(UdpEndpoint, LastInput);
            Log.Ok("field", "[{0}] spawn -> motor da partida (field {1}, seat {2}, mode {3})", Slot, f.Id, FieldSeat, f.Mode);
        }

        /// <summary>
        /// Parse do 0x3b (FUN_00423580: name\0 senha\0 desc\0 [map][mode][rounds][u16 durSec]...):
        /// guarda map/mode/rounds/duracao da sala p/ aplicar ao Field no spawn (0x4b). Mapas Battle
        /// (200-213) vem com mode 1-4; rounds validado &lt; 0x16 no original (sala de stage = 1);
        /// durSec validado em 0x1e..0xE10 (30..3600s; alargado do RE 290..1210 que travava stages curtos = 288s).
        /// </summary>
        private void ParseRoomCreate(byte[] data)
        {
            try
            {
                var p = new PacketReader(data);
                PendingRoomName = p.CString(0x29); PendingRoomPass = p.CString(9); p.CString(0xc9); // nome (->0x44), senha, desc
                if (p.Remaining >= 2) { PendingRoomMap = p.Byte(); PendingRoomMode = p.Byte(); }
                if (p.Remaining >= 3)
                {
                    byte rounds = p.Byte(); // param_3[+2] (<0x16)
                    if (rounds >= 1 && rounds < 0x16) PendingRoomRounds = rounds;
                    ushort dur = p.UInt16();
                    PendingRoomSlot = dur;  // o u16 após map/mode/timeFlag É o mapSlot (0x122..0x4ba) — ver Op_0x3B_Recon
                    // RANGE ALARGADO p/ aceitar QUALQUER stage (30s..1h). O cliente é autoritativo da duração
                    // do stage (combate client-side); rejeitar a dur faz o field cair no default 432 -> o 0x48
                    // (RemainingSec = dur+3) anuncia o tempo ERRADO -> o cliente TRAVA o stage (esperava o tempo
                    // do mapa). Era esse o bug do Stage 3 (288s = 0x120, rejeitado pelo floor antigo de 290) —
                    // Stage 2 (432s) passava no range antigo e por isso só ele funcionava. Floor 30s = anti-lixo.
                    if (dur >= 0x1e && dur <= 0xE10) PendingRoomDurationSec = dur;
                }
                if (p.Remaining >= 3)
                {
                    // cauda do 0x3b (captura `... 14 01 0a`): b3=frag/points limit, b4=minLevel, b5=maxLevel —
                    // ecoados de volta ao joiner no header do 0x37 (+f/+8/+9) e no entry do 0x36.
                    PendingRoomFrag = p.Byte();
                    PendingRoomMinLevel = p.Byte();
                    PendingRoomMaxLevel = p.Byte();
                }
            }
            catch { PendingRoomMap = 0; PendingRoomMode = 0; PendingRoomDurationSec = 0; PendingRoomRounds = 0; }
        }

        /// <summary>
        /// Pos-StageClear: depois de mostrar a tela de Rank (0x4A), espera um pouco p/ o overlay aparecer
        /// e entao manda 0x44 (match-end) + refresh do lobby POS-CLEAR (0x1f/0x1e/0x36) -> volta a selecao.
        /// O delay e' o que faz o Rank ser VISIVEL (mandar tudo junto fazia o cliente pular direto pro lobby).
        /// </summary>
        private async Task ScheduleLobbyReturnAfterRankAsync()
        {
            try { await Task.Delay(12000, _cts.Token); }
            catch (OperationCanceledException) { return; }
            catch { }
            if (!Connected) return;
            // SO o 0x44 (match-end): devolve o cliente ao GAME ROOM onde ele estava (p/ rejogar).
            // NAO mandar 0x1f/0x1e/0x36 aqui: o 0x1e e' a LISTA DE CANAIS ("dchannel01") e levava o
            // cliente pra LISTA DE GAMES em vez do room (o original volta pro room apos o stage).
            // Volta ao room lobby (Status=2, InField/FSec) -> shop continua disponivel no room pos-stage.
            InField = true; FieldSecondary = true; SecondActive = true; Status = 2;
            SendEncryptedFrame(LobbyFrames.MatchEnd(2, PendingRoomName.Length > 0 ? PendingRoomName : "asdd"));  // reason=2; nome da sala do domínio
            Log.Ok("lobby", "[{0}] pos-Rank (delay) -> 0x44 match-end (volta ao game room, sala='{1}')", Slot,
                PendingRoomName.Length > 0 ? PendingRoomName : "asdd");
        }

        /// <summary>Handler do 0x53 GameResult (resultado do stage solo, FUN_00425010): traduz bytes -> chamada de
        /// domínio (<see cref="ProgressionService.ApplyStageResult"/>, que credita exp/gold/rank) e serializa as respostas
        /// — level-up (0x51) + ack (0x53). A regra de negócio (progressão) mora no WorldServer; aqui só parse +
        /// validação de limites + serialização. Layout: [stage u8][rank u8][count u8][count×u16 mapSlots][exp u32][gold u32].</summary>
        private void OnStageResult(byte[] data)
        {
            try
            {
                var p = new PacketReader(data);
                byte stage = p.Byte();         // <100 = stage id
                byte rank = p.Byte();          // <6 = grade (0=nenhum,1=D,2=C,3=B,4=A,5=S)
                byte count = p.Byte();         // <5 = qtde de mapSlots (u16) reportados
                ushort[] mapSlots = new ushort[count];
                for (int i = 0; i < count; i++) mapSlots[i] = (p.Remaining >= 2) ? p.UInt16() : (ushort)0;
                if (count > 0) Log.Ok("field", "[{0}] 0x53 mapSlots REAIS do cliente: {1}", Slot,
                    string.Join(",", System.Array.ConvertAll(mapSlots, s => $"0x{s:x4}({s})")));
                uint exp = p.CanRead(4) ? p.UInt32() : 0;
                uint gold = p.CanRead(4) ? p.UInt32() : 0;
                const uint Max = 1_000_000;    // teto de sanidade (anti-cheat, = ValidateGamePoints do 0x50)
                if (exp > Max || gold > Max)
                {
                    Log.Warn("field", "[{0}] 0x53 solo: Wrong Game Point! Exp:{1} Gold:{2} — ignorado", Slot, exp, gold);
                    return;
                }
                int ups = _server.Progression.ApplyStageResult(this, stage, rank, exp, gold);   // domínio: bônus PU + gold + rank + exp
                if (ups > 0) SendLevelUp();                                          // 0x51 ao cliente
                // ACK do 0x53: sem ele o cliente nao comita o resultado (o rank so' entrava na selecao apos relog).
                SendStageResultAck(stage, rank, count, mapSlots);
                Log.Ok("field", "[{0}] 0x53 stage result solo — stage={1} rank={2} exp={3} gold={4}{5}", Slot, stage, rank, exp, gold,
                    ups > 0 ? $" (+{ups} nivel -> {CharLevel})" : "");
            }
            catch (Exception ex) { Log.Warn("field", "[{0}] 0x53 solo parse: {1}", Slot, ex.Message); }
        }

        /// <summary>ACK do 0x53 GameResult (reply de sucesso do FUN_00425010): [53 00][0][stage][rank][count]
        /// [mapSlots:u16...]. O original responde isto a TODO 0x53; sem ele o cliente nao confirma o resultado do
        /// stage (rank/result so' aparecem na selecao apos relog). Canal LOBBY (SendLobby = FUN_004038e0).</summary>
        private void SendStageResultAck(byte stage, byte rank, byte count, ushort[] mapSlots)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x53);
            w.WriteByte(0);          // result = sucesso
            w.WriteByte(stage);
            w.WriteByte(rank);
            w.WriteByte(count);
            for (int i = 0; i < mapSlots.Length; i++) w.WriteWord(mapSlots[i]);
            SendLobby(w.ToArray());
        }

        // ---- handshake UDP -------------------------------------------------------------
        // Fluxo REAL (capturado): o cliente faz ping UDP -> world ecoa 0x0201 (UdpGameplay/BrokerLink)
        // -> cliente manda TCP 0x0e -> TryHandleLobbyEntry responde 0x0e(endpoints)+0x10+... Aqui so
        // registramos o endpoint UDP do jogador (o 0x10 NAO sai aqui — vai apos o 0x0e do cliente).
        public void NotifyUdpReady(IPEndPoint udpEndpoint)
        {
            UdpEndpoint = udpEndpoint;
        }

        /// <summary>Erro de login (FUN_004038e0, cat 3, {0x0C, sub}).</summary>
        public void SendLoginError(byte sub)
        {
            using var p = new PacketWriter();
            p.WriteByte(Protocol.LoginError.Main); // 0x0C
            p.WriteByte(sub);
            SendMessage(Protocol.LoginError.Category, p.ToArray());
        }
    }
}
