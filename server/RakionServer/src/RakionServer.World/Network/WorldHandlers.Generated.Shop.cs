using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_InventoryAllocationPoint(HandlerContext ctx) // 0x33 = SendInventoryAllocationPoint (FUN_004229f0+FUN_0040b3d0)
        {
            var u = ctx.User;
            if (!u.InField || !u.FieldSecondary) { u.Disconnect(0x43); return; }  // FUN_004229f0 guards
            if (u.Status != 0x02) { u.Disconnect(0x44); return; }
            byte statIdx = ctx.P.CanRead(1) ? ctx.P.Byte() : (byte)0;   // *param_3 = qual stat (0..9)
            if (statIdx > 9) { SendAllocResult(u, 5); return; }          // erro 5: indice invalido
            // FUN_0040b3d0: precisa de level-point OU PU bonus point (this+0x2370). Sem nenhum -> erro 3.
            if (u.CharLevelPoint == 0 && u.PowerLevelPoint == 0) { SendAllocResult(u, 3); return; }
            if (u.Stats[statIdx] >= 50) { SendAllocResult(u, 4); return; } // cap por stat (tela mostra /50)
            // aloca: stat++ e deduz do LEVEL-POINT primeiro; se zerado, do PU BONUS (powerlevelpoint).
            // Persiste: level-point em characterinfo.levelpoint; PU bonus em usergameinfo.powerlevelpoint.
            u.Stats[statIdx]++;
            bool fromLevel = u.CharLevelPoint > 0;
            if (fromLevel) u.CharLevelPoint--; else u.PowerLevelPoint--;
            if (u.ActiveCharId > 0)
            {
                if (fromLevel) _ = ctx.World.Db.AllocateStatAsync(u.ActiveCharId, statIdx);
                else _ = ctx.World.Db.AllocateStatPuAsync(u.ActiveCharId, u.GameInfoId, statIdx);
            }
            // RESPOSTA sucesso (FUN_004229f0, 10B): [0x33][0][levelpoint u16][PU bonus u16][stat u8][newStat u16]
            using var w = new PacketWriter();
            w.WriteWord(0x33);
            w.WriteByte(0);                          // status sucesso
            w.WriteWord((ushort)u.CharLevelPoint);   // levelpoint novo (this+0x1566)
            w.WriteWord((ushort)u.PowerLevelPoint);  // PU bonus points novo (this+0x2370)
            w.WriteByte(statIdx);                    // qual stat
            w.WriteWord(u.Stats[statIdx]);           // novo valor do stat alocado
            u.SendLobby(w.ToArray());
            Log.Ok("shop", "[{0}] 0x33 alloc stat {1} -> {2} ({3}: lvlpts={4} puBonus={5})",
                u.Slot, statIdx, u.Stats[statIdx], fromLevel ? "level" : "PU", u.CharLevelPoint, u.PowerLevelPoint);
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
            var def = ctx.World.Items.Find(itemId);
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
            // SET (type 10) = bundle: a COMPRA entrega as 6 peças de gear (membros), NÃO o item-bundle. Demais
            // itens: concede o próprio. (O mesmo desempacote roda no login como rede de segurança.)
            var grantItems = ctx.World.Items.ExpandSetMembers(itemId);
            if (grantItems.Count == 0) grantItems = new System.Collections.Generic.List<int> { itemId };
            // ocupa a 1a célula VAZIA da grade esparsa (120, 0=vazia) p/ CADA item concedido; registra (item,slot)
            // p/ o delta da resposta. Só GEAR (displayable) entra no grid; não-gear persiste mas não pinta.
            var granted = new System.Collections.Generic.List<(int Item, byte Slot)>(grantItems.Count);
            foreach (var it in grantItems)
            {
                byte slot = ctx.World.Items.IsBoxDisplayable(it) ? u.AddBoxItemStacked(it) : (byte)0; // poção empilha; gear -> célula nova
                granted.Add((it, slot));
            }
            // NAO adiciona em u.Items (= useriteminfo/APARENCIA equipada): o item de box NUNCA vai pro corpo 3D (crash).

            Log.Ok("shop", "[{0}] BUY item {1} type={2} por {3} {4} (saldo {5}->{6}) char={7} -> {8} peça(s){9}",
                u.Slot, itemId, def.Type, price, payGold ? "GOLD" : "CASH", balance, newBalance, u.ActiveCharId,
                granted.Count, grantItems.Count > 1 ? $" (set {itemId} desempacotado na compra)" : "");

            // PERSISTE em background (handler e sync; DB e async). Reverte saldo em memoria se falhar.
            int gameInfoId = u.GameInfoId; string acct = u.UserId;
            var db = ctx.World.Db; var world = ctx.World; bool gold = payGold; int p = price;
            System.Threading.Tasks.Task.Run(async () =>
            {
                bool ok = true;
                try
                {
                    if (gold) { if (gameInfoId > 0) await db.AddGoldAsync(gameInfoId, -p); else ok = false; }
                    else { if (!string.IsNullOrEmpty(acct)) await db.AddCashAsync(acct, -p); else ok = false; }
                    if (ok && gameInfoId > 0)
                        foreach (var g in granted)
                        {
                            int rowId = await db.InsertItemBoxAsync(gameInfoId, g.Item, 0);   // BOX (itembox)
                            if (rowId <= 0) { ok = false; break; }
                            // conecta a célula do box à linha do itembox -> o refino (UPDATE level / DELETE por id exato)
                            // passa a persistir; sem isto os itens comprados tinham rowId=0 e o relog "desfazia" o upgrade.
                            if (world.Items.IsBoxDisplayable(g.Item) && g.Slot < u.BoxRowId.Count) u.BoxRowId[g.Slot] = rowId;
                        }
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
            w.WriteByte(0);                          // c1 = 0 (sem delta de loadout +0x1b78)
            w.WriteByte((byte)granted.Count);        // c2 = nº de itens novos (set = 6 peças; senão 1)
            foreach (var g in granted) w.WriteUInt32((uint)g.Item);  // block2a: itemIds (c2*u32)
            foreach (var g in granted) w.WriteByte(g.Slot);          // block2b: slots (c2*u8)
            w.WriteByte(0);                          // c3 = 0
            u.SendLobby(w.ToArray());
            // BOX render: o grid so' pinta em menu de loja (FUN_0047d1d0/FUN_004774e0 gate 0x19/1a/1b). A COMPRA
            // acontece com o menu garantidamente em loja — UNICO momento confiavel. Entao re-pinto TODO o box
            // (0x31 de cada item, slot=indice), nao so' o comprado. Assim os itens PERSISTIDOS do itembox (que
            // foram mandados antes em menu errado e ficaram invisiveis) aparecem junto com o novo. (Log provou:
            // o comprado ia pro slot N=BoxItems.Count e so' ele pintava; os 0..N-1 ficavam invisiveis.)
            for (int i = 0; i < u.BoxItems.Count && i < 0x78; i++) if (u.BoxItems[i] != 0) u.SendBoxAdd(u.BoxItems[i], (byte)i, (byte)(1 + u.BoxLevel[i]), u.BoxCount[i]); // 1 + nível de refino

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

        private static void Op_GroupMemberInfo(HandlerContext ctx) // 0x2f = SendInventorySell([u8 slot]) (engine 0x361917c0)
        {
            var u = ctx.User;
            // Guard: precisa estar no field e com field secundario ativo (this_00+0x1460 != 0 && this_00+0x14a4 != 0)
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x39); return; }
            // Guard: status precisa ser '\x02' (room)
            if (u.Status != 0x02) { u.Disconnect(0x3a); return; }

            // payload: [byte slot]. O 0x2f e' SEMPRE a VENDA do slot do box: o engine SendInventorySell
            // (0x361917c0) manda [0x2f][slot] e o worldserv (FUN_004215a0->FUN_0040cd70) vende. Antes o servidor
            // so' reenviava a lista (0x15) e NUNCA tirava o item nem creditava gold ("vendi e nada aconteceu").
            byte slotArg = ctx.P.Byte();
            if (slotArg >= 0x78) { u.Disconnect(0x3b); return; }

            if (slotArg < u.BoxItems.Count && u.BoxItems[slotArg] != 0) { SellBoxSlot(ctx, slotArg); return; }
            SendInventoryList(ctx, slotArg);   // slot vazio/fora do box -> so' a lista (sem vender)
        }

        /// <summary>VENDA de um item do box (0x2f): credita o preco de venda em gold, remove o item do armazem
        /// e repinta o box sem ele + saldo ao vivo. Espelha o handler de COMPRA (Op_RoomMemberQuery), invertido.</summary>
        private static void SellBoxSlot(HandlerContext ctx, byte slot)
        {
            var u = ctx.User;
            int itemId = u.BoxItems[slot];
            int sellGold = SellPriceOf(ctx.World.Items.Find(itemId), itemId);   // preco fiel ao iteminfo

            u.BoxItems[slot] = 0;                                  // esvazia a celula (grade esparsa; SEM shift -> nao desloca as outras)
            uint prev = u.Gold; u.Gold = prev + (uint)sellGold;
            Log.Ok("shop", "[{0}] SELL slot {1} item {2} por {3} gold (saldo {4}->{5})", u.Slot, slot, itemId, sellGold, prev, u.Gold);

            // persiste em background: credita gold + remove UMA linha do itembox com esse itemId
            int gameInfoId = u.GameInfoId; var db = ctx.World.Db; int g = sellGold; int iid = itemId; int uslot = u.Slot;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try { if (gameInfoId > 0) { await db.AddGoldAsync(gameInfoId, g); await db.DeleteItemBoxByItemAsync(gameInfoId, iid); } }
                catch (System.Exception ex) { Log.Error("shop", "[{0}] persist SELL item {1}: {2}", uslot, iid, ex.Message); }
            });

            // repinta: limpa SO' a celula vendida (grade esparsa -> as outras nao mudam de lugar)
            u.SendBoxAdd(0, slot, 1);

            // saldo ao vivo (0x2e count=0) -> HUD de gold (igual a compra; FUN_004774e0 grava AccountInfo+0x64)
            using var gw = new PacketWriter();
            gw.WriteWord(0x2e); gw.WriteByte(0);
            gw.WriteUInt32(u.Gold); gw.WriteUInt32(u.Cash);
            gw.WriteByte(0); gw.WriteByte(0);
            u.SendLobby(gw.ToArray());
        }

        /// <summary>Preco de venda (gold) fiel ao worldserv (FUN_0040cd70 -> FUN_0040a810 -> round FUN_004365a8):
        /// o item devolve uma FRACAO do preco de loja, ARREDONDADA — nao o preco cheio. Constantes (double)
        /// lidas do binario worldserv.exe: loja de GOLD (shop=1) devolve round(gold * 0.4) [@0x447678];
        /// loja de CASH (shop=2) devolve round(cash * 1.5) creditado em gold [@0x447670]. Pocoes (12xxx,
        /// zeradas no FUN_0040cd70) e itens fora de loja (shop=0) devolvem 0.
        /// (Os dados reais sempre dao multiplos exatos -> empate .5 nao ocorre; round e' so' por fidelidade.)</summary>
        private static int SellPriceOf(RakionServer.World.Database.ItemDef? def, int itemId)
        {
            if (def == null || (itemId >= 12000 && itemId <= 12999)) return 0;
            double price = def.Shop == 1 ? def.Gold * 0.4
                         : def.Shop == 2 ? def.Cash * 1.5
                         : 0.0;
            return (int)System.Math.Round(price, System.MidpointRounding.AwayFromZero);
        }

        /// <summary>Lista do inventario (0x15) — fallback do 0x2f quando o slot esta vazio/fora do box.</summary>
        private static void SendInventoryList(HandlerContext ctx, byte slotArg)
        {
            var u = ctx.User;
            var items = u.Items ?? new System.Collections.Generic.List<RakionServer.World.Database.UserItem>();
            // escalares do slot consultado (FUN_0040cd70): se o slot nao casa, ficam 0 (UI tolera)
            ushort groupId = 0; uint field1 = 0; uint field2 = 0; uint field3 = 0; byte b1 = 0;
            foreach (var it in items)
            {
                if (it.Slot != slotArg) continue;
                groupId = (ushort)it.ItemSn; field2 = (uint)it.ItemId; b1 = it.Level; field3 = (uint)it.LimitTime; break;
            }
            byte count2 = (byte)System.Math.Min(items.Count, 0x77);
            using var blk2 = new PacketWriter();
            for (int i = 0; i < count2; i++) blk2.WriteUInt32((uint)items[i].ItemId);
            for (int i = 0; i < count2; i++) blk2.WriteByte((byte)i);
            byte[] block2 = blk2.ToArray();

            byte[] payload = BuildGroupMemberInfo(u, slotArg, groupId, field1, field2, field3, b1, 0, count2, System.Array.Empty<byte>(), block2);
            using var fw = new PacketWriter();
            fw.WriteWord(0x15); fw.WriteWord(0); fw.WriteBytes(payload);
            u.SendLobby(fw.ToArray());
            Log.Ok("shop", "[{0}] 0x2f lista (0x15): {1} itens (slot {2} vazio, sem venda)", u.Slot, count2, slotArg);
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
    }
}
