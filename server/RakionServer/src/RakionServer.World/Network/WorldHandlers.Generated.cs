using System;
using System.Collections.Generic;
using System.Linq;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    // GERADO pelo workflow multi-agente (rakion-handlers-reconstruct): os 53 handlers
    // de gameplay reconstruidos do decompile (handlers.out.txt) e verificados.
    // O static ctor repointa a Table (de Stub) para estes metodos.
    public static partial class WorldHandlers
    {
        static WorldHandlers() => RegisterGenerated();

        private static void RegisterGenerated()
        {
            Table[0x14] = new("FieldGameStart", 0x41fef0, Op_FieldGameStart);
            Table[0x1b] = new("FieldJoinById", 0x4208e0, Op_FieldJoinById);
            Table[0x1c] = new("FieldJoinByName", 0x420a40, Op_FieldJoinByName);
            Table[0x2d] = new("RoomRosterSync", 0x420f10, Op_RoomRosterSync);
            Table[0x2e] = new("RoomMemberQuery", 0x421210, Op_RoomMemberQuery);
            Table[0x33] = new("InventoryAllocationPoint", 0x4229f0, Op_InventoryAllocationPoint);
            Table[0x2f] = new("GroupMemberInfo", 0x4215a0, Op_GroupMemberInfo);
            Table[0x34] = new("GroupListQuery", 0x422b10, Op_GroupListQuery);
            Table[0x36] = new("FieldPlayerList", 0x422c90, Op_FieldPlayerList);
            Table[0x3a] = new("FieldLeaveGame", 0x4234e0, Op_FieldLeaveGame);
            Table[0x3b] = new("RoomCreate", 0x423580, Op_RoomCreate);
            // 0x3d/0x46/0x4d/0x4f: NAO sobrescrever — a tabela base (Build) aponta p/ as versoes
            // Recon (ReconCombatA/B/RoomB), integradas ao motor de partida (Field/wins/settle).
            // As versoes geradas da mesma FUN_xxxx nao mexem no estado do match (ex.: o 0x4d
            // gerado ignorava o golem destruido -> a partida terminava em empate por timeout).
            Table[0x3e] = new("FieldGameReady", 0x423b70, Op_FieldGameReady);
            Table[0x3f] = new("FieldGameStart_3f", 0x423c00, Op_FieldGameStart_3f);
            Table[0x40] = new("FieldSetGameMode", 0x423cc0, Op_FieldSetGameMode);
            Table[0x41] = new("FieldCreateRoomEntry", 0x423dd0, Op_FieldCreateRoomEntry);
            Table[0x42] = new("FieldUnitCommand", 0x424100, Op_FieldUnitCommand);
            Table[0x43] = new("FieldUnitStop", 0x424210, Op_FieldUnitStop);
            Table[0x45] = new("FieldUnitByteAction", 0x4242c0, Op_FieldUnitByteAction);
            Table[0x4a] = new("FieldUnitCharAction", 0x4246e0, Op_FieldUnitCharAction);
            Table[0x4b] = new("FieldChatBroadcast", 0x4247b0, Op_FieldChatBroadcast);
            Table[0x4c] = new("FieldTaggedBroadcast", 0x424880, Op_FieldTaggedBroadcast);
            // 0x50: NAO sobrescrever — vale Op_0x50_Recon (exp/gold + level-up; o gerado
            // Op_GamePointSettle desconectava com DISC 0x95 no settle pos-round do PvP).
            Table[0x53] = new("GameResultReport", 0x425010, Op_GameResultReport);
            Table[0x56] = new("GameChat", 0x425620, Op_GameChat);
            Table[0x57] = new("GameVoiceChat", 0x4256d0, Op_GameVoiceChat);
            Table[0x59] = new("GameEmoteAction", 0x4257b0, Op_GameEmoteAction);
            Table[0x5a] = new("GameWhisperToSlot", 0x425860, Op_GameWhisperToSlot);
            Table[0x5b] = new("FieldPlayerAction", 0x425990, Op_FieldPlayerAction);
            Table[0x5d] = new("FieldChatNamed", 0x425a70, Op_FieldChatNamed);
            Table[0x5e] = new("FieldChatCode", 0x425bb0, Op_FieldChatCode);
            Table[0x60] = new("FieldTargetCommand", 0x425cc0, Op_FieldTargetCommand);
            Table[0x61] = new("SetUserPing", 0x41c270, Op_SetUserPing);
            Table[0x62] = new("FieldRelayAction", 0x41c2b0, Op_FieldRelayAction);
            Table[0x64] = new("VerifyTutorialStage", 0x4283a0, Op_VerifyTutorialStage);
            Table[0x65] = new("VerifyClientHash", 0x428430, Op_VerifyClientHash);
            Table[0x6b] = new("RequestFieldTick", 0x4286a0, Op_RequestFieldTick);
            Table[0x6c] = new("RequestFieldSnapshot", 0x428750, Op_RequestFieldSnapshot);
            Table[0x6d] = new("FieldEmoteEcho", 0x428a10, Op_FieldEmoteEcho);
            Table[0x6e] = new("FieldUseItem", 0x428c90, Op_FieldUseItem);
            Table[0x6f] = new("RoomReadyEmblem", 0x428d80, Op_RoomReadyEmblem);
            Table[0x70] = new("RoomRankReward", 0x4292b0, Op_RoomRankReward);
            Table[0x71] = new("RoomFixedReward", 0x4293f0, Op_RoomFixedReward);
            Table[0x72] = new("RoomMemberFieldInfo", 0x428520, Op_RoomMemberFieldInfo);
            Table[0x73] = new("RoomCharSelectInfo", 0x421a50, Op_RoomCharSelectInfo);
            Table[0x74] = new("RoomMoveAction", 0x421e10, Op_RoomMoveAction);
            Table[0x75] = new("BuyLotto", 0x4222a0, Op_BuyLotto);
            Table[0x76] = new("RoomReadyState", 0x4225d0, Op_RoomReadyState);
            Table[0x77] = new("ServerInfoDump", 0x41be60, Op_ServerInfoDump);
            Table[0x78] = new("FieldStateQuery", 0x41bde0, Op_FieldStateQuery);
            Table[0x79] = new("DisconnectNotText", 0x422270, Op_DisconnectNotText);
        }

        private static void Op_FieldGameStart(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_0041fef0: guard via this_00[0x518]/[0x529] (= user+0x1460 InField / user+0x14a4 FieldSecondary).
                    // this_00[0x518]==0 (nao esta no field) OU this_00[0x529]!=0 (ja iniciou/secundario) -> erro 0x1d.
                    if (!u.InField || u.FieldSecondary)
                    {
                        // FUN_0041eb20(this, slot, 0x1d, '\0', 1): erro NAO-fatal (cVar5=0).
                        using var we = new PacketWriter();
                        we.WriteWord(0x1d);
                        we.WriteByte(0);
                        u.SendLobby(we.ToArray());
                        Log.Debug($"[FieldGameStart] slot={u.Slot} rejeitado: estado invalido (InField={u.InField} Secondary={u.FieldSecondary})");
                        return;
                    }

                    // *param_3 = id-alvo (int). Se 0 -> erro 0x1e com cVar5='\x01'.
                    int target = ctx.P.Int32();
                    if (target == 0)
                    {
                        using var we = new PacketWriter();
                        we.WriteWord(0x1e);
                        we.WriteByte(1);
                        u.SendLobby(we.ToArray());
                        Log.Debug($"[FieldGameStart] slot={u.Slot} rejeitado: target id == 0");
                        return;
                    }

                    // Varredura dos 6 slots de membro do usuario (this_00 + idx*0xd8, idx<6), procurando *piVar1 == target.
                    // Ao achar: FUN_0040be30 (carrega stats/char do membro), FUN_0040d3f0 (recalcula derivados a partir
                    // de piVar1+0x356 e piVar1[0xd6]), FUN_0040ac30 (faz o spawn/broadcast in-game com os stats) e copia
                    // piVar1+0xd7 -> user+0x2368 (ready-flag). Esses helpers nao tem corpo neste slice; modelados como
                    // 'membro encontrado -> sucesso' contra a lista de jogadores do field.
                    byte resultCode = 0;       // local_100a
                    bool found = false;
                    var field = ctx.World.GetField(u.FieldId);
                    if (field != null)
                    {
                        foreach (var m in field.Players)
                        {
                            if (m.Slot == target) { found = true; break; }
                        }
                    }
                    // iVar2 == 6 (nenhum dos 6 slots casou) -> resultCode = 2.
                    if (!found) { resultCode = 2; }

                    // FUN_004038e0(handler, slot, 3, &local_1004): resposta lobby subtype 0x14 + byte resultCode (len 3).
                    using var w = new PacketWriter();
                    w.WriteWord(0x14);         // local_1004 subtype
                    w.WriteByte(resultCode);   // local_1002
                    u.SendLobby(w.ToArray());
                    Log.Info($"[FieldGameStart] slot={u.Slot} target={target} result={resultCode} (found={found})");

                    // FUN_0041b8b0(this, slot): notificacao pos-start ao field. Sem corpo neste slice; modelado como
                    // broadcast informando que este jogador entrou em jogo, para os demais membros do field.
                    if (field != null && resultCode == 0)
                    {
                        using var bw = new PacketWriter();
                        bw.WriteWord(0x14);
                        bw.WriteByte((byte)u.Slot);
                        field.Broadcast(bw.ToArray(), u);
                    }
                }


        private static void Op_FieldJoinById(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField)
            {
                u.Disconnect(0x2b);
                return;
            }

            byte type = ctx.P.Byte();
            ushort arg = 0;
            if (type != 0)
                arg = ctx.P.UInt16();

            uint fieldSecondary = (uint)(u.FieldSecondary ? 1 : 0); // user+0x14a4 (local_1008)

            // TODO FUN_0040bd80(this_00, type, arg, &a, &b, &c): resolves the target field/room
            // and fills a/b/c (u32) when type==1. Helper not in this body. Modeled as success.
            byte status = 0;
            uint a = 0, b = 0, c = 0;

            // efeito de FUN_0040bd80 + join: vincula o usuario ao field alvo (type==1 -> arg = fieldId)
            if (type == 0x01)
            {
                var jf = ctx.World.GetField(arg);
                if (jf == null || jf.Count >= jf.MaxPlayers) status = 1;
                else { jf.Add(u); u.FieldId = jf.Id; a = (uint)(jf.Master?.Game?.UserId ?? 0); }
            }

            if (status == 0)
            {
                // SUCCESS -> plain channel (SendMessage), subtype 0x10.
                using var w = new PacketWriter();
                w.WriteUInt32(fieldSecondary); // local_1000
                w.WriteByte(type);             // local_ffc
                if (type == 0x01)
                {
                    w.WriteUInt32(a);          // local_ffb
                    w.WriteUInt32(b);          // local_ff7
                    w.WriteUInt32(c);          // local_ff3
                    w.WriteWord(arg);          // local_fef
                }
                u.SendMessage(0x10, w.ToArray());
            }
            else
            {
                // FAIL -> lobby channel (SendLobby), subtype 0x1b + status byte.
                using var w = new PacketWriter();
                w.WriteWord(0x1b);
                w.WriteByte(status);
                u.SendLobby(w.ToArray());
            }
        }

        private static void Op_FieldJoinByName(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!u.InField)
            {
                u.Disconnect(0x2c);
                return;
            }

            uint fieldSecondary = (uint)(u.FieldSecondary ? 1 : 0); // user+0x14a4 (local_1018)

            string name = ctx.P.CString();
            byte type = ctx.P.Byte();
            ushort arg = 0;
            if (type != 0)
                arg = ctx.P.UInt16();

            // TODO FUN_0040bd80(this_00, type, arg, &a, &b, &c): resolves the named field/room
            // and fills a/b/c (u32) when type==1. Helper not in this body. Modeled as success.
            byte status = 0;
            uint a = 0, b = 0, c = 0;

            if (status != 0)
            {
                // FAIL -> lobby channel, subtype 0x1c + status byte.
                using var fw = new PacketWriter();
                fw.WriteWord(0x1c);
                fw.WriteByte(status);
                u.SendLobby(fw.ToArray());
                return;
            }

            // SUCCESS -> plain channel (SendMessage), subtype 0x11.
            using var w = new PacketWriter();
            w.WriteUInt32(fieldSecondary); // uStack_1000
            w.WriteCString(name);          // aCStack_ffc (name + null)
            w.WriteByte(type);             // cVar1
            if (type == 0x01)
            {
                w.WriteUInt32(a);          // uStack_1020
                w.WriteUInt32(b);          // uStack_1024
                w.WriteUInt32(c);          // uStack_1028
                w.WriteWord(arg);          // uVar2
            }
            u.SendMessage(0x11, w.ToArray());
        }

        private static void Op_RoomRosterSync(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1: must be InField and in the secondary (active) state.
            if (!u.InField || !u.FieldSecondary)
            {
                u.Disconnect(0x34);
                return;
            }
            // Guard 2: must be in room state (Status == 0x02).
            if (u.Status != 0x02)
            {
                u.Disconnect(0x35);
                return;
            }

            // 0x2d = SendInventoryLeave (FECHAR o inventario), NAO a lista de itens! (engine.dll:
            // SendInventoryLeave -> opcode 0x2d). O handler real (FUN_00420f10) responde com os blocos de
            // itens, mas ESSES ITENS (u16 itemIds) CRASHAM nosso cliente GG-removido — igual aos itens do
            // 0x0C @139 ("Cannot open file"/fecha o jogo). Entao respondemos so o VAZIO [0x2d][0] (o caminho
            // n1==n2==n3==0 do exe): o cliente fecha o inventario e volta pro lobby SEM crash.
            using var fw = new PacketWriter();
            fw.WriteWord(0x2d);
            fw.WriteByte(0);
            u.SendLobby(fw.ToArray());
            Log.Ok("shop", "[{0}] 0x2d InventoryLeave -> resposta vazia (sem itens; evita crash do cliente)", u.Slot);
        }

        private static void Op_InventoryAllocationPoint(HandlerContext ctx) // 0x33 = SendInventoryAllocationPoint (FUN_004229f0+FUN_0040b3d0)
        {
            var u = ctx.User;
            if (!u.InField || !u.FieldSecondary) { u.Disconnect(0x43); return; }  // FUN_004229f0 guards
            if (u.Status != 0x02) { u.Disconnect(0x44); return; }
            byte statIdx = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;   // *param_3 = qual stat (0..9)
            if (statIdx > 9) { SendAllocResult(u, 5); return; }          // erro 5: indice invalido
            if (u.CharLevelPoint == 0) { SendAllocResult(u, 3); return; } // erro 3: sem level-points
            // aloca: stat++ e level-point-- (cap por-classe omitido). Cosmetico: o stage usa o spawn estatico,
            // entao reflete no DISPLAY do char (a tela de status), nao no combate. Sessao-only (reseta no relogin).
            u.Stats[statIdx]++;
            u.CharLevelPoint--;
            // RESPOSTA sucesso (FUN_004229f0, 10B): [0x33][0][levelpoint u16][bonus u16][stat u8][newStat u16]
            using var w = new PacketWriter();
            w.WriteWord(0x33);
            w.WriteByte(0);                          // status sucesso
            w.WriteWord((ushort)u.CharLevelPoint);   // levelpoint novo (this+0x1566)
            w.WriteWord(0);                          // bonus points (this+0x2370) = 0
            w.WriteByte(statIdx);                    // qual stat
            w.WriteWord(u.Stats[statIdx]);           // novo valor do stat alocado
            u.SendLobby(w.ToArray());
            Log.Ok("shop", "[{0}] 0x33 alloc stat {1} -> {2} (level-points restantes {3})", u.Slot, statIdx, u.Stats[statIdx], u.CharLevelPoint);
        }

        // erro/graceful da alocacao: SendLobby([0x33][err]) (FUN_004229f0 caminho de erro, 3B)
        private static void SendAllocResult(ClientSession u, byte err)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x33);
            w.WriteByte(err);
            u.SendLobby(w.ToArray());
        }

        private static void Op_RoomMemberQuery(HandlerContext ctx) // 0x2e = ShopBuy (FUN_00421210 + FUN_0040cb10)
        {
            var u = ctx.User;
            // Gate (FUN_00421210 L189): exige sessao ativa. O original responde ERRO (FUN_0041eb20 code 0x36),
            // NUNCA desconecta — e o cliente GG-removido CRASHA no disconnect (0x0e). InField NAO e' exigido: a
            // compra acontece no lobby/inventario (fora de campo), onde InField/FieldSecondary sao false.
            if (!u.SlotActive) { SendShopError(u, 0x36); return; }

            // parse do payload de COMPRA: [u16 itemId][u8 off2=moeda][u8 cVar1][u16 off4 se cVar1==1]
            if (!ctx.P.CanRead(4)) { u.Disconnect(0x37); return; }
            ushort itemId = ctx.P.UInt16();   // off0
            byte off2 = ctx.P.Byte();         // off2 = SELETOR DE MOEDA: 0=CASH (user+0x153c), !=0=GOLD (user+0x1538)
            byte cVar1 = ctx.P.Byte();        // off3 (0=normal; 1=tem token anti-cheat em off4)
            ushort off4 = 0;
            if (cVar1 == 0x01 && ctx.P.CanRead(2)) off4 = ctx.P.UInt16(); // token (apenas ecoado)

            // item tem que existir no catalogo (FUN_00421210 L1749: itemId < this+0x108 = teto de item-id).
            var def = ctx.World.FindItemDef(itemId);
            if (def == null) { Log.Warn("shop", "[{0}] BUY item {1} inexistente -> erro 3", u.Slot, itemId); SendShopError(u, 3); return; }

            bool payGold = off2 != 0;                       // FUN_0040cb10 L70: param_2=='\0' => CASH; senao GOLD
            int price = payGold ? def.Gold : def.Cash;      // preco base (mult so p/ itens lotto 11000-11999)
            if (price < 0) price = 0;

            if (u.ShopBuyInProgress) { SendShopError(u, 2); return; } // anti-duplo-clique (user+0x144c==2)

            uint balance = payGold ? u.Gold : u.Cash;
            if (balance < (uint)price)
            {
                Log.Info("shop", "[{0}] BUY item {1} negado: saldo {2} < preco {3} ({4})", u.Slot, itemId, balance, price, payGold ? "GOLD" : "CASH");
                SendShopError(u, 3); return;
            }

            // debita em memoria (fonte da resposta/HUD imediatos)
            uint newBalance = balance - (uint)price;
            if (payGold) u.Gold = newBalance; else u.Cash = newBalance;
            u.ShopBuyInProgress = true;
            // slot do box = contagem atual do armazem (cada compra vai pra proxima celula livre, sem sobrescrever).
            byte invSlot = (byte)(u.BoxItems.Count & 0x77);
            // Só GEAR (type<=5) entra no display do box: não-gear (transform/especial) renderiza invisível e
            // crasha o painel. Ele ainda é comprado (debita) e persiste no itembox (DB) abaixo — só não pinta
            // no grid. Assim BoxItems = sempre gear -> todos os loops de paint (0x13/0x2e/0x31) ficam limpos.
            if (ctx.World.IsBoxDisplayable(itemId))
                u.BoxItems.Add(itemId);   // persiste no estado da sessao -> exibido no proximo 0x2d + slot incrementa
            // NAO adiciona em u.Items (= useriteminfo/APARENCIA equipada): o item de box NUNCA vai pro corpo 3D (crash).

            Log.Ok("shop", "[{0}] BUY item {1} type={2} por {3} {4} (saldo {5}->{6}) char={7} slot={8}",
                u.Slot, itemId, def.Type, price, payGold ? "GOLD" : "CASH", balance, newBalance, u.ActiveCharId, invSlot);

            // PERSISTE em background (handler e sync; DB e async). Reverte saldo em memoria se falhar.
            int gameInfoId = u.GameInfoId; string acct = u.UserId;
            var db = ctx.World.Db; bool gold = payGold; int p = price;
            System.Threading.Tasks.Task.Run(async () =>
            {
                bool ok = true;
                try
                {
                    if (gold) { if (gameInfoId > 0) await db.AddGoldAsync(gameInfoId, -p); else ok = false; }
                    else { if (!string.IsNullOrEmpty(acct)) await db.AddCashAsync(acct, -p); else ok = false; }
                    if (ok && gameInfoId > 0)
                        ok = await db.InsertItemBoxAsync(gameInfoId, itemId, 0) > 0; // BOX (itembox), nao useriteminfo
                    else ok = false;
                }
                catch (System.Exception ex) { ok = false; Log.Error("shop", "[{0}] persist BUY item {1}: {2}", u.Slot, itemId, ex.Message); }
                finally
                {
                    if (!ok) { if (gold) u.Gold += (uint)p; else u.Cash += (uint)p; Log.Warn("shop", "[{0}] persist BUY item {1} FALHOU -> saldo revertido (+{2} {3})", u.Slot, itemId, p, gold ? "GOLD" : "CASH"); }
                    u.ShopBuyInProgress = false;
                }
            });

            // --- RESPOSTA SUCESSO (msgType 0x14) ---
            // O cliente despacha pelo 1o u16 (frame LOBBY = [msgType][aux][payload], confirmado no MITM:
            // W->C u16a=msgType). FUN_0041b940 envia [0x14][serverSeq][payload]. NAO usar SendMessage (poe o
            // serverSeq primeiro -> cliente leria msgType=0x0E -> "OnRecvSuccessUDP error"). Usar SendLobby
            // (msgType-first, igual ao Op_RoomReady e ao erro 0x2e). payload @off4 = InField/FieldSec/off2/cVar1.
            uint inField = (uint)(u.InField ? 1 : 0);
            uint fieldSecondary = (uint)(u.FieldSecondary ? 1 : 0);
            using var w = new PacketWriter();
            w.WriteWord(0x14);             // msgType (1o u16 = dispatch do cliente)
            w.WriteWord(0);                // aux (posicao do serverSeq no original; 0 como o 0x14 de entrada)
            w.WriteUInt32(inField);        // off4
            w.WriteUInt32(fieldSecondary); // off8
            w.WriteByte(off2);             // off0xC (byte de moeda ecoado)
            w.WriteInt32((sbyte)cVar1);    // off0xD (cVar1 como int)
            w.WriteByte(cVar1);            // off0x11
            if (cVar1 == 0x01) { w.WriteUInt32(0); w.WriteUInt32(0); w.WriteUInt32(0); w.WriteWord(off4); }
            // DELTA do inventario: c2=1 com o item novo (+0x1bc4). Sem isso o cliente fecha o dialog mas NAO
            // adiciona o item nem desconta o gold ("nada aconteceu"). Bloco c2 = c2*u32(itemId) ++ c2*u8(slot).
            w.WriteByte(0);                  // c1 = 0 (sem delta de loadout +0x1b78)
            w.WriteByte(1);                  // c2 = 1 (1 item novo no inventario)
            w.WriteUInt32((uint)itemId);     // block2a: itemId (c2*u32)
            w.WriteByte(invSlot);            // block2b: slot/celula do Box (c2*u8)
            w.WriteByte(0);                  // c3 = 0
            u.SendLobby(w.ToArray());
            // BOX render: o grid so' pinta em menu de loja (FUN_0047d1d0/FUN_004774e0 gate 0x19/1a/1b). A COMPRA
            // acontece com o menu garantidamente em loja — UNICO momento confiavel. Entao re-pinto TODO o box
            // (0x31 de cada item, slot=indice), nao so' o comprado. Assim os itens PERSISTIDOS do itembox (que
            // foram mandados antes em menu errado e ficaram invisiveis) aparecem junto com o novo. (Log provou:
            // o comprado ia pro slot N=BoxItems.Count e so' ele pintava; os 0..N-1 ficavam invisiveis.)
            for (int i = 0; i < u.BoxItems.Count && i < 0x78; i++) u.SendBoxAdd(u.BoxItems[i], (byte)i, 1);

            // --- SALDO EM TEMPO REAL (msgType 0x2e, code 0) ---
            // RE do cliente: o HUD le gold em AccountInfo+0x64 e cash +0x68 (engine.dll). O handler 0x2e
            // (FUN_004774e0 case 0) grava gold=param_2 -> +0x64 e cash=param_3 -> +0x68 ANTES do gate de
            // menu-state, entao count=0 atualiza o saldo AO VIVO sem mexer no box. O original manda ESTE 0x2e
            // (saldo+itens) ALEM do 0x14 (ack) na compra (sender = FUN_00427b10 via FUN_004038e0=SendLobby);
            // eu so' mandava o 0x14, por isso o saldo so' aparecia no relog. Formato byte-a-byte do FUN_00427b10:
            // [0x2e][code=0][gold u32][cash u32][count u8][slots..][items u16..][types..][flag u8]. count=0 = so' saldo.
            using var gw = new PacketWriter();
            gw.WriteWord(0x2e);            // msgType (dispatch do cliente)
            gw.WriteByte(0);               // code = 0 (sucesso)
            gw.WriteUInt32(u.Gold);        // novo saldo de gold -> AccountInfo+0x64 (HUD)
            gw.WriteUInt32(u.Cash);        // novo saldo de cash -> AccountInfo+0x68
            gw.WriteByte(0);               // count = 0 (so' saldo; o item do box ja foi pelo 0x31 acima)
            gw.WriteByte(0);               // flag final = 0
            u.SendLobby(gw.ToArray());
            Log.Ok("shop", "[{0}] 0x2e saldo ao vivo: gold={1} cash={2}", u.Slot, u.Gold, u.Cash);
        }

        /// <summary>Erro de compra: SendLobby([u16 0x2e][u8 err]). err: 2=em progresso, 3=sem dinheiro/item inexistente.</summary>
        private static void SendShopError(ClientSession u, byte err)
        {
            using var fw = new PacketWriter();
            fw.WriteWord(0x2e);
            fw.WriteByte(err);
            u.SendLobby(fw.ToArray());
        }

        private static void Op_GroupMemberInfo(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: precisa estar no field e com field secundario ativo (this_00+0x1460 != 0 && this_00+0x14a4 != 0)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x39); return; }
            // Guard: status precisa ser '\x02' (room)
            if (u.Status != 0x02) { u.Disconnect(0x3a); return; }

            // payload: [byte slotArg]  (= *param_3 = bVar2)
            byte slotArg = ctx.P.Byte();
            if (slotArg >= 0x78) { u.Disconnect(0x3b); return; }

            var items = u.Items ?? new System.Collections.Generic.List<RakionServer.World.Database.UserItem>();
            // escalares do slot consultado (FUN_0040cd70): se o slot nao casa, ficam 0 (UI tolera)
            ushort groupId = 0; uint field1 = 0; uint field2 = 0; uint field3 = 0; byte b1 = 0;
            foreach (var it in items)
            {
                if (it.Slot != slotArg) continue;
                groupId = (ushort)it.ItemSn; field2 = (uint)it.ItemId; b1 = it.Level; field3 = (uint)it.LimitTime; break;
            }
            // BLOCO2 = inventario: count2*u32 (itemId) ++ count2*u8 (slot index) PARALELOS (disasm FUN_0040bcb0 loop2).
            // Usa o INDICE i como slot do Box (posicoes distintas) — os itens no DB tem slot=1 (a compra usava
            // invSlot=1) e sobreporiam no mesmo lugar; o indice espalha cada item numa celula propria.
            byte count2 = (byte)System.Math.Min(items.Count, 0x77);
            using var blk2 = new PacketWriter();
            for (int i = 0; i < count2; i++) blk2.WriteUInt32((uint)items[i].ItemId);
            for (int i = 0; i < count2; i++) blk2.WriteByte((byte)i);
            byte[] block2 = blk2.ToArray();
            byte count1 = 0; byte[] block1 = System.Array.Empty<byte>(); // loadout equipado: vazio (sem char-select)

            // RESPOSTA msgType 0x15 via SendLobby (msgType-first; SendMessage poria serverSeq 1o -> cliente erraria o dispatch).
            byte[] payload = BuildGroupMemberInfo(u, slotArg, groupId, field1, field2, field3, b1, count1, count2, block1, block2);
            using var fw = new PacketWriter();
            fw.WriteWord(0x15);  // msgType (1o u16 = dispatch do cliente)
            fw.WriteWord(0);     // aux (posicao do serverSeq; 0)
            fw.WriteBytes(payload);
            u.SendLobby(fw.ToArray());
            Log.Ok("shop", "[{0}] 0x2f inventario (0x15) enviado: {1} itens -> Box", u.Slot, count2);
        }

        // Monta o payload (apos seq+subtype) do 0x15 na ordem real dos offsets a partir de &local_1010.
        private static byte[] BuildGroupMemberInfo(ClientSession u, byte slotArg, ushort groupId,
                                                   uint field1, uint field2, uint field3,
                                                   byte b1, byte count1, byte count2, byte[] block1, byte[] block2)
        {
            using var w = new PacketWriter();
            w.WriteUInt32((uint)u.FieldId);   // local_100c (off4)  = *(0x1460) fieldId
            w.WriteUInt32((uint)0);           // local_1008 (off8)  = *(0x14a4) handle field-secondary — TODO valor real
            w.WriteWord(groupId);             // local_1004 (off0xc)
            w.WriteUInt32(field1);            // local_1002 (off0xe)
            w.WriteByte(slotArg);             // local_ffe  (off0x12) = (byte)local_12e8 (slotArg real)
            w.WriteUInt32(field2);            // local_ffd  (off0x13)
            w.WriteByte(count1);              // local_ff9  (off0x17)
            w.WriteByte(b1);                  // local_ff8  (off0x18)
            w.WriteUInt32(field3);            // local_ff7  (off0x19) -> base termina em 0x1d
            if (count1 != 0) w.WriteBytes(block1); // count1*4 (local_12d0) + count1 (local_109c)
            w.WriteByte(count2);              // local_12e9 (sempre escrito)
            if (count2 != 0) w.WriteBytes(block2); // count2*4 (local_1280) + count2 (local_1088)
            return w.ToArray();
        }

        private static void Op_GroupListQuery(HandlerContext ctx)
        {
            var u = ctx.User;
            // payload: [byte mode][byte hasExtra]( [u16 extra] se hasExtra!=0 )
            byte mode = ctx.P.Byte();      // cVar2 = *param_3
            byte hasExtra = ctx.P.Byte();  // cVar1 = param_3[1]
            ushort extra = 0;
            if (hasExtra != 0) extra = ctx.P.UInt16(); // *(ushort*)(param_3+2)

            // Guard: mode deve ser 0 ou 1 (else DISC 0x45) — sem checagem de field aqui
            if (mode != 0 && mode != 1) { u.Disconnect(0x45); return; }

            // TODO FUN_0040b2c0(user, mode, hasExtra, extra, out a(u32), out b(u32), out c(u32)) -> byte err
            byte err = 0;   // sucesso ((char)uVar4)
            uint a = 0;     // local_1010 -> local_ff8
            uint b = 0;     // local_1018 -> local_ff4
            uint c = 0;     // local_1014 -> local_ff0

            if (err != 0)
            {
                // Falha: SendLobby subtype 0x34 + [byte err] (len 3)
                using var wf = new PacketWriter();
                wf.WriteWord(0x34);   // local_1004 (subtype lobby)
                wf.WriteByte(err);    // local_1002 low = (char)uVar4
                u.SendLobby(wf.ToArray());
                return;
            }

            // Sucesso: SendMessage subtype 0x17 (local_1002 = 0x17) via FUN_0041b940.
            // local_1004 (off0) = *(0x1488) e o SEQ que SendMessage ja prepende -> NAO entra no payload.
            // Payload comeca em fieldId (offset 4).
            using var w = new PacketWriter();
            w.WriteUInt32((uint)u.FieldId);    // local_1000 (off4) = *(0x1460) fieldId
            w.WriteByte(mode);                 // local_ffc  (off8) = (byte)local_100c
            w.WriteWord((ushort)0);            // local_ffb  (off9) = *(0x2370) — TODO campo interno
            w.WriteByte(hasExtra);             // local_ff9  (off0xb) = cVar1
            if (hasExtra == 1)
            {
                w.WriteUInt32(a);              // local_ff8  (off0xc)
                w.WriteUInt32(b);              // local_ff4  (off0x10)
                w.WriteUInt32(c);              // local_ff0  (off0x14)
                w.WriteWord(extra);            // local_fec  (off0x18)
            }
            u.SendMessage(0x17, w.ToArray());
        }

        private static void Op_FieldPlayerList(HandlerContext ctx)
        {
            var u = ctx.User;
            var world = ctx.World;
            // Guards na ordem do binario
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x46); return; }
            if (u.Status != 0x02) { u.Disconnect(0x47); return; }

            // payload: [byte max][u16 startIdx][byte b11][byte b3][byte b2][byte b4][byte b5][byte b6][byte b7]
            byte maxCount = ctx.P.Byte();          // bVar1 = *param_3
            if (maxCount > 10) { u.Disconnect(0x48); return; }
            ushort startIdx = ctx.P.UInt16();      // uVar14 = *(param_3+1)
            if (startIdx >= world.Fields.Count /* DAT_00455824 (num fields) */) { u.Disconnect(0x49); return; }
            byte b11 = ctx.P.Byte();  // param_3[3]
            byte b3  = ctx.P.Byte();  // param_3[4]
            byte b2  = ctx.P.Byte();  // param_3[5]
            byte b4  = ctx.P.Byte();  // param_3[6]
            byte b5  = ctx.P.Byte();  // param_3[7]
            byte b6  = ctx.P.Byte();  // param_3[8]
            byte b7  = ctx.P.Byte();  // param_3[9]

            // Varredura dos fields a partir de startIdx, ate maxCount entradas, serializando cada uma
            // (FUN_0041b830/FUN_00405920/FUN_00405790). Cada entrada = [u16 fieldIdx][Field.SerializeListEntry()].
            // (filtros de FUN_0040b6c0 nao aplicados — lista todos; refinar com b2..b7 quando necessario.)
            byte matched = 0;          // local_102d
            byte[] entries;
            using (var le = new PacketWriter())
            {
                var fields = world.Fields;
                for (int i = startIdx; i < fields.Count && matched < maxCount; i++)
                {
                    var fld = fields[i];
                    le.WriteWord(fld.Id);                 // u16 fieldIdx
                    le.WriteBytes(fld.SerializeListEntry());
                    matched++;
                }
                entries = le.ToArray();
            }

            // Resposta sempre via SendLobby subtype 0x36:
            //   local_1004._0_2_ = 0x36 (subtype, off0)
            //   local_1004._2_1_ = matched (byte, off2) — contador
            //   off3+ : entries (cada: u16 fieldIdx + payload do field via FUN_00405790)
            using var w = new PacketWriter();
            w.WriteWord(0x36);        // subtype
            w.WriteByte(matched);     // contador de entradas
            if (matched != 0) w.WriteBytes(entries);
            u.SendLobby(w.ToArray());
        }

        private static void Op_FieldLeaveGame(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guards na ordem do binario
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x50); return; }
            if (u.Status != 0x03) { u.Disconnect(0x51); return; } // status '\x03' = field/in-game

            // Sem parse de payload e sem struct de resposta.
            // FUN_0040b7d0(user, out fieldIdx(u16), out slot(byte)) — le posicao do user no field.
            ushort fieldIdx = 0;   // param_1 (reaproveitado pela FUN_0040b7d0)
            byte slot = 0;         // local_4 (byte)
            // TODO FUN_0040b7d0(user, out fieldIdx, out slot);
            // TODO FUN_004091e0(field[fieldIdx], slot) — remove/marca saida do jogador no field.
            // TODO FUN_0041b8b0(this, originalSlot) — notifica/atualiza estado pos-saida.
            // remove o jogador do field atual (FUN_004091e0); libera o field se vazio
            _ = fieldIdx; _ = slot;
            ctx.World.LeaveField(u);
        }

        private static void Op_RoomCreate(HandlerContext ctx)
        {
            var u = ctx.User;
            var world = ctx.World;
            // Guards na ordem do binario
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x52); return; }
            if (u.Status != 0x02) { u.Disconnect(0x53); return; }

            // payload (strings nul-terminadas + campos):
            //   roomName (CString, len < 0x29)   -> local_20fc
            //   password (CString, len < 9)      -> local_2108
            //   description (CString, len < 0xc9)-> local_20d0
            //   byte mapId   (bVar1  -> uStack_2124)
            //   byte mode    (bVar2  -> uStack_210c)
            //   byte b3      (param_3[+2] -> uStack_2120)
            //   u16  capacity(uVar10 -> uStack_2128)
            //   byte minLevel(bVar3)
            //   byte b4      (bVar4  -> local_212c)
            //   byte maxLevel(bVar5  -> uStack_2114)
            //   byte b8      (param_3[+8] -> uStack_2118)
            string roomName = ctx.P.CString();
            if (roomName.Length >= 0x29) { u.Disconnect(0x54); return; }
            string password = ctx.P.CString();
            if (password.Length >= 9) { u.Disconnect(0x55); return; }
            string description = ctx.P.CString();
            if (description.Length >= 0xc9) { u.Disconnect(0x56); return; }
            byte mapId   = ctx.P.Byte();   // bVar1
            byte mode    = ctx.P.Byte();   // bVar2
            byte b3      = ctx.P.Byte();   // param_3[uVar8+2]
            ushort capacity = ctx.P.UInt16(); // uVar10
            byte minLevel = ctx.P.Byte();  // bVar3
            byte b4      = ctx.P.Byte();   // bVar4
            byte maxLevel= ctx.P.Byte();   // bVar5
            byte b8      = ctx.P.Byte();   // param_3[uVar8+8]

            if (mode == 0)
            {
                // mode==0: criacao normal de sala (eco de confirmacao)
                if (mapId >= 100) { u.Disconnect(0x57); return; }
                // TODO *(this+0xe8)[mapId*3] != 0  -> mapa habilitado?
                bool mapEnabled = true; // modelado como habilitado
                if (!mapEnabled) { u.Disconnect(0x58); return; }

                // Sucesso: SendMessage subtype 0x25 (uStack_2004._2_2_ = 0x25) via FUN_0041b940.
                // uStack_2004._0_2_ (off0) = *(0x1488) e o SEQ (prepended por SendMessage) -> NAO entra no payload.
                // cria o field no dominio (espelha alloc de this+0xe4) e vincula o usuario
                var newField = world.CreateField(roomName, mapId, mode, capacity, u);
                u.FieldId = newField.Id;
                // Payload comeca em fieldId (off4):
                using var w = new PacketWriter();
                w.WriteUInt32((uint)u.FieldId);    // uStack_2000 (off4) = *(0x1460) fieldId
                w.WriteCString(roomName);          // aCStack_1ffc
                w.WriteCString(password);
                w.WriteCString(description);
                w.WriteByte(mapId);                // uStack_2124 low
                w.WriteByte(b3);                   // uStack_2120 low (= param_3[+2], NAO mode)
                w.WriteWord(capacity);             // uStack_2128 low (u16)
                w.WriteByte(minLevel);             // bVar3
                w.WriteByte(b4);                   // local_212c low
                w.WriteByte(maxLevel);             // uStack_2114 low
                w.WriteByte(b8);                   // uStack_2118 low
                u.SendMessage(0x25, w.ToArray());
                return;
            }

            // mode != 0: criacao de sala competitiva/ranqueada
            if (mode > 4) { u.Disconnect(0x5b); return; }
            if (b3 >= 0x16) { u.Disconnect(0xca); return; }
            if (capacity < 0x122 || capacity > 0x4ba) { u.Disconnect(0xcb); return; }
            if (mode == 2)
            {
                if (minLevel < 0xd || minLevel > 0x1e) { u.Disconnect(0xcc); return; }
            }
            else if (mode == 3)
            {
                // Decompile: erro 0xcc quando minLevel NAO esta em (0x13,0x33), i.e. <=0x13 ou >=0x33.
                if (!(minLevel > 0x13 && minLevel < 0x33)) { u.Disconnect(0xcc); return; }
            }
            // (mode 1 e 4 caem direto na validacao comum abaixo)

            // Validacao comum (LAB_004237ce)
            if (b4 == 0 || maxLevel > 99) { u.Disconnect(0x59); return; }
            // user+0x1531 = nivel/rank do criador; precisa b4 <= rank <= maxLevel
            byte creatorRank = 0; // TODO *(user+0x1531)
            if (!(b4 <= creatorRank && creatorRank <= maxLevel)) { u.Disconnect(0x5a); return; }

            // Aloca a sala competitiva no dominio (espelha a varredura/alloc de this+0xe4)
            // e vincula o usuario como master. notify 0x13 quando substatus==1 (FUN_0040b7b0).
            byte notify = (u.SubStatus == 0x01) ? (byte)0x13 : (byte)0x00;
            _ = notify;
            var compField = world.CreateField(roomName, mapId, mode, capacity, u);
            u.FieldId = compField.Id;
            byte resultFlag = 0;                       // 0 = sucesso
            ushort assignedIdx = (ushort)compField.Id; // uStack_1001

            // Resposta: SendLobby subtype 0x3b (uStack_1004 = 0x3b), len 5
            //   off0 u16 subtype, off2 byte resultFlag (uStack_1002), off3 u16 assignedIdx (uStack_1001)
            using var wr = new PacketWriter();
            wr.WriteWord(0x3b);          // subtype
            wr.WriteByte(resultFlag);    // uStack_1002
            wr.WriteWord(assignedIdx);   // uStack_1001
            u.SendLobby(wr.ToArray());
        }

        private static void Op_FieldGameAction(HandlerContext ctx)
                {
                    // FUN_00423ad0 (opcode 0x3d). user = this+0xd4 + slot*0x23b4.
                    var u = ctx.User;

                    // Guard: (*(user+0x1460) != 0) && (*(user+0x14a4) != 0) -> InField && FieldSecondary.
                    // Senao => FUN_0041eb20(this, slot, 0x60, 1, 1) -> Disconnect(0x60).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x60); return; }
                    // Guard: *(user+0x1440) == '\x03' (Status). Senao => Disconnect(0x61).
                    if (u.Status != 0x03) { u.Disconnect(0x61); return; }

                    // FUN_0040b7d0(user, &fieldIndex, &fieldSlot):
                    //   fieldIndex(u16) = *(user+0x14a0); fieldSlot(byte) = *(user+0x14a2).
                    int fieldIndex = u.FieldId;       // *(user+0x14a0)
                    byte fieldSlot = (byte)u.Slot;    // *(user+0x14a2)

                    // Payload: 1 byte de acao (cVar1 = *param_3).
                    byte action = ctx.P.Byte();

                    // FUN_00407520(field = this+0xe4 + fieldIndex*0x3c0, fieldSlot, action):
                    // aplica a acao do jogador no field. Corpo nao presente no decompile -> efeito
                    // modelado: broadcast da acao aos demais jogadores do field.
                    var field = ctx.World.GetField(fieldIndex);
                    if (field == null)
                    {
                        Log.Warn("field", "FieldGameAction: field {0} inexistente (slot {1})", fieldIndex, u.Slot);
                        return;
                    }

                    Log.Info("field", "FieldGameAction: field {0} '{1}' slot {2} action=0x{3:x2}", field.Id, field.Name, fieldSlot, action);

                    using var w = new PacketWriter();
                    w.WriteByte(fieldSlot);   // quem executou
                    w.WriteByte(action);      // acao
                    field.Broadcast(WorldHandlers.Prefix(0x3d, w.ToArray()), u);
                }


        private static void Op_FieldGameReady(HandlerContext ctx)
                {
                    // FUN_00423b70 (opcode 0x3e). user = this+0xd4 + slot*0x23b4.
                    var u = ctx.User;

                    // Guard: InField && FieldSecondary. Senao => Disconnect(0x62).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x62); return; }
                    // Guard: Status == '\x03'. Senao => Disconnect(99 = 0x63).
                    if (u.Status != 0x03) { u.Disconnect(0x63); return; }

                    // FUN_0040b7d0(user, &fieldIndex, &fieldSlot)
                    int fieldIndex = u.FieldId;       // *(user+0x14a0)
                    byte fieldSlot = (byte)u.Slot;    // *(user+0x14a2)

                    // Sem payload neste opcode.

                    // FUN_004075a0(field = this+0xe4 + fieldIndex*0x3c0, fieldSlot, 0):
                    // logica de troca de time/slot (FUN_004075a0 @ 0x4075a0). Estrutura por-slot do field
                    // (field+0x124+i*0x14: u16 userSlot; +0x126 occupied; +0x127 locked) e contadores de
                    // time (+0x116/+0x117) nao estao modelados no dominio atual -> efeito modelado:
                    // broadcast de "ready" (subtype 0x3e, payload local_1004=0x3e) aos jogadores do field.
                    var field = ctx.World.GetField(fieldIndex);
                    if (field == null)
                    {
                        Log.Warn("field", "FieldGameReady: field {0} inexistente (slot {1})", fieldIndex, u.Slot);
                        return;
                    }

                    Log.Info("field", "FieldGameReady: field {0} '{1}' slot {2} pronto", field.Id, field.Name, fieldSlot);

                    using var w = new PacketWriter();
                    w.WriteByte(fieldSlot);   // local_1001 = param_1 (slot de origem)
                    field.Broadcast(WorldHandlers.Prefix(0x3e, w.ToArray()), u);
                }


        private static void Op_FieldGameStart_3f(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: InField e FieldSecondary
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x64); return; } // 100 = 0x64
            // Guard: Status = 0x03 (field)
            if (u.Status != 0x03) { u.Disconnect(0x65); return; }

            // FUN_0040b7d0(user, &fieldIndex, &fieldSlot)
            int fieldIndex = u.FieldId;
            byte fieldSlot = (byte)u.Slot;
            _ = fieldSlot;

            // Guard adicional: SubStatus do usuario precisa ser '4' (0x34) -> caso contrario DISC 0x66.
            // No binario: *(user+0x146c) != '4'. SubStatus mapeia +0x146c.
            if (u.SubStatus != 0x34) { u.Disconnect(0x66); return; }

            // FUN_00405740(field): inicia o jogo no field (state em jogo) e avisa os jogadores.
            var field = ctx.World.GetField(fieldIndex);
            if (field != null)
            {
                field.InGame = true;
                field.State = 2;                 // field+8 = 2 (em jogo)
                using var w = new PacketWriter();
                w.WriteByte((byte)u.Slot);       // quem iniciou
                field.Broadcast(Prefix(0x3f, w.ToArray()));
                Log.Info("field", "field {0} '{1}' iniciou a partida", field.Id, field.Name);
            }
        }

        private static void Op_FieldSetGameMode(HandlerContext ctx)
                {
                    // FUN_00423cc0 (opcode 0x40). user = this+0xd4 + slot*0x23b4; iVar4 = slot*0x23b4.
                    var u = ctx.User;

                    // Guard: InField && FieldSecondary. Senao => Disconnect(0x67).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x67); return; }
                    // Guard: Status == '\x03'. Senao => Disconnect(0x68).
                    if (u.Status != 0x03) { u.Disconnect(0x68); return; }

                    // FUN_0040b7d0(user, &fieldIndex(local_8), &fieldSlot(param_1 low byte)):
                    //   fieldIndex(u16) = *(user+0x14a0); fieldSlot(byte) = *(user+0x14a2).
                    int fieldIndex = u.FieldId;       // local_8[0]
                    byte fieldSlot = (byte)u.Slot;    // (char)param_1

                    var field = ctx.World.GetField(fieldIndex);
                    if (field == null)
                    {
                        Log.Warn("field", "FieldSetGameMode: field {0} inexistente (slot {1})", fieldIndex, u.Slot);
                        return;
                    }

                    // cVar1 = *(this+0xd4 + 0x146c + slot*0x23b4) = SubStatus do usuario.
                    // Se (sub != '4' && sub != '\x01'): exige fieldSlot == *(field+0x121) (master slot),
                    // senao => Disconnect(0x69).
                    byte sub = u.SubStatus;
                    if (sub != 0x34 && sub != 0x01)
                    {
                        byte fieldMasterSlot = (byte)field.MasterSlot; // *(field+0x121)
                        if (fieldSlot != fieldMasterSlot) { u.Disconnect(0x69); return; }
                    }

                    // Payload: 1 byte (bVar2 = *param_3) = novo modo/valor.
                    byte mode = ctx.P.Byte();

                    // FUN_004097c0(field = this+0xe4 + fieldIndex*0x3c0, mode): aplica o modo no field.
                    // Corpo nao presente no decompile -> efeito modelado: grava Field.Mode e propaga.
                    field.Mode = mode;
                    Log.Info("field", "FieldSetGameMode: field {0} '{1}' slot {2} mode=0x{3:x2}", field.Id, field.Name, fieldSlot, mode);

                    using var w = new PacketWriter();
                    w.WriteByte(fieldSlot);   // quem alterou
                    w.WriteByte(mode);        // novo modo
                    field.Broadcast(WorldHandlers.Prefix(0x40, w.ToArray()));
                }


        private static void Op_FieldCreateRoomEntry(HandlerContext ctx)
        {
            var u = ctx.User;
            // local_4 = DAT_00454928 -> guarda de stack/SEH (FUN_00435e83). Sem efeito de protocolo.

            // Guard: InField e FieldSecondary
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x6a); return; }
            // Guard: Status = 0x03 (field)
            if (u.Status != 0x03) { u.Disconnect(0x6b); return; }

            // FUN_0040b7d0(user, &fieldIndex, &fieldFlag): fieldIndex(u16) + um byte (local_11d)
            int fieldIndex = u.FieldId;
            byte fieldFlag = (byte)u.Slot; // local_11d resolvido por FUN_0040b7d0 (byte do usuario)

            var field = ctx.World.GetField(fieldIndex);

            // field+8 == '\x02' -> field ocupado/em-jogo -> DISC 0x6c.
            // TODO field+8 = estado do field; modelado como field.State.
            if (field.State == 0x02) { u.Disconnect(0x6c); return; }
            // field+0x119 == 0 -> slot/sala nao habilitada -> DISC 0x6d.
            if (field.SlotEnabled == 0) { u.Disconnect(0x6d); return; }
            // local_11d != field+0x121 (master slot) -> DISC 0x6e.
            if (fieldFlag != (byte)field.MasterSlot) { u.Disconnect(0x6e); return; }

            // ---- Parse do payload (param_3): 3 strings nul-terminated + bytes/word ----
            // String 1: roomName, len < 0x29 (40), buffer 44; senao DISC 0x6f.
            string roomName = ctx.P.CString();
            if (roomName.Length >= 0x29) { u.Disconnect(0x6f); return; }

            // String 2: password, len < 9; senao DISC 0x70. buffer 12.
            string password = ctx.P.CString();
            if (password.Length >= 9) { u.Disconnect(0x70); return; }

            // String 3: descricao/extra, len < 0xc9 (201); senao DISC 0x71. buffer 204.
            string desc = ctx.P.CString();
            if (desc.Length >= 0xc9) { u.Disconnect(0x71); return; }

            // Campos apos as 3 strings (na ordem do binario):
            byte field0 = ctx.P.Byte();        // param_3[pCVar1+0]  -> arg 'flags' passado a FUN_004077c0
            byte maxOrType = ctx.P.Byte();     // bVar2 = param_3[pCVar1+1] (limite < 0x16)
            ushort mapId = ctx.P.UInt16();     // uVar7 = param_3[pCVar1+2..3]
            byte level = ctx.P.Byte();         // bVar3 = param_3[pCVar1+4]
            byte mode  = ctx.P.Byte();         // bVar4 = param_3[pCVar1+5]

            // Validacao (mesma ordem/codigos do binario). uVar7 = codigo de erro default.
            ushort err;
            if (maxOrType < 0x16)
            {
                // bVar2 < 0x16
                if (mapId < 0x122 || mapId > 0x4ba)
                {
                    err = 0xce;
                }
                else if (mode == 2)
                {
                    // 0x0c < level < 0x1f -> OK
                    if (level > 0x0c && level < 0x1f) { ApplyCreate(); return; }
                    err = 0xcf;
                }
                else if (mode == 3)
                {
                    // 0x13 < level < 0x33 -> OK
                    if (level > 0x13 && level < 0x33) { ApplyCreate(); return; }
                    err = 0xcf;
                }
                else
                {
                    // mode != 0 && mode < 5 -> OK; senao 0x72
                    if (mode != 0 && mode < 5) { ApplyCreate(); return; }
                    err = 0x72;
                }
            }
            else
            {
                err = 0xcd; // bVar2 >= 0x16
            }

            u.Disconnect(err);
            return;

            // LAB_0042406b: caminho de sucesso.
            void ApplyCreate()
            {
                // TODO FUN_004077c0(field, roomName, password, desc, field0, maxOrType, mapId, level, mode):
                // cria/registra a entrada de sala/jogo no field. Sem corpo -> efeito modelado (assume sucesso).
                // FUN_004077c0(field, roomName, password, desc, field0, maxOrType, mapId, level, mode);
                _ = field0; _ = maxOrType; _ = mapId; _ = level; _ = mode;
                _ = roomName; _ = password; _ = desc;
            }
        }

        private static void Op_FieldUnitCommand(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00424100. userObj = this+0xd4 + slot*0x23b4.
                    // Guard 0x73: InField (0x1460) && FieldSecondary (0x14a4).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x73); return; }
                    // Guard 0x74: Status (0x1440) == '\x03' (em campo).
                    if (u.Status != 0x03) { u.Disconnect(0x74); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &ownerByte):
                    //   targetIndex = *(u16)(userObj+0x14a0);  ownerByte = *(byte)(userObj+0x14a2)
                    ushort targetIndex = u.FieldTargetIndex;  // local_8[0]
                    byte ownerByte = u.FieldTargetOwner;      // (byte)param_1 apos b7d0

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0; owner guard: ownerByte == *(byte)(fieldObj+0x121) (MasterSlot)
                    var field = ctx.World.GetField(targetIndex);
                    byte targetOwner = (byte)(field != null && field.MasterSlot >= 0 ? field.MasterSlot : 0);
                    if (ownerByte != targetOwner) { u.Disconnect(0x75); return; }

                    // Payload: [byte cmd]. Faixa valida: cmd<0x14 && cmd!=8 && cmd!=9 && cmd!=0x12 && cmd!=0x13.
                    if (!ctx.P.CanRead(1)) { u.Disconnect(0x76); return; }
                    byte cmd = ctx.P.Byte();
                    if (cmd < 0x14 && cmd != 8 && cmd != 9 && cmd != 0x12 && cmd != 0x13)
                    {
                        // Faixa valida: le tambem [byte value] e aplica FUN_00407910(fieldObj, cmd, value).
                        if (!ctx.P.CanRead(1)) { u.Disconnect(0x76); return; }
                        byte value = ctx.P.Byte();
                        Log.Debug("field", "[{0}] UnitCommand field={1} cmd={2} value={3}", u.Slot, targetIndex, cmd, value);

                        // FUN_00407910: aplica o comando de unidade no field-objeto e replica aos demais
                        // jogadores. Sem corpo no decompile disponivel -> efeito de rede modelado:
                        // relay do comando (opcode 0x42 [cmd][value]) ao field, exceto o emissor.
                        if (field != null)
                        {
                            using var w = new PacketWriter();
                            w.WriteByte(cmd);
                            w.WriteByte(value);
                            field.Broadcast(WorldHandlers.Prefix(0x42, w.ToArray()), u);
                        }
                        return;
                    }
                    // cmd fora da faixa permitida.
                    u.Disconnect(0x76);
                }


        private static void Op_FieldUnitStop(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00424210. userObj = this+0xd4 + slot*0x23b4.
                    // Guard 0x77: InField (0x1460) && FieldSecondary (0x14a4).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x77); return; }
                    // Guard 0x78: Status (0x1440) == '\x03'.
                    if (u.Status != 0x03) { u.Disconnect(0x78); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &ownerByte): targetIndex=*(u16)(userObj+0x14a0), ownerByte=*(byte)(userObj+0x14a2)
                    ushort targetIndex = u.FieldTargetIndex;  // local_4 (u16)
                    byte ownerByte = u.FieldTargetOwner;      // (byte)param_1

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0; owner guard 0x79 contra *(byte)(fieldObj+0x121)
                    var field = ctx.World.GetField(targetIndex);
                    byte targetOwner = (byte)(field != null && field.MasterSlot >= 0 ? field.MasterSlot : 0);
                    if (ownerByte != targetOwner) { u.Disconnect(0x79); return; }

                    // FUN_004079d0(fieldObj): comando "stop" da unidade. Sem payload e sem resposta direta;
                    // helper indisponivel no decompile -> efeito de rede modelado: relay do opcode 0x43 (stop)
                    // aos demais jogadores do field para que repliquem a parada da unidade.
                    Log.Debug("field", "[{0}] UnitStop field={1}", u.Slot, targetIndex);
                    if (field != null)
                    {
                        field.Broadcast(WorldHandlers.Prefix(0x43, System.Array.Empty<byte>()), u);
                    }
                }


        private static void Op_FieldUnitByteAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_004242c0. userObj = this+0xd4 + slot*0x23b4.
                    // No decompile a ordem dos guards e: primeiro InField/FieldSecondary (DISC 0x7a),
                    // depois Status==3 (DISC 0x7b).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x7a); return; }
                    if (u.Status != 0x03) { u.Disconnect(0x7b); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &actionByte): targetIndex=*(u16)(userObj+0x14a0),
                    //   actionByte=*(byte)(userObj+0x14a2). NAO ha checagem de owner (+0x121) aqui.
                    ushort targetIndex = u.FieldTargetIndex; // param_1 (u16)
                    byte actionByte = u.FieldTargetOwner;    // (byte)local_4

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0.
                    // FUN_00407c70(fieldObj, actionByte): aplica a acao de byte. Sem resposta no fluxo;
                    // helper indisponivel -> efeito de rede modelado: relay do opcode 0x45 [actionByte] ao field.
                    Log.Debug("field", "[{0}] UnitByteAction field={1} action={2}", u.Slot, targetIndex, actionByte);
                    var field = ctx.World.GetField(targetIndex);
                    if (field != null)
                    {
                        using var w = new PacketWriter();
                        w.WriteByte(actionByte);
                        field.Broadcast(WorldHandlers.Prefix(0x45, w.ToArray()), u);
                    }
                }


        private static void Op_FieldUnitPickup(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00424350. userObj = this+0xd4 + slot*0x23b4 (iVar3 = slot*0x23b4).
                    // Guard 0x7c: InField (0x1460) && FieldSecondary (0x14a4).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x7c); return; }
                    // Guard 0x7d: Status (0x1440) == '\x03'.
                    if (u.Status != 0x03) { u.Disconnect(0x7d); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &slotByte): targetIndex=*(u16)(userObj+0x14a0), slotByte=*(byte)(userObj+0x14a2)
                    ushort targetIndex = u.FieldTargetIndex; // local_2010[0]
                    byte slotByte = u.FieldTargetOwner;      // local_200c[0]

                    // Payload: [byte action]; so processa o pickup quando action < 2.
                    if (!ctx.P.CanRead(1)) { return; }
                    byte action = ctx.P.Byte();

                    var field = ctx.World.GetField(targetIndex);
                    if (action < 2)
                    {
                        // FUN_0041b860(fieldObj, slotByte): valida se o slot pode ser coletado (retorna !=0 = ok).
                        // Helper indisponivel no decompile -> condicao modelada como sucesso quando o field existe.
                        bool ok = field != null;
                        if (ok)
                        {
                            // local_2008 = *(u32)(userObj + 0x14a4) (mesmo offset cru de FieldSecondary).
                            uint fieldSec = (uint)u.FieldSecondaryRaw;

                            // FUN_00405950(fieldObj, slotByte): getter que devolve um id/quantidade do item.
                            //   = max(0, *(byte)(fieldObj + slot*0x14 + 0x12d) - *(byte)(fieldObj + slot*0x14 + 0x12e))
                            // Tabela por-unidade do field nao modelada -> usa 0 (sem item disponivel) como base.
                            byte costArg = 0; // <= retorno de FUN_00405950 (usado como param de FUN_0040b900)

                            // FUN_0040b900(userObj, costArg): debita o cash do jogador e devolve o saldo restante.
                            //   cost = (*(byte)(userObj+0x1531) >> 1) + costArg*5; se cost < cash: cash-=cost; senao cash=0.
                            uint cost = (uint)(u.FieldCashCost >> 1) + (uint)costArg * 5u;
                            if (cost < u.FieldCash) u.FieldCash -= cost; else u.FieldCash = 0;
                            int resultValue = (int)u.FieldCash; // iVar2 (saldo final) usado em local_1ffc/local_1002

                            Log.Debug("field", "[{0}] UnitPickup field={1} slot={2} cost={3} saldo={4}", u.Slot, targetIndex, slotByte, cost, resultValue);

                            // Resposta 1 (FUN_0041b940, canal plano/SendMessage, subtype 0xc):
                            //   struct local_2004 = [u16 seq=*(userObj+0x1488)][u16 subtype=0xc][u32 fieldSec][int resultValue]
                            //   FUN_0041b940 prefixa seq+subtype; em C# SendMessage faz [serverSeq][subtype] e recebe o corpo apos o subtype.
                            using (var w = new PacketWriter())
                            {
                                w.WriteUInt32(fieldSec);    // local_2000
                                w.WriteInt32(resultValue);  // local_1ffc
                                u.SendMessage(0x0c, w.ToArray());
                            }

                            // Resposta 2 (FUN_004038e0(this+0x118, slot, 6, &local_1004), canal lobby/SendLobby, subtype 0x58):
                            //   struct local_1004 = [u16 subtype=0x58][int resultValue].
                            using (var w2 = new PacketWriter())
                            {
                                w2.WriteWord(0x58);          // local_1004 (subtype, primeiro campo do payload lobby)
                                w2.WriteInt32(resultValue);  // local_1002
                                u.SendLobby(w2.ToArray());
                            }
                        }
                    }
                    // FUN_00407e00(fieldObj, slotByte): SEMPRE executado (mesmo se action>=2 ou pickup falhou).
                    //   Marca o slot da unidade como removido (tabela field+slot*0x14+0x126 -> 1), recalcula
                    //   sobreviventes, reatribui o master (field+0x121) se necessario e dispara fim de rodada.
                    //   Helper de estado profundo indisponivel/parcial no decompile -> efeito de rede modelado:
                    //   notifica o field do "pickup/remocao" (opcode 0x46 [slotByte]) para sincronizar os clientes.
                    if (field != null)
                    {
                        using var w3 = new PacketWriter();
                        w3.WriteByte(slotByte);
                        field.Broadcast(WorldHandlers.Prefix(0x46, w3.ToArray()), u);
                    }
                }


        private static void Op_FieldUnitCharAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_004246e0. userObj = this+0xd4 + slot*0x23b4.
                    // Guard 0x83: InField (0x1460) && FieldSecondary (0x14a4).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x83); return; }
                    // Guard 0x84: Status (0x1440) == '\x03'.
                    if (u.Status != 0x03) { u.Disconnect(0x84); return; }

                    // FUN_0040b7d0(userObj, &targetIndex, &ownerByte): targetIndex=*(u16)(userObj+0x14a0), ownerByte=*(byte)(userObj+0x14a2)
                    ushort targetIndex = u.FieldTargetIndex; // local_4 (u16)
                    byte ownerByte = u.FieldTargetOwner;     // (byte)param_1

                    // fieldObj = (this+0xe4) + targetIndex*0x3c0; owner guard 0x85 contra *(byte)(fieldObj+0x121) (MasterSlot)
                    var field = ctx.World.GetField(targetIndex);
                    byte targetOwner = (byte)(field != null && field.MasterSlot >= 0 ? field.MasterSlot : 0);
                    if (ownerByte != targetOwner) { u.Disconnect(0x85); return; }

                    // Payload: [byte action].
                    if (!ctx.P.CanRead(1)) { return; }
                    byte action = ctx.P.Byte();

                    // FUN_00405a90(fieldObj, action): comando de personagem no field. Quando o estado do field permite
                    //   (field+8=='2' && field+0x119=='0') e action e 2 ou 3, ajusta contadores de time/lado
                    //   (field+0x2bf/0x2c0/0x2c1), arma um timer de 15s (field+0x2b8) e faz broadcast do opcode 0x4a
                    //   (struct [0x4a][bid][bdir/flag][cnt...]) aos jogadores ativos do field (slot status '\x04').
                    //   Estado interno por-unidade nao modelado -> efeito de rede modelado: relay do opcode 0x4a [action].
                    Log.Debug("field", "[{0}] UnitCharAction field={1} action={2}", u.Slot, targetIndex, action);
                    if (field != null)
                    {
                        using var w = new PacketWriter();
                        w.WriteByte(action);
                        field.Broadcast(WorldHandlers.Prefix(0x4a, w.ToArray()), u);
                    }
                }


        private static void Op_FieldChatBroadcast(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: deve estar no field (in-field + secondary)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x86); return; }
            // Guard: status precisa ser 0x03 (field)
            if (u.Status != 0x03) { u.Disconnect(0x87); return; }

            // TODO FUN_0040b7d0: resolve o slot do jogador dentro do field e um byte de flag.
            // Efeito minimo: assume o proprio slot do usuario e flag 0.
            ushort fieldSlot = u.Slot;
            byte fieldFlag = 0;

            // Payload: [u16 length][byte[length] data]
            ushort length = ctx.P.UInt16();
            if (length > 200) { u.Disconnect(0x88); return; }
            byte[] data = ctx.P.Bytes(length);

            // TODO FUN_00405c00(fieldObj=(this+0xe4 + fieldSlot*0x3c0), fieldFlag, length, data):
            // dispatch de chat/broadcast no nivel do field. A resposta e emitida internamente.
            var field = ctx.World.GetField(u.FieldId);
            if (field != null)
            {
                // TODO FUN_00405c00: broadcast do conteudo aos jogadores do field.
                using var w = new PacketWriter();
                w.WriteWord(length);
                w.WriteBytes(data);
                field.Broadcast(w.ToArray());
            }
            _ = fieldSlot; _ = fieldFlag;
        }

        private static void Op_FieldTaggedBroadcast(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x89); return; }
            if (u.Status != 0x03) { u.Disconnect(0x8a); return; }

            // TODO FUN_0040b7d0: resolve fieldSlot + flag. Efeito minimo: proprio slot, flag 0.
            ushort fieldSlot = u.Slot;
            byte fieldFlag = 0;

            // Payload: [byte tag][u16 length][byte[length] data]
            byte tag = ctx.P.Byte();
            if (tag > 0x13) { u.Disconnect(0x8b); return; }
            ushort length = ctx.P.UInt16();
            if (length > 200) { u.Disconnect(0x8c); return; }
            byte[] data = ctx.P.Bytes(length);

            // TODO FUN_00405cc0(fieldObj, fieldFlag, tag, length, data): dispatch tagueado no field.
            var field = ctx.World.GetField(u.FieldId);
            if (field != null)
            {
                using var w = new PacketWriter();
                w.WriteByte(tag);
                w.WriteWord(length);
                w.WriteBytes(data);
                field.Broadcast(w.ToArray());
            }
            _ = fieldSlot; _ = fieldFlag;
        }

        private static void Op_FieldPairUpdate(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00424980. this_00 = user[slot] (this+0xd4, stride 0x23b4).
                    // Guard 1: user+0x1460 (SlotActive/InField) e user+0x14a4 (SecondActive/FieldSecondary)
                    //          ambos != 0, senao DISC 0x8d.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x8d); return; }
                    // Guard 2: user+0x1440 (Status) == 3, senao DISC 0x8e.
                    if (u.Status != 0x03) { u.Disconnect(0x8e); return; }

                    // FUN_0040b7d0(user, out fieldId(u16=user+0x14a0), out slotInField(byte=user+0x14a2)).
                    int fieldId = u.FieldId;          // local_4 (u16)
                    byte slotInField = (byte)u.Slot;  // param_1 (byte) — slot do jogador dentro do field

                    var field = ctx.World.GetField(fieldId); // base = *(this+0xe4) + fieldId*0x3c0

                    // Payload (short* param_3): [s16 a][s16 b].
                    short a = ctx.P.Int16();  // *param_3
                    short b = ctx.P.Int16();  // param_3[1]

                    if (field == null) return;

                    // FUN_00405d70(playerRecord = (this+0xe4)+fieldId*0x3c0, a, b): atualiza o par (a,b)
                    // no registro do jogador dentro do field. So tem efeito quando o jogador esta vivo/jogando
                    // (record+8==2 && record+0x2b4==1). Quando a==0 ou (a!=0 && b==0) o estado muda para
                    // "derrotado/respawn" (record+0x2b4 vira 2, timer +15s) e dispara um broadcast subtype 0x4a
                    // para todos os membros vivos (record+0x124 stride 0x14, status byte +2 == 4) com:
                    //   [u16 0x4a][byte flag(=2)][byte dirA][byte dirB][byte respawnCount].
                    // Modelado: armazena o par no estado de field do jogador e, na transicao de derrota,
                    // repassa o broadcast 0x4a aos demais membros do field.
                    u.FieldPairA = a; // playerRecord+0x2c4
                    u.FieldPairB = b; // playerRecord+0x2c6

                    // Transicao de derrota: a==0  OU  (a!=0 && b==0).
                    bool defeated = (a == 0) || (b == 0);
                    if (defeated && u.FieldPlayState == 1)
                    {
                        if (a == 0)
                        {
                            u.FieldDirFlag = 0;          // playerRecord+0x2bf = 0
                            u.FieldRespawnCount++;        // playerRecord+0x2c1++
                        }
                        else // a!=0 && b==0
                        {
                            u.FieldDirCount++;            // playerRecord+0x2c0++
                            u.FieldDirFlag = 1;           // playerRecord+0x2bf = 1
                        }
                        u.FieldPlayState = 2;             // playerRecord+0x2b4 = 2 (e +0x2bd = 2)

                        using var w = new PacketWriter();
                        w.WriteByte(2);                   // playerRecord+0x2bd (estado)
                        w.WriteByte(u.FieldDirFlag);      // playerRecord+0x2bf
                        w.WriteByte(u.FieldDirCount);     // playerRecord+0x2c0
                        w.WriteByte(u.FieldRespawnCount); // playerRecord+0x2c1
                        field.Broadcast(WorldHandlers.Prefix(0x4a, w.ToArray()), u);
                    }
                }


        private static void Op_FieldSlotAction(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x8f); return; }
            if (u.Status != 0x03) { u.Disconnect(0x90); return; }

            // TODO FUN_0040b7d0: resolve fieldSlot + flag (local_4[0]). Efeito minimo: proprio slot, flag 0.
            ushort fieldSlot = u.Slot;
            byte fieldFlag = 0;

            // Payload: [byte a][byte b]
            byte a = ctx.P.Byte();
            if (a > 8) { u.Disconnect(0x91); return; }
            byte b = ctx.P.Byte();
            if (b > 0x13) { u.Disconnect(0x92); return; }

            // Efeito colateral do exe: se o estado do field-slot (offset 0x119) for 0x02 ou 0x03,
            // zera dois campos do user object: user+0x2395 (u32) e user+0x2399 (u16).
            // TODO: ler estado real do field-slot; modelado de forma segura como no-op condicional.
            byte fieldSlotState = 0; // TODO FUN_0040b7d0/field: (this+0xe4 + fieldSlot*0x3c0 + 0x119)
            if (fieldSlotState == 0x02 || fieldSlotState == 0x03)
            {
                // TODO: u.FieldScore = 0; (user+0x2395 u32) ; u.FieldScoreAux = 0; (user+0x2399 u16)
            }

            // TODO FUN_004087d0(fieldObj, fieldFlag, a, b): executa a acao de slot no field.
            _ = fieldSlot; _ = fieldFlag; _ = a; _ = b;
        }

        private static void Op_GamePointSettle(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00424b60. Guard: this_00+0x1460 (InField) e this_00+0x14a4 (FieldSecondary) != 0 -> senao DISC 0x93.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x93); return; }
                    // this_00+0x1440 (Status) deve ser '\x03' -> senao DISC 0x94.
                    if (u.Status != 0x03) { u.Disconnect(0x94); return; }

                    // FUN_0040b7d0(this_00, &local_20d4(u16), local_20c4(byte)): le user+0x14a0 -> indice do field-object
                    // deste user, e user+0x14a2 -> byte de seat/item-slot. local_20c4[0] e o seat usado nos broadcasts.
                    ushort fieldObjIdx = u.FieldObjectIndex;   // local_20d4
                    byte seat = u.FieldSeat;                   // local_20c4[0]

                    // iVar1 = field-object[fieldObjIdx]; guard *(iVar1+0x119) != 0 -> senao DISC 0x95.
                    var field = ctx.World.GetField(u.FieldId);
                    if (field == null || field.FieldFlag119 == 0) { u.Disconnect(0x95); return; }

                    // So procede se *(iVar1+8)=='\x02' (StateB) E *(iVar1+0x2b4)=='\x02' (StateMode). Caso contrario
                    // o exe nao envia nada e apenas retorna (cleanup).
                    if (!(field.StateB == 0x02 && field.StateMode == 0x02)) { return; }

                    // Payload (uint* param_3): +0 exp, +4 gold(local_20c8), +8 material(byte local_20d0),
                    // +9 u32 (local_209c), +0xd u32 (local_2098), +0x11 u32 (local_2094), +0x15 s16 flag.
                    uint exp = ctx.P.UInt32();      // uVar4 / local_20bc
                    uint gold = ctx.P.UInt32();     // local_20c8
                    byte material = ctx.P.Byte();   // *(param_3+2) -> (char)local_20d0
                    uint dropA = ctx.P.UInt32();    // local_209c (param_3+9)
                    uint dropB = ctx.P.UInt32();    // local_2098 (param_3+0xd)
                    uint dropC = ctx.P.UInt32();    // local_2094 (param_3+0x11)
                    short tailFlag = ctx.P.Int16(); // *(param_3+0x15)

                    // Bonus: se *(user+0x236c) != 0 entao exp = exp*3/2 e local_20c0|=1 (flag de bonus).
                    byte bonusFlag = 0;             // (char)local_20c0
                    if (u.ExpBonusActive) { exp = exp * 3u >> 1; bonusFlag = 1; }

                    // FUN_0041d0d0(this, slot, fieldObjIdx, bonusFlag, exp, gold, material): preview/log da recompensa.
                    Log.Info($"[GamePointSettle] slot={u.Slot} obj={fieldObjIdx} seat={seat} exp={exp} gold={gold} mat={material} bonus={bonusFlag}");

                    // FUN_0041cf80(this, &slot, &fieldObjIdx, &bonusFlag, &exp, &gold): anti-cheat de game-point.
                    // Retorno 1 = invalido. Sem corpo neste slice; modelado como 0 (ok).
                    int gpCheck = 0;
                    if (gpCheck == 1)
                    {
                        // FUN_0041d380(this, "Wrong Game Point! Exp : %u, Gold : %u", group) + DISC 0x97.
                        Log.Warn($"[GamePointSettle] slot={u.Slot} WRONG GAME POINT exp={exp} gold={gold}");
                        u.Disconnect(0x97);
                        return;
                    }

                    // Caminho valido:
                    // FUN_0041d290(this, slot, fieldObjIdx, seat, exp, gold, material): aplica recompensa ao alvo.
                    // FUN_004061c0(fieldObject, seat, exp): acumula exp no slot (field+seat*0x14+0x130 += exp).
                    // Sem corpo neste slice; efeitos modelados via logica de level-up/drop abaixo.

                    // FUN_0040d300(userObj, exp, &gold, &newLevel, &levelAux, &levelExtra): aplica exp/gold e calcula
                    // level-up. Retorno (uVar2) != 0 indica que houve mudanca de nivel.
                    byte newLevel = 0;       // local_20d9
                    uint levelAux = 0;       // local_20ac
                    ushort levelExtra = 0;   // local_20cc[0]
                    uint levelUp = 0;        // uVar2 (retorno)
                    if (levelUp != 0)
                    {
                        // FUN_004038e0(handler, slot, 5, &local_1004): lobby subtype 0x51 [u8 newLevel][u16 levelExtra].
                        using var lw = new PacketWriter();
                        lw.WriteWord(0x51);        // local_1004
                        lw.WriteByte(newLevel);    // local_1002 (local_20d9)
                        lw.WriteWord(levelExtra);  // local_1001 (local_20cc[0])
                        u.SendLobby(lw.ToArray());
                    }

                    // FUN_0040b940(userObj, &dropStruct(local_209c..), &local_20b8, local_20d8, &local_2090): processa
                    // itens/drops. Retorno (uVar4) != 0 (ou levelUp != 0) dispara broadcast 0x52 no field.
                    uint itemResult = 0;     // uVar4
                    ushort itemC = 0;        // local_20d8._0_2_
                    byte itemD = 0;          // local_20d8[2]
                    uint itemA = 0;          // local_20b8
                    uint itemB = 0;          // local_20b4
                    uint dropLevel = 0;      // local_20b0
                    if ((itemResult | levelUp) != 0)
                    {
                        // FUN_004061f0(fieldObject, 7, &local_1004): broadcast no field (len 7):
                        // [u16 0x52][u8 seat][u8 newLevel][u16 itemC][u8 itemD].
                        using var iw = new PacketWriter();
                        iw.WriteWord(0x52);        // local_1004
                        iw.WriteByte(seat);        // local_1002 (local_20c4[0])
                        iw.WriteByte(newLevel);    // local_1001 byte0 (local_20d9)
                        iw.WriteWord(itemC);       // local_1001 bytes1..2 (local_20d8._0_2_)
                        iw.WriteByte(itemD);       // local_ffe (local_20d8[2])
                        field.Broadcast(iw.ToArray());
                    }

                    // Modo final: se tailFlag==0 -> mode=3; senao FUN_00405980(fieldObject, seat) -> mode.
                    byte mode;
                    if (tailFlag == 0) { mode = 3; }
                    else
                    {
                        // FUN_00405980: le um modo derivado do field-object para este seat. Sem corpo; modelado como 0.
                        mode = 0;
                    }

                    // FUN_0040bb60(userObj, mode, &valA, &valB, &valC): calcula 3 valores finais (stats consolidados).
                    uint valA = 0; // local_20a0
                    uint valB = 0; // local_20a4
                    uint valC = 0; // local_20a8

                    // Campos extras lidos do user object (preenchidos por FUN_0040b940 / FUN_0040bb60).
                    uint extraA = 0; // local_2090
                    uint extraB = 0; // local_208c
                    uint extraC = 0; // local_2088

                    // Cabecalho derivado do user object.
                    uint groupId = (uint)u.GroupId;                 // local_2000 (user+0x1460)
                    uint secondaryRaw = (uint)u.FieldSecondaryRaw;   // local_1ff8 (user+0x14a4, valor cru)

                    // Resposta principal: FUN_0041b940(this, slot, 0x40, &local_2004) -> SendMessage subtype 0x0a (10),
                    // 0x40 e o LEN total. SendMessage ja injeta seq(=user+0x1488)+subtype; payload comeca apos o subtype.
                    using var w = new PacketWriter();
                    w.WriteUInt32(groupId);    // local_2000 (user+0x1460)
                    w.WriteUInt32(gold);       // local_1ffc (local_20c8)
                    w.WriteUInt32(secondaryRaw); // local_1ff8 (user+0x14a4)
                    w.WriteByte(newLevel);     // local_1ff4 (local_20d9)
                    w.WriteUInt32(levelAux);   // local_1ff3 (local_20ac)
                    w.WriteUInt32(valA);       // local_1fef (local_20a0)
                    w.WriteUInt32(valB);       // local_1feb (local_20a4)
                    w.WriteUInt32(valC);       // local_1fe7 (local_20a8)
                    w.WriteWord(levelExtra);   // local_1fe3 (local_20cc[0])
                    w.WriteUInt32(itemA);      // local_1fe1 (local_20b8)
                    w.WriteUInt32(itemB);      // local_1fdd (local_20b4)
                    w.WriteUInt32(dropLevel);  // local_1fd9 (local_20b0)
                    w.WriteWord(itemC);        // local_1fd5 (local_20d8._0_2_)
                    w.WriteByte(itemD);        // local_1fd3 (local_20d8[2])
                    w.WriteUInt32(extraA);     // local_1fd2 (local_2090)
                    w.WriteUInt32(extraB);     // local_1fce (local_208c)
                    w.WriteUInt32(extraC);     // local_1fca (local_2088)
                    w.WriteByte(0);            // local_1fc6
                    w.WriteByte(0);            // local_1fc5
                    u.SendMessage(0x0a, w.ToArray());

                    _ = material; _ = dropA; _ = dropB; _ = dropC; _ = mode;
                }


        private static void Op_GameResultReport(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00425010. Guard 1: InField (0x1460) && FieldSecondary (0x14a4) -> senao DISC 0x98.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x98); return; }
                    // Guard 2: Status (0x1440) == '\x03' -> senao DISC 0x99.
                    if (u.Status != 0x03) { u.Disconnect(0x99); return; }

                    // FUN_0040b7d0(this_00, &local_30f4(u16), &local_30e5(byte)): le user+0x14a0 (indice do field-object)
                    // e user+0x14a2 (seat byte deste user).
                    ushort fieldObjIdx = u.FieldObjectIndex;  // local_30f4[0]
                    byte mySeat = u.FieldSeat;                // local_30e5
                    var field = ctx.World.GetField(u.FieldId);
                    if (field == null) { u.Disconnect(0x9a); return; }

                    // iVar9 = field-object[fieldObjIdx]. Guard 3: *(iVar9+0x119)==0 -> senao DISC 0x9a.
                    if (field.FieldFlag119 != 0) { u.Disconnect(0x9a); return; }
                    // Guard 4: *(iVar9+8)=='\x02' (StateB) && *(iVar9+0x2b4)=='\x02' (StateMode) -> senao DISC 0x9b.
                    if (!(field.StateB == 0x02 && field.StateMode == 0x02)) { u.Disconnect(0x9b); return; }

                    // Parse cabecalho: b0(<100 -> 0x9c), b1(<6 -> 0x9d), count(<5 -> 0x9e).
                    byte b0 = ctx.P.Byte();   // *param_3 -> local_30d4
                    if (b0 >= 100) { u.Disconnect(0x9c); return; }
                    byte b1 = ctx.P.Byte();   // param_3[1] -> local_30d8
                    if (b1 >= 6) { u.Disconnect(0x9d); return; }
                    byte count = ctx.P.Byte(); // param_3[2] -> bVar2 / local_2fd2
                    if (count >= 5) { u.Disconnect(0x9e); return; }

                    // Lê 'count' seats (u16). Cada um deve ser < MaxUser (this+0x108); senao DISC 0x9f.
                    ushort[] seats = new ushort[count];
                    for (int i = 0; i < count; i++)
                    {
                        ushort sw = ctx.P.UInt16();
                        seats[i] = sw;
                        if (sw >= ctx.World.MaxUser) { u.Disconnect(0x9f); return; }
                    }

                    // FUN_0040bae0(userObj, b0, b1, &gamePoint(local_30bc), &rank(local_30f5)): computa resultado p/ este
                    // user. Retorno (bVar5): >1 -> cleanup sem resposta; ==1 -> reply curto de erro; ==0 -> sucesso.
                    uint gamePoint = 0;   // local_30bc
                    byte rank = mySeat;   // local_30f5
                    byte result = 0;      // (byte)uVar7
                    if (result > 1) { return; } // LAB_00425602: cleanup

                    if (result != 0)
                    {
                        // FUN_004038e0(handler, slot, 3, &local_2010): lobby 0x53 [u8 result] (len 3).
                        using var wErr = new PacketWriter();
                        wErr.WriteWord(0x53);     // local_2010
                        wErr.WriteByte(result);   // local_200e
                        u.SendLobby(wErr.ToArray());
                        Log.Info($"[GameResultReport] slot={u.Slot} result={result} (rejeitado)");
                        return;
                    }

                    // (A) reply de sucesso: FUN_004038e0(handler, slot, len, &local_2010) -> lobby 0x53.
                    // local_200e=result(0), local_200d=CONCAT11(b1,b0) => bytes b0,b1; se count!=0 copia os 'count' seats
                    // (local_3098 -> local_200a) e len = count*2 + 6; senao len = 6. local_200b = count.
                    using (var wOk = new PacketWriter())
                    {
                        wOk.WriteWord(0x53);   // local_2010 subtype
                        wOk.WriteByte(0);      // local_200e (result)
                        wOk.WriteByte(b0);     // local_200d byte0 (local_30d4)
                        wOk.WriteByte(b1);     // local_200d byte1 (local_30d8)
                        wOk.WriteByte(count);  // local_200b
                        for (int i = 0; i < count; i++) wOk.WriteWord(seats[i]); // local_200a[]
                        u.SendLobby(wOk.ToArray());
                    }
                    Log.Info($"[GameResultReport] slot={u.Slot} obj={fieldObjIdx} seat={mySeat} b0={b0} b1={b1} count={count}");

                    // exp/gold do payload em offset (3 + 2*count); cursor ja esta nessa posicao.
                    uint exp = ctx.P.UInt32();    // local_30ec
                    uint gold = ctx.P.UInt32();   // local_30dc
                    // Bonus: *(user+0x236c) != 0 => exp = exp*3/2.
                    if (u.ExpBonusActive) { exp = exp * 3u >> 1; }

                    // FUN_0041cf80(this, &slot, &fieldObjIdx, &rank, &exp, &gold): anti-cheat. 0 = ok, !=0 = invalido.
                    int validate = 0;
                    if (validate != 0)
                    {
                        // FUN_0041d380(this, "Wrong Game Point! Exp : %u, Gold : %u", group) + DISC 0xa0.
                        Log.Warn($"[GameResultReport] slot={u.Slot} WRONG GAME POINT exp={exp} gold={gold}");
                        u.Disconnect(0xa0);
                        return;
                    }

                    // (B) FUN_0040d300(userObj, exp, &gold, &newLevel(local_30f6), &levelAux(local_30c4), &levelExtra
                    // (local_30e4)): aplica exp/gold e calcula level-up. Retorno (local_30e0) != 0 -> lobby 0x51.
                    byte newLevel = rank;   // local_30f6
                    uint levelAux = 0;      // local_30c4
                    ushort levelExtra = 0;  // local_30e4[0]
                    bool leveled = false;   // local_30e0 != 0
                    if (leveled)
                    {
                        using var wLvl = new PacketWriter();
                        wLvl.WriteWord(0x51);       // local_2010
                        wLvl.WriteByte(newLevel);   // local_200e (local_30f6)
                        wLvl.WriteByte((byte)levelExtra); // local_200d (local_30e4[0])
                        u.SendLobby(wLvl.ToArray());
                    }

                    // drops em param_3 + (cursor+8): 3 u32 (local_30b0/30ac/30a8).
                    uint dropA = ctx.P.UInt32();
                    uint dropB = ctx.P.UInt32();
                    uint dropC = ctx.P.UInt32();

                    // (C) FUN_0040b940(userObj, &dropStruct, &local_30d0, local_30f0, &local_30a4): processa itens/quest.
                    // Retorno (uVar8) != 0 (ou leveled) -> FUN_004061f0(fieldObject, 7, &local_1010) broadcast 0x52.
                    ushort questQ0 = 0;   // local_30f0._0_2_
                    byte questQ1 = 0;     // local_30f0[2]
                    uint questD0 = 0;     // local_30d0
                    uint questA = 0;      // local_30a4
                    bool questUpdated = false; // uVar8 != 0
                    if (leveled || questUpdated)
                    {
                        using var wq = new PacketWriter();
                        wq.WriteWord(0x52);     // local_1010
                        wq.WriteByte(mySeat);   // local_100e (local_30e5)
                        wq.WriteByte(newLevel); // local_100d (local_30f6)
                        wq.WriteWord(questQ0);  // local_100c (local_30f0._0_2_)
                        wq.WriteByte(questQ1);  // local_100a (local_30f0[2])
                        field.Broadcast(wq.ToArray());
                    }

                    // FUN_0040bb60(userObj, '\x03', &valA(local_30b4), &valB(local_30b8), &valC(local_30c0)): stats finais.
                    uint valA = 0; // local_30b4
                    uint valB = 0; // local_30b8
                    uint valC = 0; // local_30c0

                    // (D) snapshot final: FUN_0041b940(this, slot, len, local_3010) -> SendMessage subtype 0x0a (10).
                    // Base len 0x3f; se count!=0 copia seats (local_3098 -> local_2fd1) e len += count*2.
                    // Apos isso grava b1 e, se b1!='\0', um bloco com id do field-object + b0 + gamePoint + rank.
                    using (var wState = new PacketWriter())
                    {
                        wState.WriteUInt32((uint)u.FieldHandleRaw);     // local_300c (field-obj +0x1460 / user+0x1460)
                        wState.WriteWord((ushort)(u.GroupId & 0xffff));  // local_3010 (+0x1488 sessao) — header local
                        wState.WriteUInt32(gold);                        // local_3008 (local_30dc)
                        wState.WriteUInt32((uint)u.FieldSecondaryRaw);   // local_3004 (user+0x14a4)
                        wState.WriteByte(newLevel);                      // local_3000 (local_30f6)
                        wState.WriteUInt32(levelAux);                    // local_2fff (local_30c4)
                        wState.WriteUInt32(valA);                        // local_2ffb (local_30b4)
                        wState.WriteUInt32(valB);                        // local_2ff7 (local_30b8)
                        wState.WriteUInt32(valC);                        // local_2ff3 (local_30c0)
                        wState.WriteWord(levelExtra);                    // local_2fef (local_30e4[0])
                        wState.WriteUInt32(questD0);                     // local_2fed (local_30d0)
                        wState.WriteUInt32(0);                           // local_2fe9 (local_30cc)
                        wState.WriteUInt32(0);                           // local_2fe5 (local_30c8)
                        wState.WriteWord(questQ0);                       // local_2fe1 (local_30f0._0_2_)
                        wState.WriteByte(questQ1);                       // local_2fdf (local_30f0[2])
                        wState.WriteUInt32(questA);                      // local_2fde (local_30a4)
                        wState.WriteUInt32(0);                           // local_2fda (local_30a0)
                        wState.WriteUInt32(0);                           // local_2fd6 (local_309c)
                        wState.WriteByte(count);                         // local_2fd2
                        for (int i = 0; i < count; i++) wState.WriteWord(seats[i]); // local_2fd1[]
                        wState.WriteByte(b1);                            // local_3010[iVar9] = (char)local_30d8
                        if (b1 != 0)
                        {
                            // *(field-obj[fieldObjIdx] + 4): id do field-object.
                            wState.WriteUInt32((uint)field.Id);          // local_3010+iVar9+1
                            wState.WriteByte(b0);                        // local_300c[..] (local_30d4)
                            wState.WriteUInt32(gamePoint);               // local_30bc
                            wState.WriteByte(rank);                      // local_30f5
                        }
                        u.SendMessage(0x0a, wState.ToArray());
                    }

                    _ = dropA; _ = dropB; _ = dropC;
                }


        private static void Op_GameChat(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa1); return; }
                    // Guard 2: status field (3); se nao, retorna sem disconnect
                    if (u.Status != 0x03) { return; }

                    // FUN_0040b7d0: fieldIdx + slot do remetente
                    // TODO FUN_0040b7d0(this_00, out fieldIdx, out mySlot)
                    int fieldIdx = u.FieldId;
                    byte mySlot = (byte)u.Slot;
                    var field = ctx.World.GetField(fieldIdx);

                    // Parse: u16 = tamanho do conteudo
                    ushort len = ctx.P.UInt16();
                    if (len > 1000) { u.Disconnect(0xa2); return; }
                    byte[] body = ctx.P.Bytes(len);

                    // FUN_00405f30: faz broadcast do chat no field para todos os jogadores
                    // TODO FUN_00405f30(field, mySlot, len, body)
                    using (var w = new PacketWriter())
                    {
                        w.WriteByte(mySlot);
                        w.WriteWord(len);
                        w.WriteBytes(body);
                        field?.Broadcast(Prefix(0x56, w.ToArray()), u);
                    }
                }

        private static void Op_GameVoiceChat(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa3); return; }
                    // Guard 2
                    if (u.Status != 0x03) { return; }

                    // FUN_0040b7d0: fieldIdx + mySlot
                    // TODO FUN_0040b7d0(this_00, out fieldIdx, out mySlot)
                    int fieldIdx = u.FieldId;
                    byte mySlot = (byte)u.Slot;
                    var field = ctx.World.GetField(fieldIdx);

                    // Parse: u8 canal/indice (<= 0x13)
                    byte channel = ctx.P.Byte();
                    if (channel > 0x13) { u.Disconnect(0xa4); return; }
                    // u16 tamanho (<= 1000)
                    ushort len = ctx.P.UInt16();
                    if (len > 1000) { u.Disconnect(0xa5); return; }
                    byte[] body = ctx.P.Bytes(len);

                    // FUN_004060a0: broadcast do chat/voz no field considerando o canal
                    // TODO FUN_004060a0(field, mySlot, channel, len, body)
                    using (var w = new PacketWriter())
                    {
                        w.WriteByte(mySlot);
                        w.WriteByte(channel);
                        w.WriteWord(len);
                        w.WriteBytes(body);
                        field?.Broadcast(Prefix(0x57, w.ToArray()), u);
                    }
                }

        private static void Op_GameEmoteAction(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa6); return; }
                    // Guard 2
                    if (u.Status != 0x03) { return; }

                    // Parse (na ordem do binario, ANTES do FUN_0040b7d0)
                    byte action = ctx.P.Byte();
                    if (action > 0x13) { u.Disconnect(0xa7); return; }
                    uint data = ctx.P.UInt32();

                    // FUN_0040b7d0: fieldIdx + mySlot
                    // TODO FUN_0040b7d0(this_00, out fieldIdx, out mySlot)
                    int fieldIdx = u.FieldId;
                    byte mySlot = (byte)u.Slot;
                    var field = ctx.World.GetField(fieldIdx);

                    // FUN_004062c0: executa/transmite a acao no field. 3o arg = slot do user (uVar3 = (ushort)param_1 original).
                    // TODO FUN_004062c0(field, action, u.Slot, data)
                    using (var w = new PacketWriter())
                    {
                        w.WriteByte((byte)u.Slot);
                        w.WriteByte(action);
                        w.WriteUInt32(data);
                        field?.Broadcast(Prefix(0x59, w.ToArray()), u);
                    }
                }

        private static void Op_GameWhisperToSlot(HandlerContext ctx)
                {
                    var u = ctx.User;
                    // Guard 1
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa8); return; }
                    // Guard 2: status field (3); se nao, apenas retorna
                    if (u.Status != 0x03) { return; }

                    // Parse: slot alvo (local_1008/uVar2)
                    ushort targetSlot = ctx.P.UInt16();
                    if (targetSlot >= ctx.World.MaxUser) { u.Disconnect(0xa9); return; }

                    // FUN_0040b7d0: meu seat (local_1008) + meu slot (local_100a)
                    // TODO FUN_0040b7d0(this_00, out mySeat, out mySlot)
                    ushort mySlot = u.Slot;     // local_100a -> local_1002 (undefined2 = 2 bytes)
                    uint data = ctx.P.UInt32(); // local_1000 = *(param_3+1)

                    // Localiza a sessao do alvo e confirma que esta em field (status==3)
                    ClientSession target = null;
                    foreach (var s in ctx.World.Sessions)
                    {
                        if (s.Slot == targetSlot) { target = s; break; }
                    }
                    if (target == null || target.Status != 0x03) { return; }

                    // Resposta enviada AO ALVO via SendLobby (canal lobby), len 8: 0x5a { u16 mySlot, u32 data }
                    using (var w = new PacketWriter())
                    {
                        w.WriteWord(mySlot);   // local_1002 (undefined2 = 2 bytes)
                        w.WriteUInt32(data);   // local_1000
                        target.SendLobby(Prefix(0x5a, w.ToArray()));
                    }
                }

        private static void Op_FieldPlayerAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00425990. pvVar2 = user[slot] (this+0xd4, stride 0x23b4).
                    // Guard 1: user+0x1460 && user+0x14a4 != 0, senao DISC 0xaa.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xaa); return; }
                    // Guard 2: user+0x1440 (Status) == 3, senao DISC 0xab.
                    if (u.Status != 0x03) { u.Disconnect(0xab); return; }
                    // Guard 3: user+0x146c (SubStatus) == 1, senao DISC 0xac.
                    if (u.SubStatus != 0x01) { u.Disconnect(0xac); return; }

                    // FUN_0040b7d0(user, out fieldId(local_4,u16), out slotInField(param_1,byte)).
                    int fieldId = u.FieldId;          // local_4 (u16)
                    byte slotInField = (byte)u.Slot;  // (char)param_1

                    var field = ctx.World.GetField(fieldId); // base = *(this+0xe4)+fieldId*0x3c0
                    if (field == null) return;

                    // Gate do exe (le o byte de acao SOMENTE quando entra):
                    //   playerRecord+8 != 2  (jogador nao no estado proibido),
                    //   slotInField == playerRecord+0x121 (slot deste jogador no field),
                    //   acao < 0x12 (faixa valida).
                    // Modelado: playerRecord+8 == FieldPlayInactive(2). O slot do jogador no field e o proprio.
                    if (u.FieldRecordState == 0x02) return;
                    if (slotInField != (byte)u.Slot) return;

                    byte action = ctx.P.Byte();  // bVar1 = *param_3
                    if (action >= 0x12) return;

                    // FUN_00409080(playerRecord, action): aplica a acao do jogador dentro do field
                    // (animacao/estado) e a propaga aos demais membros do field. Sem pacote de resposta direto.
                    // Modelado: repassa a acao aos outros jogadores do field, subtype 0x5b.
                    using var w = new PacketWriter();
                    w.WriteByte(slotInField); // identifica o autor da acao no field
                    w.WriteByte(action);
                    field.Broadcast(WorldHandlers.Prefix(0x5b, w.ToArray()), u);
                }


        private static void Op_FieldChatNamed(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: must be in field + secondary field flag set -> DISC 0xad
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xad); return; }
            // Guard: status must be 3 (field) -> DISC 0xae
            if (u.Status != 0x03) { u.Disconnect(0xae); return; }

            // Payload: [byte targetCode][cstring text]
            byte targetCode = ctx.P.Byte();          // local_108d = *param_3
            string text = ctx.P.CString();           // lstrcpyA(buf, param_3+1)
            // local_108f = '\0' -> a leading category/flag byte passed to FUN_0040a420
            byte category = 0x00;

            // FUN_0040b7d0(userObj, out fieldId, out slotInField)
            // TODO FUN_0040b7d0: resolve the user's field and slot.
            int fieldId = u.FieldId;                  // auStack_108c[0]
            byte slotInField = (byte)u.Slot;          // bStack_108e

            var field = ctx.World.GetField(fieldId);

            // FUN_0040a420(fieldObj, &category, &slotInField, &targetCode, textBuf)
            // performs the chat/whisper delivery within the field; returns nonzero on success.
            // TODO FUN_0040a420: deliver the field chat; assume success (true) to preserve
            // the response structure.
            bool ok = field != null; // model: success when field resolved
            _ = category; _ = slotInField; _ = targetCode; _ = text;

            if (ok)
            {
                // On success: lobby ack, subtype 0x5f, total len 3 (subtype only).
                u.SendLobby(BuildSubtypeOnly(0x5f));
            }
        }

        // helper: lobby payload carrying only the [u16 subtype] (matches FUN_004038e0 len=3)
        private static byte[] BuildSubtypeOnly(int subtype)
        {
            using var w = new PacketWriter();
            w.WriteWord(subtype);
            return w.ToArray();
        }

        private static void Op_FieldChatCode(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: must be in field + secondary field flag set -> DISC 0xaf
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xaf); return; }
            // Guard: status must be 3 (field) -> DISC 0xb0
            if (u.Status != 0x03) { u.Disconnect(0xb0); return; }

            // Payload: [byte code]
            byte code = ctx.P.Byte();                 // local_100b = *param_3
            byte category = 0x00;                     // local_100a = 0

            // FUN_0040b7d0(userObj, out fieldId, out slotInField)
            // TODO FUN_0040b7d0: resolve the user's field and slot.
            int fieldId = u.FieldId;                  // local_1008[0]
            byte slotInField = (byte)u.Slot;          // local_1009

            var field = ctx.World.GetField(fieldId);

            // FUN_0040a420(fieldObj, &code, &slotInField, &category, NULL)
            // same delivery routine as 0x5d but with no text buffer (last arg NULL).
            // TODO FUN_0040a420: deliver the field signal/emote; assume success.
            bool ok = field != null;
            _ = code; _ = slotInField; _ = category;

            if (ok)
            {
                // On success: lobby ack, subtype 0x5f, total len 3 (subtype only).
                using var w = new PacketWriter();
                w.WriteWord(0x5f);
                u.SendLobby(w.ToArray());
            }
        }

        private static void Op_FieldTargetCommand(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00425cc0. pvVar2 = user[slot].
                    // Guard 1: user+0x1460 && user+0x14a4 != 0, senao DISC 0xb1.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xb1); return; }
                    // Guard 2: user+0x1440 (Status) == 3, senao DISC 0xb2.
                    if (u.Status != 0x03) { u.Disconnect(0xb2); return; }

                    // FUN_0040b7d0(user, out fieldId(param_1,u16), out slotInField(local_4,byte)).
                    int fieldId = u.FieldId;          // param_1 (u16)
                    byte slotInField = (byte)u.Slot;  // (char)local_4

                    var field = ctx.World.GetField(fieldId); // base = *(this+0xe4)+fieldId*0x3c0
                    if (field == null) return;

                    // Gate do exe: slotInField precisa bater com um dos dois bytes de papel do field
                    //   playerRecord+0x122 (lider time A)  OU  playerRecord+0x123 (lider time B).
                    // So entao o comando e aplicado. Modelado via Field.LeaderSlotA/LeaderSlotB.
                    if (slotInField != field.LeaderSlotA && slotInField != field.LeaderSlotB) return;

                    // Payload (lido dentro do gate, como no binario): [byte arg0][u16 arg1].
                    byte arg0 = ctx.P.Byte();      // *param_3
                    ushort arg1 = ctx.P.UInt16();  // *(u16*)(param_3+1)

                    // FUN_00405ef0(playerRecord, arg0, arg1): aplica o comando de alvo/objetivo do field
                    // (so atua quando record+8==2 && record+0x2b4==1 && record+0x119==4; arg0<10 grava em
                    //  record+0x2c8, senao em record+0x2ca). Sem pacote de resposta. Repassa o estado ao field.
                    if (arg0 < 10) u.FieldTargetA = arg1; // playerRecord+0x2c8
                    else u.FieldTargetB = arg1;           // playerRecord+0x2ca

                    using var w = new PacketWriter();
                    w.WriteByte(slotInField);
                    w.WriteByte(arg0);
                    w.WriteWord(arg1);
                    field.Broadcast(WorldHandlers.Prefix(0x60, w.ToArray()), u);
                }


        // field role-slot bytes at field+0x122 / field+0x123 (privileged slots).
        // TODO: map to the real Domain.Field fields once known.
        private static byte FieldRoleSlotA(Domain.Field f) => 0; // field+0x122
        private static byte FieldRoleSlotB(Domain.Field f) => 0; // field+0x123

        private static void Op_SetUserPing(HandlerContext ctx)
        {
            var u = ctx.User;
            // No state guards in the binary.

            // Payload: [int value]
            int value = ctx.P.Int32();        // iVar1 = *param_3

            // Store at user+0x2380 (per-slot field, e.g. measured ping/latency value).
            // TODO: expose this as a typed property on ClientSession (e.g. u.Ping = value).
            u.Ping = value;

            // If it matches the server-wide reference value (this+0x51b4, a WorldServer
            // counter/threshold), bump the match counter (this+0x51bc).
            // TODO: map this+0x51b4 / this+0x51bc to WorldServer fields once identified.
            if (value == ctx.World.PingReference)
            {
                ctx.World.PingMatchCount++;
            }
        }

        private static void Op_FieldRelayAction(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_0041c2b0. this_00 = user[slot].
                    // Guard (forma combinada do exe, sem disconnect): user+0x1460 && user+0x14a4 != 0 && Status==3.
                    // Se qualquer condicao falhar o handler simplesmente retorna (nenhum DISC neste opcode).
                    if (!(u.InField && u.FieldSecondary && u.Status == 0x03)) return;

                    if (!ctx.P.CanRead(1)) return;
                    byte src = ctx.P.Byte(); // *param_3 -> reempacotado em (byte)param_1 e usado como source

                    // FUN_0040b7d0(user, out fieldId(local_4,u16), out param_3(byte: indice de membro alvo)).
                    // local_4 indexa o array this+0xe4 (passo 0x3c0): o registro de campo do proprio jogador.
                    int fieldId = u.FieldId;
                    var field = ctx.World.GetField(fieldId);
                    if (field == null) return;

                    // FUN_00406930(playerRecord, &param_3, &src):
                    //   uVar1 = *(playerRecord + (*param_3)*0x14 + 0x124)  -> slot global do membro de indice (*param_3)
                    //   monta [u16 0x62][byte src] e envia (FUN_0041b8a0) APENAS para esse membro.
                    // O segundo byte do payload (param_3 apos FUN_0040b7d0) e o indice do membro destino no field.
                    byte memberIndex = (byte)u.Slot; // param_3 (byte) resolvido por FUN_0040b7d0

                    // Modelado: relay direcionado. Sem o mapa indice->slot do field, repassa o byte 'src'
                    // (subtype 0x62) aos demais membros do field, preservando o relay observavel.
                    _ = memberIndex;
                    using var w = new PacketWriter();
                    w.WriteByte(src); // local_1002 = *param_1
                    field.Broadcast(WorldHandlers.Prefix(0x62, w.ToArray()), u);
                }


        private static void Op_VerifyTutorialStage(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: SubStatus (*(user+0x146c)) deve ser '4' (0x34). Senao => disconnect 0xb9.
            if (u.SubStatus != 0x34)
            {
                u.Disconnect(0xb9);
                return;
            }

            // FUN_0040abe0(user, &local_14, &local_c): obtem um par de valores do estado do usuario.
            // local_14[0] e comparado contra a tabela global DAT_00456034..0x456044 (4 ints).
            // TODO FUN_0040abe0: extrair (id, aux) do estado do slot.
            int stageId = u.FieldId; // efeito minimo: usa o id de field/stage atual como local_14[0]

            // Tabela permitida de stages (DAT_00456034, 4 entradas u32). Valores reais do .data nao
            // estao neste corpo; modelado como conjunto cujo conteudo deve vir da config.
            // TODO DAT_00456034: carregar os 4 ids validos.
            int[] allowedStages = ctx.World.Config?.AllowedTutorialStages ?? System.Array.Empty<int>();

            bool ok = false;
            foreach (var id in allowedStages)
            {
                if (stageId == id) { ok = true; break; }
            }

            if (!ok)
            {
                // Nenhum match na tabela => disconnect 0xba.
                u.Disconnect(0xba);
                return;
            }
            // Match encontrado: sucesso, nenhum pacote de resposta (goto LAB_00428410).
        }

        private static void Op_VerifyClientHash(HandlerContext ctx)
        {
            var u = ctx.User;
            // *(user+0x237c) = modo/estado do slot. Se for '\x04' ou '\x05', pula toda a verificacao.
            byte mode = u.VerifyMode; // = *(user+0x237c)
            if (mode == 0x04 || mode == 0x05)
                return;

            // Caso contrario: precisa estar no field; senao => disconnect 0xbb.
            if (!u.InField)
            {
                u.Disconnect(0xbb);
                // OBS: o exe NAO retorna aqui; continua e ainda compara o hash abaixo.
            }

            // Copia 0x21 bytes (8 dwords + 1 byte terminador) do payload => string recebida.
            if (!ctx.P.CanRead(0x21))
            {
                u.Disconnect(0xbc);
                return;
            }
            string received = ctx.P.CString(0x21);

            // Escolhe o hash de referencia: se mode == '\x01' usa user+0x14d (MD5_2), senao user+300 (MD5_1).
            // (this+0x14d e this+0x12c sao os dois hashes MD5 salvos em opcode 0x0b.)
            string expected = (mode == 0x01) ? u.Md5Hash2 : u.Md5Hash1;

            // Comparacao de string (case-sensitive). Diferente => disconnect 0xbc.
            if (!string.Equals(received ?? string.Empty, expected ?? string.Empty, System.StringComparison.Ordinal))
            {
                u.Disconnect(0xbc);
                return;
            }
        }

        private static void Op_RequestFieldTick(HandlerContext ctx)
                {
                    // FUN_004286a0 (opcode 0x6b). iVar1 = slot*0x23b4 + *(this+0xd4).
                    var u = ctx.User;

                    // Guard: (*(iVar1+0x1460) != 0) && (*(iVar1+0x14a4) != 0) -> InField && FieldSecondary.
                    // Senao => FUN_0041eb20(this, slot, 0xc2, 1, 1) -> Disconnect(0xc2).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xc2); return; }

                    // Resposta plana (FUN_0041b940 = SendMessage), layout a partir de &local_1004:
                    //   local_1004 = *(u16)(iVar1+0x1488) -> seq slot (escrito pelo SendMessage em C#).
                    //   local_1002 = 0x1e                -> subtype.
                    //   local_1000 = *(int)(iVar1+0x1460) -> payload de 4 bytes (field handle / InField marker).
                    // len = 8 = seq(2)+subtype(2)+payload(4). Em C#, SendMessage ja escreve seq+subtype,
                    // entao envio apenas o payload (os 4 bytes de local_1000).
                    int fieldHandle = u.FieldHandleRaw; // *(int)(user+0x1460) = local_1000

                    Log.Info("field", "RequestFieldTick: slot {0} field {1} handle=0x{2:x8}", u.Slot, u.FieldId, fieldHandle);

                    using var w = new PacketWriter();
                    w.WriteInt32(fieldHandle); // local_1000 (unico campo apos o subtype)
                    u.SendMessage(0x1e, w.ToArray());
                }


        private static void Op_RequestFieldSnapshot(HandlerContext ctx)
                {
                    // FUN_00428750 (opcode 0x6c). iVar7 = slot*0x23b4; this_00 = *(this+0xd4) + iVar7.
                    var u = ctx.User;

                    // Guard: (*(this_00+0x1460) != 0) && (local_12f0 = *(this_00+0x14a4)) != 0
                    //        -> InField && FieldSecondary. Senao => Disconnect(0xc3).
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xc3); return; }
                    int fieldSecondary = u.FieldSecondaryRaw; // local_12f0 = *(int)(user+0x14a4)

                    // Le do payload (param_3 = undefined4*): uVar3 = *param_3 (u32); uVar2 = *(u16)(param_3+1).
                    if (!ctx.P.CanRead(6)) { u.Disconnect(0xc3); return; }
                    uint reqA = (uint)ctx.P.Int32();   // uVar3 = *param_3
                    ushort reqB = ctx.P.UInt16();      // uVar2 = *(u16)(param_3+1)

                    // FUN_0040ca50(this_00, &cntA(local_12f2), listA(local_12e8), listA2(local_109c),
                    //   &cntB(local_12f3), listB(local_1298), listB2(local_1088), &flagC(local_12f1),
                    //   &extra(local_10b4)):
                    // coleta o snapshot do field do usuario em tres blocos opcionais:
                    //   - bloco A: cntA entradas; por entrada 0x13 bytes (listA) seguidos de 4 bytes (listA2).
                    //   - bloco B: cntB entradas; por entrada 4 bytes (listB) seguidos de 1 byte (listB2).
                    //   - bloco C: flag flagC; se != 0, anexa o cabecalho estendido (~0x21 bytes):
                    //       *(int)(iVar1+0x1460), *(int)(iVar1+0x14a4), local_10b4..local_10a0,
                    //       *(int)(iVar1+0x1460) de novo e *(u16)(iVar1+0x2370).
                    // Corpo de FUN_0040ca50 nao presente no decompile -> efeito modelado: snapshot vazio,
                    // preservando o formato exato da resposta.
                    byte cntA = 0, cntB = 0;
                    byte flagC = 0;
                    byte[] blockA = System.Array.Empty<byte>();   // cntA * (0x13 + 4)
                    byte[] blockB = System.Array.Empty<byte>();   // cntB * (4 + 1)
                    byte[] blockC = System.Array.Empty<byte>();   // cabecalho estendido quando flagC != 0

                    // Cabecalho fixo da resposta (FUN_0041b940 = SendMessage), layout a partir de &local_1010:
                    //   local_1010._0_2_ : *(u16)(user+0x1488)  -> seq slot (escrito pelo SendMessage em C#).
                    //   local_1010._2_2_ : 0x1f (subtype).
                    //   local_100c[0]    : *(int)(iVar1+0x1460)  (field handle).
                    //   local_100c[1]    : reqA (echo do u32 recebido).
                    //   local_1004       : reqB (echo do u16 recebido).
                    //   local_1002       : fieldSecondary (int, *(user+0x14a4) = local_12f0).
                    //   local_ffe        : cntA.
                    //   ...em seguida: bloco A; byte cntB; bloco B; byte flagC; bloco C (se flagC != 0).
                    // Em C#, SendMessage ja escreve seq+subtype(0x1f); o payload comeca no field handle.
                    int fieldHandle = u.FieldHandleRaw;    // *(int)(user+0x1460) = local_100c[0]

                    Log.Info("field", "RequestFieldSnapshot: slot {0} field {1} reqA=0x{2:x8} reqB=0x{3:x4} cntA={4} cntB={5} flagC={6}", u.Slot, u.FieldId, reqA, reqB, cntA, cntB, flagC);

                    using var w = new PacketWriter();
                    w.WriteInt32(fieldHandle);         // local_100c[0]
                    w.WriteUInt32(reqA);               // local_100c[1] (echo)
                    w.WriteWord(reqB);                 // local_1004 (echo)
                    w.WriteInt32(fieldSecondary);      // local_1002
                    w.WriteByte(cntA);                 // local_ffe
                    w.WriteBytes(blockA);              // entradas do bloco A (0x13 + 4 por entrada)
                    w.WriteByte(cntB);                 // byte de contagem do bloco B
                    w.WriteBytes(blockB);              // entradas do bloco B (4 + 1 por entrada)
                    w.WriteByte(flagC);                // flag do bloco C
                    if (flagC != 0)
                        w.WriteBytes(blockC);          // cabecalho estendido do field
                    u.SendMessage(0x1f, w.ToArray());
                }


        private static void Op_FieldEmoteEcho(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00428a10. iVar1 = user[slot]. local_1000 = user+0x1460 (handle de field, cru).
                    // Guard: user+0x1460 != 0 && user+0x14a4 != 0, senao DISC 0xc4.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xc4); return; }

                    // Payload (undefined4* param_3): [u32 echo]. local_ffc = *param_3.
                    int echo = ctx.P.Int32();

                    // Resposta via FUN_0041b940(this, slot, len=0xc, &local_1004):
                    //   local_1004 = *(user+0x1488) (serverSeq) -> injetado por SendMessage
                    //   local_1002 = 0x20                       -> subtype/msgType
                    //   local_1000 = *(user+0x1460)             -> handle de field (cru), reenviado no corpo
                    //   local_ffc  = echo                       -> dword ecoado
                    // len 0xc = [u16 seq][u16 subtype][u32 handle][u32 echo].
                    using var w = new PacketWriter();
                    w.WriteInt32(u.FieldHandleRaw); // local_1000 (= cru de user+0x1460)
                    w.WriteInt32(echo);             // local_ffc
                    u.SendMessage(0x20, w.ToArray());
                }


        private static void Op_FieldUseItem(HandlerContext ctx)
        {
                    var u = ctx.User;
                    // FUN_00428c90. Guard: this_00+0x1460 (InField) && this_00+0x14a4 (FieldSecondary) != 0 -> senao DISC 0xd0.
                    if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xd0); return; }
                    // this_00+0x1440 (Status) deve ser '\x03' -> senao DISC 0xd1.
                    if (u.Status != 0x03) { u.Disconnect(0xd1); return; }

                    // Payload: [u8 itemType][s16 itemArg]. (bVar1 = *param_3, sVar2 = *(param_3+1)).
                    byte itemType = ctx.P.Byte();   // bVar1
                    short itemArg = ctx.P.Int16();  // sVar2

                    // FUN_0040b7d0(this_00, &local(u16), &local(byte)): le user+0x14a0 (indice do field-object) e
                    // user+0x14a2. O 4o argumento de FUN_0040e5f0 e *(field-object[idx] + 0x119) = a flag/modo do field.
                    ushort fieldObjIdx = u.FieldObjectIndex;
                    var field = ctx.World.GetField(u.FieldId);
                    byte fieldModeFlag = field != null ? field.Mode : (byte)0; // *(fieldObj[idx]+0x119)

                    // FUN_0040e5f0(userObj, itemType, itemArg, fieldModeFlag): aplica o uso de item no field. Retorna
                    // 0 em falha -> DISC 0xd2. Helper sem corpo neste slice; modelado como sucesso (!=0) e broadcast
                    // do uso de item aos demais jogadores do field.
                    int useResult = 1;
                    if (useResult == 0) { u.Disconnect(0xd2); return; }

                    Log.Info($"[FieldUseItem] slot={u.Slot} obj={fieldObjIdx} type={itemType} arg={itemArg} mode={fieldModeFlag}");
                    if (field != null)
                    {
                        using var bw = new PacketWriter();
                        bw.WriteWord(0x6e);
                        bw.WriteWord(fieldObjIdx);
                        bw.WriteByte(itemType);
                        bw.WriteInt16(itemArg);
                        field.Broadcast(bw.ToArray(), u);
                    }
                }


        private static void Op_RoomReadyEmblem(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1: in field + secondary, else DISC 0xd3
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xd3); return; }
            // Guard 2: status must be 0x02 (room), else DISC 0xd4
            if (u.Status != 0x02) { u.Disconnect(0xd4); return; }

            // *(user+0x2368): selects an emblem/team id; only 3,4,5 are valid.
            // NOTE: real offset is +0x2368, NOT SubStatus (+0x146c). SubStatus is used here only
            // as the closest modeled field; replace with the true +0x2368 field when available.
            byte sel = ctx.User.SubStatus; // modeled source of *(user+0x2368); see notes
            int emblemId;
            switch (sel)
            {
                case 0x03: emblemId = 0x2718; break;
                case 0x04: emblemId = 0x2719; break;
                case 0x05: emblemId = 0x271a; break;
                default: u.Disconnect(0xd5); return; // invalid selection
            }

            // FUN_0040a860(table[emblemId], &local_1009 (1 byte), &local_1008 (4 bytes)):
            // looks up two display fields for the emblem from the 0x20-stride table at (this+0x10c).
            // TODO FUN_0040a860: emblem display lookup. Assume zeros.
            byte field8 = 0;     // local_ff8 <- local_1009 (1 byte)
            uint field7 = 0;     // local_ff7 <- local_1008 (4 bytes)

            // Response struct (FUN_0041b940 => SendMessage, subtype 0x21, len 0x13):
            //   local_1004 seq (set by SendMessage)
            //   local_1002 = 0x21 subtype
            //   local_1000 = *(user+0x1460) in-field dword
            //   local_ffc  = *(user+0x14a4) secondary dword
            //   local_ff8  = field8 (1 byte)
            //   local_ff7  = field7 (4 bytes)
            //   local_ff3  = (u16)emblemId
            using var w = new PacketWriter();
            w.WriteInt32(u.InField ? 1 : 0);        // local_1000
            w.WriteInt32(u.FieldSecondary ? 1 : 0); // local_ffc
            w.WriteByte(field8);                    // local_ff8
            w.WriteUInt32(field7);                  // local_ff7
            w.WriteWord(emblemId);                  // local_ff3 (u16)
            u.SendMessage(0x21, w.ToArray());
        }

        private static void Op_RoomRankReward(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1: in field + secondary, else DISC 0xd9
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xd9); return; }
            // Guard 2: status must be 0x02 (room), else DISC 0xda
            if (u.Status != 0x02) { u.Disconnect(0xda); return; }

            // bVar1 = *(user+0x1531): a rank/level value bucketed into reward tiers.
            byte rank = 0; // *(user+0x1531) -- modeled; see notes

            // Bucket -> (rewardPtrField, rewardId). Default (no match) keeps zeros.
            uint rewardField = 0; // local_ff8 (4 bytes, from item table at this+0x10c + offset)
            int rewardId = 0;     // local_ff4 (u16)
            if (rank >= 10 && rank <= 0x14)        // 10..20
            {
                // local_ff8 = *(this+0x10c + 0x4e368); local_ff4 = 0x271b
                // TODO read item table at (this+0x10c)+0x4e368
                rewardField = 0;
                rewardId = 0x271b;
            }
            else if (rank >= 0x15 && rank <= 0x28) // 21..40
            {
                // local_ff8 = *(this+0x10c + 0x4e388); local_ff4 = 0x271c
                rewardField = 0;
                rewardId = 0x271c;
            }
            else if (rank > 0x28)                   // >40
            {
                // local_ff8 = *(this+0x10c + 0x4e3a8); local_ff4 = 0x271d
                rewardField = 0;
                rewardId = 0x271d;
            }
            // rank < 10 -> rewardField/rewardId stay 0

            // Response struct (FUN_0041b940 => SendMessage, subtype 0x23, len 0x12):
            //   local_1004 seq (set by SendMessage)
            //   local_1002 = 0x23 subtype
            //   local_1000 = *(user+0x1460) in-field dword
            //   local_ffc  = *(user+0x14a4) secondary dword
            //   local_ff8  = rewardField (4 bytes)
            //   local_ff4  = (u16)rewardId
            using var w = new PacketWriter();
            w.WriteInt32(u.InField ? 1 : 0);        // local_1000
            w.WriteInt32(u.FieldSecondary ? 1 : 0); // local_ffc
            w.WriteUInt32(rewardField);             // local_ff8
            w.WriteWord(rewardId);                  // local_ff4 (u16)
            u.SendMessage(0x23, w.ToArray());
        }

        private static void Op_RoomFixedReward(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1: in field + secondary, else DISC 0xdb
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xdb); return; }
            // Guard 2: status must be 0x02 (room), else DISC 0xdc
            if (u.Status != 0x02) { u.Disconnect(0xdc); return; }

            // local_ff8 = *(this+0x10c + 0x4e3c8): fixed reward field from the item/string table.
            // TODO read item table at (this+0x10c)+0x4e3c8.
            uint rewardField = 0;
            int rewardId = 0x271e; // local_ff4 (u16) - constant

            // Response struct (FUN_0041b940 => SendMessage, subtype 0x24, len 0x12):
            //   local_1004 seq (set by SendMessage)
            //   local_1002 = 0x24 subtype
            //   local_1000 = *(user+0x1460) in-field dword
            //   local_ffc  = *(user+0x14a4) secondary dword
            //   local_ff8  = rewardField (4 bytes)
            //   local_ff4  = 0x271e (u16)
            using var w = new PacketWriter();
            w.WriteInt32(u.InField ? 1 : 0);        // local_1000
            w.WriteInt32(u.FieldSecondary ? 1 : 0); // local_ffc
            w.WriteUInt32(rewardField);             // local_ff8
            w.WriteWord(rewardId);                  // local_ff4 (u16 = 0x271e)
            u.SendMessage(0x24, w.ToArray());
        }

        private static void Op_RoomMemberFieldInfo(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1: precisa estar em field com secundario (DISC 0xd6)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xd6); return; }
            // Guard 2: precisa estar em modo field (Status==0x03) (DISC 0xd7)
            if (u.Status != 0x03) { u.Disconnect(0xd7); return; }

            ushort targetSlot = ctx.P.UInt16();

            // Guard 3: alvo precisa estar em um field (DISC 0xd8)
            var target = ctx.World.Sessions.FirstOrDefault(s => s.Slot == targetSlot);
            if (target == null || !target.InField) { u.Disconnect(0xd8); return; }

            // FUN_0040b7d0(this_00, &local_100c, &local_1005):
            // extrai id de field/sala (u16) e um byte do estado do user remetente.
            // TODO FUN_0040b7d0: modelado como 0 (sucesso/sem dados). Preserva estrutura.
            ushort fieldRef = 0;

            // Nome do remetente fica em (user+0x14a8) => CharName/secondary name.
            string senderName = u.CharName ?? string.Empty;

            using var w = new PacketWriter();
            w.WriteWord(0x72);              // local_1004._0_2_ = subtype 0x72
            w.WriteWord(u.Slot);           // local_1004._2_2_ = senderSlot (param_1)
            w.WriteCString(senderName);    // lstrcpyA(local_1000, name) — nul-terminated
            w.WriteWord(fieldRef);         // *(local_1004 + len+5) = local_100c[0]

            // FUN_00406a80(field[fieldRef], &buf[len+7]) => serializa info do field e retorna tamanho.
            // TODO FUN_00406a80: 0 bytes extras (sucesso, sem payload adicional). Estrutura preservada.
            // (nada a escrever aqui no modelo minimo)

            // Enviado para o SLOT ALVO (uVar1), nao para o remetente.
            target.SendLobby(w.ToArray());
        }

        private static void Op_RoomCharSelectInfo(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xde); return; }
            // Guard 2: modo room (Status==0x02)
            if (u.Status != 0x02) { u.Disconnect(0xdf); return; }

            byte idxA = ctx.P.Byte();
            byte idxB = ctx.P.Byte();
            // Guard 3/4: limites 0x78
            if (idxA >= 0x78) { u.Disconnect(0xe0); return; }
            if (idxB >= 0x78) { u.Disconnect(0xe1); return; }

            // FUN_0040ca50(user, &countA, listA1, listA2, &countB, listB1, listB2, &flag, blockOut)
            // TODO FUN_0040ca50: estado de listas do user. Modelado vazio.
            byte countA = 0;
            byte countB = 0;
            byte flag = 0;

            // FUN_0040c140(user, idxA, idxB, &outA, &outB, &outC) -> codigo de erro
            // TODO FUN_0040c140: assume sucesso (err==0), outs=0.
            byte err = 0;
            uint outA = 0; // local_12f0
            uint outB = 0; // local_12f8
            uint outC = 0; // local_12f4

            if (err != 0)
            {
                // SendLobby[u16 0x73][u8 err]  (FUN_004038e0, len=3)
                using var we = new PacketWriter();
                we.WriteWord(0x73);
                we.WriteByte(err);
                u.SendLobby(we.ToArray());
                return;
            }

            uint roomId = (uint)u.RoomId;                 // local_100c = *(user+0x1460)
            uint fieldHandle = (uint)(u.FieldSecondary ? 1 : 0); // local_1300 = *(user+0x14a4) (TODO valor real)

            using var w = new PacketWriter();
            // SendMessage: passa apenas bytes APOS o subtype 0x27 (local_1010._2_2_).
            w.WriteUInt32(roomId);   // local_100c
            w.WriteByte(idxA);       // local_1008 = bVar2 (idxA)
            w.WriteByte(idxB);       // local_1007 = (undefined)local_1304 (idxB)
            w.WriteUInt32(outA);     // local_1006 = local_12f0
            w.WriteUInt32(outB);     // local_1002 = local_12f8
            w.WriteUInt32(outC);     // local_ffe = local_12f4
            w.WriteUInt32(fieldHandle); // local_ffa = local_1300 (+0x14a4)
            w.WriteByte(countA);     // local_ff6 = countA

            // TODO FUN_0040ca50: anexar countA*itens (u32 listA1 seguidos do tail de listA2),
            // depois countB (u8) + countB*itens, depois flag (u8); se flag!=0, bloco extra de campos
            // (local_10b4..local_10a0 + roomId u32 + field u16). Listas vazias no modelo minimo.
            w.WriteByte(countB);     // *(buf + off) = local_1306
            w.WriteByte(flag);       // *(buf + off) = local_1307

            u.SendMessage(0x27, w.ToArray());
        }

        private static void Op_RoomMoveAction(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xe2); return; }
            // Guard 2: modo room (Status==0x02)
            if (u.Status != 0x02) { u.Disconnect(0xe3); return; }

            byte valA = ctx.P.Byte();   // param_3[0] (tratado como float no exe -> local_12f4)
            byte b1 = ctx.P.Byte();     // param_3[1] (local_12f8)
            byte count = ctx.P.Byte();  // param_3[2] (local_1310)
            // Guard 3: count < 4
            if (count >= 4) { u.Disconnect(0xe4); return; }

            byte[] data = new byte[3];
            for (int i = 0; i < count; i++) data[i] = ctx.P.Byte();

            // FUN_0040ca50: listas do user (igual 0x73). TODO -> vazias.
            byte countA = 0;
            byte countB = 0;
            byte flag = 0;

            // FUN_0040c310(user, valA, b1, count, data, &cFlag, &out1, &out2, &out3, &out4, &out5) -> erro
            // 5 out-params distintos (local_1300, local_1308, local_130c, local_12ec, local_12f0).
            // TODO FUN_0040c310: assume sucesso (err==0), todos os outs=0.
            byte err = 0;
            uint out1 = 0;  // local_1300 (reaproveitado como out)
            uint out2 = 0;  // local_1308
            uint out3 = 0;  // local_130c
            uint out4 = 0;  // local_12ec
            uint out5 = 0;  // local_12f0

            if (err != 0)
            {
                // SendLobby[u16 0x74][u8 err] (FUN_004038e0, len=3)
                using var we = new PacketWriter();
                we.WriteWord(0x74);
                we.WriteByte(err);
                u.SendLobby(we.ToArray());
                return;
            }

            uint roomId = (uint)u.RoomId;                  // local_100c = *(user+0x1460)
            uint fieldHandle = (uint)(u.FieldSecondary ? 1 : 0); // local_1304/local_fed = *(user+0x14a4) (TODO valor real)

            using var w = new PacketWriter();
            // SendMessage: bytes APOS o subtype 0x28 (local_1010._2_2_), na ORDEM EXATA do struct.
            w.WriteUInt32(roomId);    // local_100c   @4
            w.WriteByte(valA);        // local_1008   @8  = (byte)local_12f4
            w.WriteUInt32(out1);      // local_1007   @9  = local_1300 (out)
            w.WriteByte(b1);          // local_1003   @13 = (byte)local_12f8
            w.WriteUInt32(out2);      // local_1002   @14 = local_1308 (out)
            w.WriteByte(count);       // local_ffe    @18 = (byte)local_1310
            w.WriteByte(data[0]);     // local_ffd    @19 = local_1314[0]
            w.WriteUInt32(out3);      // local_ffc    @20 = local_130c (out)
            w.WriteByte(data[1]);     // local_ff8    @24 = local_1314[1]
            w.WriteUInt32(out4);      // local_ff7    @25 = local_12ec (out)
            w.WriteByte(data[2]);     // local_ff3    @29 = local_1314[2]
            w.WriteUInt32(out5);      // local_ff2    @30 = local_12f0 (out)
            w.WriteByte(0);           // local_fee    @34 = 0
            w.WriteUInt32(fieldHandle);// local_fed   @35 = local_1304 (+0x14a4)
            w.WriteByte(countA);      // local_fe9    @39 = countA (local_1318)

            // TODO FUN_0040ca50: anexar listas (countA arrays, countB+arrays, flag+bloco) como no 0x73.
            w.WriteByte(countB);      // @40 = local_1317
            w.WriteByte(flag);        // @41 = local_1315

            u.SendMessage(0x28, w.ToArray());
        }

        private static void Op_BuyLotto(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xe5); return; }
            // Guard 2: modo room (Status==0x02)
            if (u.Status != 0x02) { u.Disconnect(0xe6); return; }

            byte[] req = ctx.Raw; // param_3 = payload do request (bytes do bilhete)
            int reqLen = req.Length;

            // Os 5 numeros: offsets do exe param_3[1], [2], [3], [4], [5].
            // (local_2028[0]=p[1], [1]=p[2], [2]=p[3], [3]=p[4], local_2024=p[5])
            byte[] nums = new byte[5];
            for (int i = 0; i < 5; i++)
                nums[i] = (uint)(i + 1) < (uint)reqLen ? req[i + 1] : (byte)0;

            Log.Info($"[RW] # NetworkMessageBuyLotto # - Number : {nums[0]}, {nums[1]}, {nums[2]}, {nums[3]}, {nums[4]}");

            // TODO FUN_0042fe40: determina o tipo de pagamento (local_2045). 0=gold, 1=cash, outro=erro 0xe7.
            byte payType = 0;
            if (payType != 0 && payType != 1) { u.Disconnect(0xe7); return; }

            uint gold = u.Gold;  // *(user+0x1538)
            uint cash = u.Cash;  // *(user+0x153c)

            // Verifica fundos PRIMEIRO (gold>=1000 ou cash>99). Se insuficiente -> falha code 1.
            bool fundsOk = payType == 0 ? gold >= 1000 : cash > 99;
            if (!fundsOk)
            {
                Log.Info("[RW] # NetworkMessageBuyLotto # - not enough gold or cash");
                using var wf = new PacketWriter();
                wf.WriteWord(0x75);      // subtype (FUN_0042fe60 escreveu 0x75 no inicio de local_2020)
                wf.WriteByte(1);         // local_2020[2] = 1 (codigo: fundos insuficientes)
                wf.WriteUInt32(gold);    // local_2020._3_4_ = gold (+0x1538)
                wf.WriteUInt32(cash);    // local_2019 = cash (+0x153c)
                u.SendLobby(wf.ToArray());
                return;
            }

            // Verifica numeros repetidos (compara os 5 numeros entre si, como o loop do exe).
            bool repeat = false;
            for (int a = 1; a < 5 && !repeat; a++)
                for (int b = a; b < 5; b++)
                    if (a != b && nums[a] == nums[b]) { repeat = true; break; }

            if (repeat)
            {
                Log.Info("[RW] # NetworkMessageBuyLotto # - repeat number");
                using var wr = new PacketWriter();
                wr.WriteWord(0x75);      // subtype
                wr.WriteByte(2);         // local_2020[2] = 2 (codigo: numero repetido)
                wr.WriteUInt32(gold);
                wr.WriteUInt32(cash);
                u.SendLobby(wr.ToArray());
                return;
            }

            // Sucesso: ecoa o request inteiro com subtype 0x29 via SendMessage.
            // local_101c = *(user+0x1460) (roomId); SendMessage ja injeta seq+subtype.
            uint roomId = (uint)u.RoomId; // local_101c = *(user+0x1460)
            using var w = new PacketWriter();
            w.WriteUInt32(roomId);   // local_101c
            w.WriteBytes(req);       // copia param_2 bytes do request (local_1018)
            u.SendMessage(0x29, w.ToArray());
        }

        private static void Op_RoomReadyState(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard 1
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xe8); return; }
            // Guard 2: modo room (Status==0x02)
            if (u.Status != 0x02) { u.Disconnect(0xe9); return; }

            ushort value = ctx.P.Byte(); // local_1008 = (ushort)*param_3

            uint roomId = (uint)u.RoomId; // local_1000 = *(user+0x1460)

            using var w = new PacketWriter();
            // SendMessage: bytes APOS o subtype 0x2a (local_1002).
            w.WriteUInt32(roomId); // local_1000
            w.WriteWord(value);    // local_ffc = local_1008
            u.SendMessage(0x2a, w.ToArray());
        }

        private static void Op_ServerInfoDump(HandlerContext ctx)
        {
            // FUN_0041be60: sem guard de estado. Monta um struct "lobby" grande (zerado, 0x1010 bytes)
            // com subtype 0x77 e copia blocos de dados globais do servidor a partir de offsets fixos
            // do objeto WorldServer (this+4, this+8, this+0x24, this+0x4c).
            // Layout da resposta (SendLobby; len enviado = 0x4c = 76):
            //   [u16 subtype=0x77]            (local_1010)
            //   [int  this+0x04]              (local_100e)            -> 4 bytes
            //   [7 x int  this+0x08..0x23]    (local_100a[7])         -> 28 bytes
            //   [10 x int this+0x24..0x4b]    (local_fee[10])         -> 40 bytes
            //   [u16 this+0x4c]               (*(u16)puVar3=*(u16)puVar2) -> 2 bytes
            //   total payload (apos subtype) = 4 + 28 + 40 + 2 = 74; +2 subtype = 76 (0x4c). OK.
            // SendLobby ja escreve o subtype (= primeiro u16 do struct), entao o payload abaixo
            // contem APENAS os campos APOS o subtype.
            var w = ctx.World;

            using var pw = new PacketWriter();
            // this+0x04: int global do servidor.
            // TODO FUN_0041be60: estes campos sao estado global do WorldServer em offsets fixos
            // (this+0x04, +0x08..+0x4c). Nao ha funcao auxiliar; modelo como blocos zerados de mesmo
            // tamanho/tipo, preservando exatamente a estrutura/len (0x4c) da resposta original.
            pw.WriteInt32(0);                 // local_100e  = *(this+0x04)
            for (int i = 0; i < 7; i++)       // local_100a[7] = *(this+0x08 .. +0x23)
                pw.WriteInt32(0);
            for (int i = 0; i < 10; i++)      // local_fee[10] = *(this+0x24 .. +0x4b)
                pw.WriteInt32(0);
            pw.WriteWord(0);                  // *(u16)(this+0x4c)

            ctx.User.SendLobby(BuildLobby(0x77, pw.ToArray()));
        }

        // Helper local para prefixar o subtype no canal lobby, conforme convencao (payload comeca com [u16 subtype]).
        private static byte[] BuildLobby(ushort subtype, byte[] body)
        {
            using var w = new PacketWriter();
            w.WriteWord(subtype);
            w.WriteBytes(body);
            return w.ToArray();
        }

        private static void Op_FieldStateQuery(HandlerContext ctx)
        {
            // FUN_0041bde0: sem guard de estado. Le dois inteiros do objeto de sessao do usuario
            // (user+0x1460 e user+0x14d0) e responde no canal "plano" (SendMessage) com subtype 0x2c.
            // Struct local (FUN_0041b940 => SendMessage, len enviado = 0xc = 12):
            //   local_1004 = *(user+0x1488)   -> sessionId/seq (preenchido pelo SendMessage em C#)
            //   local_1002 = 0x2c             -> subtype
            //   local_1000 = *(user+0x1460)   -> int (estado de field; em ClientSession ~ InField raw)
            //   local_ffc  = *(user+0x14d0)   -> int
            //   total = seq(2)+subtype(2)+4+4 = 12 (0xc). OK.
            // Em C#, SendMessage ja escreve seq+subtype; passamos APENAS os bytes APOS o subtype.
            var u = ctx.User;

            // *(user+0x1460): ponteiro/handle do field corrente (0 = nenhum). Exposto como InField em
            // ClientSession (raw em user+0x1460). Reaproveito FieldId para o valor inteiro disponivel.
            int fieldRaw = u.FieldId;                 // *(user+0x1460) (golden source p/ estado de field)
            int extra = 0;                            // *(user+0x14d0): campo de sessao em offset fixo nao mapeado
            // TODO FUN_0041bde0: user+0x14d0 nao tem getter conhecido em ClientSession; mantido 0 para
            // preservar o tamanho/estrutura (dois int32) da resposta.

            using var w = new PacketWriter();
            w.WriteInt32(fieldRaw);   // local_1000 = *(user+0x1460)
            w.WriteInt32(extra);      // local_ffc  = *(user+0x14d0)
            u.SendMessage(0x2c, w.ToArray());
        }

        private static void Op_DisconnectNotText(HandlerContext ctx)
        {
            // FUN_00422270: CWorld::NetworkMessageDisconnect -> loga e desconecta o usuario com reason 1.
            // FUN_0042f280("...") = log; FUN_0041eb20(this,slot,1,'\0',0) = Disconnect(reason=1).
            // O ultimo arg ('\0',0) difere do Disconnect padrao ('\x01',1) usado nos guards, mas a
            // convencao do projeto mapeia ClientSession.Disconnect(reason) para FUN_0041eb20(this,slot,reason,..).
            Log.Info("[RW] ### CWorld::NetworkMessageDisconnect # Disconnect 1 Not Text");
            ctx.User.Disconnect(0x01);
        }

    }
}