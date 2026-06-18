using System;
using System.Buffers.Binary;
using System.Net;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.CharSelect;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        // --- ENTRADA NO LOBBY/CAMPO (síntese do protocolo de canal/sala/stage) ------------------
        // Após o char-select, o cliente faz o handshake UDP (echo 0x0201) e manda esta cadeia TCP; o
        // world responde levando-o à LISTA DE CANAIS (0x1e "dchannel01") e à sala/stage. CLASSIFICAÇÃO:
        //  (1) DADO DE SESSÃO sintetizado do domínio — userid/nome (0x1f/0x1e via BuildSessionFrame),
        //      nome da sala (0x44 via Build44). Hardcodá-los = o bug do userid 999 (cliente cross-valida).
        //  (2) CONSTANTES DE PROTOCOLO — acks de formato fixo que o cliente espera, SEM dado por-sessão
        //      (verificado: o valor estático do capture serviu p/ contas reais distintas). Bytes ainda
        //      não decodados (handles 648c0509, tokens) são constante a refinar por RE — NÃO mudar às cegas.
        private static byte[] Hex(string h) => Convert.FromHexString(h);
        private static readonly byte[] _r10 = Hex("10004e95dd29ce3a55db20b6ad97a65cc01c000000000000"); // GG challenge
        private static readonly byte[] _r14 = Hex("1400000020000000648c0509"); // 0x14 SPAWN: [00 00][20000000][handle]; ALINHADO ao frida do original (arma o scoring/combo)
        // TEMPLATES do channel-lobby de ENTRADA. O USERID (@4/@16 do 1f, @17 do 1e) e o NOME (2 chars) sao
        // PATCHED do dominio em BuildSessionFrame — devem casar com o 0x0C (userid @7, account-name @41),
        // senao o cliente rejeita e TRAVA no char-select re-mandando 0x14. Resto: capture (a sintetizar).
        private static readonly byte[] _r1fTpl = Hex("1f000000e7034a500001000000000008e703000000000000");
        private static readonly byte[] _r1eTpl = Hex("1e000001646368616e6e656c3031000000e7034a500001000000000052a15ef61ea2c65b");
        private static readonly byte[] _r36 = Hex("3600000020000000648c0509"); // ALINHADO ao frida do original ([00 00][20000000][handle], = 0x14)
        // SEGUNDO 0x36 (mitm_full_113423: o original o manda apos o ack UDP 0d03 do cliente; e' ele
        // que ARMA a lista de games — sem ele o botao Create do channel lobby nao faz nada).
        private static readonly byte[] _r36b = Hex("3600006a4dcaaf2b4b3d9fa5");
        private static readonly byte[] _r3b = Hex("3b00000000538b003600007f"); // ALINHADO ao world real (frida)
        private static readonly byte[] _r43 = Hex("43000008010142003b000000"); // ALINHADO ao world real (frida)
        private static readonly byte[] _r48 = Hex("480001b3010000141400a00f"); // ALINHADO ao world real (frida)
        // CONCLUSAO DO STAGE (capturado via frida no world original): cliente manda 0x4A data[0]=0x02
        // (StageClear) -> world responde 0x4A (resultado) + 0x44 (fim de partida -> volta ao lobby).
        private static readonly byte[] _r4a = Hex("4a0002010100737624007c04");      // 0x4A resultado do clear
        // 0x44 (match-end pós-clear) sintetizado em Build44 — o nome da sala vem do domínio (era "asdd").
        // REFRESH do lobby PÓS-CLEAR (capturado do world ORIGINAL via frida — versões DIFERENTES das de
        // entrada _r1fTpl/_r1eTpl/_r36). O original manda estas APÓS o 0x44; reusar as de entrada pulava o Rank.
        // Mesmo patch de userid/nome (BuildSessionFrame): o 1f_clear tem userid so' @4; o 1e_clear @17 e @32.
        private static readonly byte[] _r1fClearTpl = Hex("1f000000e7034a50000100000000000200cae202598fdc71");
        private static readonly byte[] _r1eClearTpl = Hex("1e000001646368616e6e656c3031000000e7034a500001000000000030002503e7030000");
        private static readonly byte[] _r36_clear = Hex("36000022be9a400002000000");

        /// <summary>
        /// 0x0e = OnRecvSuccessUDP. ALINHADO ao frida do world ORIGINAL: ecoa o ENDPOINT DO CLIENTE
        /// (IP+porta que o cliente mandou no ping UDP, 127.0.0.1:2301=08fd big-endian) nos DOIS slots +
        /// trailer ZEROS. Antes mandavamos as portas do SERVER (9f04/9f05) + um "key" inventado -> hipotese:
        /// isso punha o cliente em modo server-authoritative e SUPRIMIA o combo HIT×N (client-side). Ecoar
        /// o endpoint do cliente (como o original) = modo local-scored. Usa o UdpEndpoint aprendido; cai
        /// p/ 2301 se ainda nao conhecido. Formato: [0e 00 00][IP1 4][port1 BE 2][IP2 4][port2 BE 2][9x00].
        /// </summary>
        private byte[] BuildEndpoints0e()
        {
            var ep = UdpEndpoint;
            byte[] ip = ep != null ? ep.Address.MapToIPv4().GetAddressBytes() : new byte[] { 127, 0, 0, 1 };
            ushort port = (ushort)(ep != null ? ep.Port : 2301);
            byte portHi = (byte)(port >> 8), portLo = (byte)(port & 0xff); // big-endian (08 fd p/ 2301)
            byte[] f = new byte[24];
            f[0] = 0x0e; f[1] = 0x00; f[2] = 0x00;
            System.Array.Copy(ip, 0, f, 3, 4); f[7] = portHi; f[8] = portLo;   // endpoint 1 = cliente
            System.Array.Copy(ip, 0, f, 9, 4); f[13] = portHi; f[14] = portLo; // endpoint 2 = cliente
            // f[15..23] = 0 (trailer zeros, igual ao original)
            return f;
        }

        /// <summary>
        /// Patcha um template de frame do channel-lobby (0x1f/0x1e) com o USERID e o NOME (2 chars) do
        /// dominio — devem casar com o 0x0C (userid @7, account-name @41). Sem isso o cliente rejeita e
        /// trava re-mandando 0x14. Passo da remocao do replay: os campos validados vem do dominio; o resto
        /// do frame ainda e' do capture (a sintetizar).
        /// </summary>
        private byte[] BuildSessionFrame(byte[] tpl, int nameAt, params int[] uidAt)
        {
            byte[] f = (byte[])tpl.Clone();
            ushort uid = (ushort)GameInfoId;
            foreach (int o in uidAt) BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(o), uid);
            byte[] nb = System.Text.Encoding.ASCII.GetBytes(LoginCharList?.AccountName ?? CharName ?? "");
            f[nameAt] = nb.Length > 0 ? nb[0] : (byte)0;
            f[nameAt + 1] = nb.Length > 1 ? nb[1] : (byte)0;
            return f;
        }

        /// <summary>0x44 match-end (volta ao game room pós-clear): [44 00][reason 00][01 00 00 00][nome da
        /// sala]. O nome vem do domínio (PendingRoomName, do 0x3b) — era "asdd" hardcoded do capture. É o
        /// ÚLTIMO campo do frame (lido até o fim), então tamanho variável é seguro.</summary>
        private byte[] Build44(byte reason)
        {
            byte[] nb = System.Text.Encoding.ASCII.GetBytes(PendingRoomName.Length > 0 ? PendingRoomName : "asdd");
            byte[] f = new byte[8 + nb.Length];
            f[0] = 0x44; f[2] = reason; f[4] = 0x01;
            System.Array.Copy(nb, 0, f, 8, nb.Length);
            return f;
        }

        private bool TryHandleLobbyEntry(ushort opcode, byte[] data)
        {
            switch (opcode)
            {
                case 0x0e: // pedido de UDP-success -> endpoints + GG
                    SendEncryptedFrame(BuildEndpoints0e());
                    SendEncryptedFrame(_r10);
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
                    SendEncryptedFrame(_r14);
                    SendEncryptedFrame(BuildSessionFrame(_r1fTpl, 6, 4, 16));
                    SendEncryptedFrame(BuildSessionFrame(_r1eTpl, 19, 17));
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
                    SendEncryptedFrame(_r36);
                    // 0x36b (arma a lista de games p/ o botao Create) SÓ na 1a vez. A captura do original
                    // mostra UM 0x36 por request; remandar o 0x36b a cada poll re-armava a game-list e
                    // mantinha o cliente em polling (telas sobrepostas), travando o Previous.
                    if (!_r36bSent) { _r36bSent = true; SendEncryptedFrame(_r36b); }
                    Log.Info("lobby", "[{0}] 0x36 ack{1}", Slot, _r36bSent ? " (+0x36b 1a vez)" : "");
                    return true;
                case 0x3b: // CRIAR SALA: room lobby = Status=2 (FieldLobby) + InField + FieldSecondary. (Era
                    // Status=3; 2 e' "sala montada antes do match" -> habilita shop 0x2d/2e/2f. O stage (0x4b)
                    // promove a Status=3 via CreateField.)
                    FieldSecondary = true; SecondActive = true; Status = 2; InField = true;
                    ParseRoomCreate(data); // guarda map/mode da sala -> decide stage (client-side) vs Battle (networked)
                    // nova sala = novo match: rearma o StartGameClock (a trava e' por ENTRADA NO STAGE,
                    // nao por sessao — sem isto a 2a sala da mesma sessao ficava com o field morto)
                    System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
                    SendEncryptedFrame(_r3b);
                    Log.Ok("lobby", "[{0}] 0x3b sala criada -> room lobby (Status=2, FSec, map={1} mode={2})",
                        Slot, PendingRoomMap, PendingRoomMode);
                    return true;
                case 0x43: // engage/start do match: rearma o clock p/ REMATCH na mesma sala
                    System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
                    SendEncryptedFrame(_r43);
                    Log.Info("lobby", "[{0}] 0x43 resp (clock rearmado)", Slot);
                    return true;
                case 0x48: SendEncryptedFrame(_r48); Log.Info("lobby", "[{0}] 0x48 resp", Slot); return true;
                case 0x4A: // 0x4A com data[0]=0x02 = StageClear. Combate usa outras 0x4A -> nao intercepta.
                    if (data.Length >= 1 && data[0] == 0x02)
                    {
                        // Manda SO a tela de Rank agora; o 0x44 (match-end) + refresh do lobby vem com um
                        // pequeno DELAY p/ o overlay de Rank ter tempo de aparecer. (Mandar tudo junto, ou
                        // disparar no 0x53, fazia o Rank sumir.) O 0x53 GameResultReport que o cliente manda
                        // logo apos e' CONSUMIDO (case 0x53) p/ nao cair no handler que desconecta em solo.
                        SendEncryptedFrame(_r4a);        // tela de RANK
                        Log.Ok("lobby", "[{0}] 0x4A StageClear -> Rank (0x44+refresh do lobby em ~5s)", Slot);
                        _ = ScheduleLobbyReturnAfterRankAsync();
                        return true;
                    }
                    return false;
                case 0x3A: // FieldLeaveGame: sair do game room -> volta a LISTA DE GAMES do canal. O handler
                    // real (Op_FieldLeaveGame) desconecta (DISC 0x50: guard InField&&FieldSecondary que o
                    // estado do solo nao satisfaz apos o stage). Tratamos aqui: reset + refresh da lista
                    // (1f/1e/36, os MESMOS frames capturados do original APOS o 0x44 = o "voltar pra lista").
                    InField = true; FieldSecondary = true; SecondActive = true; Status = 2; // volta ao channel lobby (shop segue ok)
                    SendEncryptedFrame(BuildSessionFrame(_r1fClearTpl, 6, 4));
                    SendEncryptedFrame(BuildSessionFrame(_r1eClearTpl, 19, 17, 32));
                    SendEncryptedFrame(_r36_clear);
                    Log.Ok("lobby", "[{0}] 0x3A FieldLeaveGame -> lista de games (channel lobby Status=2)", Slot);
                    return true;
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
                        if (BoxItems[i] != 0) SendBoxAdd(BoxItems[i], (byte)i, 1);
                    // quickslot: FALLBACK do auto-render. Se a pintura na entrada do lobby (0x14) ainda não
                    // pegou (widget do potion-bar não construído na hora), pinta aqui no 1o open. Guard
                    // próprio (_potionPainted) -> não repinta nas reentradas.
                    if (!_potionPainted) { _potionPainted = true; PaintQuickslot(); }
                    return true;
                case 0x2D: // FIEL à captura: o 0x2d responde SEMPRE o ack curto (handle), NUNCA 0x13.
                    // O original (test com box vazio) so' manda o ack; mandar 0x13 (lista) prendia o cliente
                    // re-pedindo 0x2d (telas sobrepostas) e o Previous nao voltava p/ a lista de salas.
                    SendInventoryAck(1);
                    return true;
                case 0x53: // GameResultReport pos-clear (SOLO PvE). O handler real (Op_0x53_Recon) exige o
                    // field-record do match-engine, que esta OFF no solo (combate client-side) e DESCONECTAVA.
                    // Consumimos aqui MAS creditamos o exp/gold reportados (FUN_00425010: [idx][cfgA][cfgB]
                    // [cfgB x u16][exp u32][gold u32]) — antes o stage clear nao dava progresso nenhum.
                    CreditSoloResult(data);
                    return true;
                case 0x4b: // SPAWN no stage (72B). Inicia o relogio da partida: um timer incrementa
                    // GameSeq e manda o tick 1583 (o cliente ecoa o seq; seq avancando = timer corre).
                    InField = true;
                    StartGameClock();
                    Log.Ok("lobby", "[{0}] 0x4b (spawn) -> STAGE; relogio de gameplay iniciado (udp={1})", Slot, UdpEndpoint?.ToString() ?? "-");
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
                default:
                    // Shop list/loadout (0x2d/0x2f) E buy (0x2e) DEVEM chegar aos handlers reais
                    // (Op_RoomRosterSync/Op_GroupMemberInfo/Op_RoomMemberQuery) -> NAO consumir aqui, deixa o
                    // Dispatch rodar (gate passa: channel/room lobby = InField+FSec+Status=2).
                    if (opcode == 0x2d || opcode == 0x2e || opcode == 0x2f || opcode == 0x33) return false;
                    // Salas BATTLE/PvP (Mode != 0): os opcodes de COMBATE vao aos handlers reais do
                    // motor de partida — 0x4d (par golem/facing: y==0 = golem inimigo destruido ->
                    // fim de round), 0x4f (morte), 0x3d (troca de arma), 0x50 (reporte de exp/gold do
                    // fim de partida -> level-up). No SOLO (Mode 0) continuam engolidos: o combate e'
                    // client-side e o 0x4d (y==0) encerraria o stage. (0x46 tem case próprio acima — solo tb.)
                    if (opcode == 0x4d || opcode == 0x4f || opcode == 0x3d || opcode == 0x50)
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
            var list = (LoginCharList ?? new CharList()) with { SessionHandle = _invHandle };
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
            Log.Ok("field", "[{0}] sala aplicada ao field {1}: mode={2} map={3} dur={4}s rounds={5}",
                Slot, f.Id, f.Mode, f.MapId, f.RoundDurationSec, f.MaxRounds);
            // Sala Battle/PvP (mode != 0) = fluxo NETWORKED: o SERVER inicia o loop UDP com o 1o
            // tick 1583 (mitm_full_113423: o original manda 1583 ANTES do 1o 0040 do cliente; sem
            // ele o input fica congelado). Stage solo (mode 0) fica client-side: sem tick.
            if (f.Mode != 0 && UdpEndpoint != null) _server.SendGameplayTick(UdpEndpoint, LastInput);
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
                PendingRoomName = p.CString(0x29); p.CString(9); p.CString(0xc9); // nome (->0x44), senha, desc
                if (p.Remaining >= 2) { PendingRoomMap = p.Byte(); PendingRoomMode = p.Byte(); }
                if (p.Remaining >= 3)
                {
                    byte rounds = p.Byte(); // param_3[+2] (<0x16)
                    if (rounds >= 1 && rounds < 0x16) PendingRoomRounds = rounds;
                    ushort dur = p.UInt16();
                    // RANGE ALARGADO p/ aceitar QUALQUER stage (30s..1h). O cliente é autoritativo da duração
                    // do stage (combate client-side); rejeitar a dur faz o field cair no default 432 -> o 0x48
                    // (RemainingSec = dur+3) anuncia o tempo ERRADO -> o cliente TRAVA o stage (esperava o tempo
                    // do mapa). Era esse o bug do Stage 3 (288s = 0x120, rejeitado pelo floor antigo de 290) —
                    // Stage 2 (432s) passava no range antigo e por isso só ele funcionava. Floor 30s = anti-lixo.
                    if (dur >= 0x1e && dur <= 0xE10) PendingRoomDurationSec = dur;
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
            SendEncryptedFrame(Build44(2));   // reason=2 (match-end pós-clear); nome da sala do domínio
            Log.Ok("lobby", "[{0}] pos-Rank (delay) -> 0x44 match-end (volta ao game room, sala='{1}')", Slot,
                PendingRoomName.Length > 0 ? PendingRoomName : "asdd");
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
