using System;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        /// <summary>
        /// 0x12 InventoryEnter ack (FUN_00420de0). Apos o cabecalho [u16 seq][u16 0x12] (SendMessage):
        /// [u32 user+0x1460][u32 user+0x14a4]. E' o que faz o cliente TRANSICIONAR p/ a tela de inventario
        /// (menu state 0x19/0x1a/0x1b); sem ele o cliente volta pro char-select e fecha.
        /// </summary>
        public void SendInventoryEnterAck(byte[] reqBody)
        {
            // FORMATO REAL (captura do worldserv ORIGINAL, mitm inventario→Previous 2026-06-11):
            //   W->C 0x2c = [2c 00][00][handle:4][00 01][00 12][00]  (12B)
            // O original NAO ecoa o body do cliente — reflete o HANDLE de sessao (bytes 13..16 do 0x0C).
            // Ecoar o body (FFFFFFFF8F21347C) deixava o cliente sem reconhecer o estado do inventario ->
            // ficava em polling de 0x2d/0x36 (telas sobrepostas) e o Previous caia no char-select.
            using var w = new PacketWriter();
            w.WriteWord(0x2c);                 // 2c 00
            w.WriteByte(0);                    // 00
            w.WriteBytes(_invHandle);          // handle de sessao (4B)
            w.WriteByte(0); w.WriteByte(1);    // 00 01
            w.WriteByte(0); w.WriteByte(0x12); // 00 12
            w.WriteByte(0);                    // 00
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x2c enter-ack (handle {1})", Slot, System.Convert.ToHexString(_invHandle));
        }

        /// <summary>
        /// 0x2d ACK curto (FUN_00420f10, path "else": FUN_004038e0 subtype 3 = [0x2d][status]). O
        /// worldserv original responde ISTO em todo 0x2d que NÃO seja a 1a list (FUN_0040c960 devolve
        /// 1 quando user+0x144c==0, ou 2 quando ==loja — sem remontar a lista). É o "nada mudou" que o
        /// cliente espera p/ concluir a saída do inventário e VOLTAR AO LOBBY. Remandar a lista 0x13 aqui
        /// fazia o cliente reprocessar o grid de widgets e cair no char-select no Previous.
        /// </summary>
        public void SendInventoryAck(byte status)
        {
            // FORMATO REAL (captura do original): W->C 0x2d = [2d 00][00 00][2c 00 00][handle:4][00] (12B).
            // Ecoa o handle de sessao (igual ao 0x2c). Antes mandavamos [2d 00][00 00][status] (5B) — o
            // cliente nao reconhecia, ficava em polling e o Previous nao concluia a saida p/ a lista de salas.
            using var w = new PacketWriter();
            w.WriteWord(0x2d);            // 2d 00
            w.WriteWord(0);              // 00 00
            w.WriteWord(0x2c);           // 2c 00
            w.WriteByte(0);              // 00
            w.WriteBytes(_invHandle);    // handle de sessao (4B)
            w.WriteByte(0);              // 00
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x2d ack (handle {1}) — fiel ao original", Slot, System.Convert.ToHexString(_invHandle));
        }

        /// <summary>
        /// 0x31 box-add: exibe um item no grid do BOX. O handler do cliente (FUN_0047d1d0) e' um MOVE com
        /// descritor de ORIGEM e de DESTINO, cada um escrevendo na celula = slot do descritor:
        ///   origem  (srcType==0 box): grava srcItem  na celula srcSlot   (call 0x47d3c9)
        ///   destino (destType==0 box): grava destItem na celula destSlot (call 0x47d740)
        /// Layout apos [u16 0x31][u16 seq]: [u32 srcDesc][u32 destDesc][u16 srcItem][u16 destItem][u32 lvl][u32 val].
        /// Descritor = slot no byte baixo, type no byte 1 (0 = box; slot &lt; 256 mantem type=0). Confirmado via frida.
        /// FIX overwrite: destDesc deve ser boxSlot (estava 0 -> jogava todo item na celula 0). srcItem=0 limpa a
        /// celula srcSlot=boxSlot, e logo em seguida o destino grava destItem=itemId na MESMA celula boxSlot.
        /// </summary>
        public void SendBoxAdd(int itemId, byte boxSlot, byte level)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x31);             // opcode @0
            w.WriteWord(0);                // seq @2
            w.WriteUInt32(boxSlot);        // F1 (confirmado -> srcSlot)
            w.WriteUInt32(0);              // F2 (confirmado -> param_7; revertido p/ 0)
            w.WriteWord((ushort)(boxSlot << 8)); // F3 = [destType:lo=0 (box)][destSlot:hi=boxSlot] -- byte baixo era destType
            w.WriteWord((ushort)itemId);   // F4 (confirmado -> destItem)
            w.WriteUInt32(level == 0 ? 1u : level); // level
            w.WriteUInt32(0x00403900);     // val (copiado da captura do 0x31 box-render do original)
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x31 box-add: item {1} -> box slot {2}", Slot, itemId, boxSlot);
        }

        /// <summary>
        /// 0x31 client->server: mover/trocar item entre o BOX (armazem, user+0x1e2c) e o POTION SLOT
        /// (quickslot de pocao, user+0x1da4). Handler original FUN_00421870 -> FUN_0040cf10 (swap) e
        /// responde um 0x31 com os descritores de origem e destino. SEM resposta o cliente trava em
        /// "Changing slot for item". Faz o swap no modelo da sessao e responde reaproveitando o framing
        /// do 0x31 box-render (FUN_0047d1d0): descritor = [type:lo][slot:hi], item no campo seguinte.
        /// </summary>
        private void HandlePotionSlot(byte[] data)
        {
            if (data.Length < 4) return;
            byte srcType = data[0], srcSlot = data[1], destType = data[2], destSlot = data[3];
            // O 0x31 é o MOVE genérico de item; só sabemos reconciliar box(0) <-> quickslot(1).
            // Qualquer outro descritor (ex.: arrastar um item do box para a LOJA = venda) NÃO pode
            // tocar o modelo do box/quickslot nem persistir — antes corrompia itembox.qslot e
            // "sumia" o item sem creditar gold. Loga o frame cru (p/ RE do destino real) e ignora.
            if (srcType > 1 || destType > 1)
            {
                Log.Warn("shop", "[{0}] 0x31 move fora de box/quickslot (src {1}:{2} dest {3}:{4}) — ignorado. raw={5}",
                    Slot, srcType, srcSlot, destType, destSlot, System.Convert.ToHexString(data));
                return;
            }
            int srcItem = ReadCell(srcType, srcSlot);
            int destItem = ReadCell(destType, destSlot);
            WriteCell(srcType, srcSlot, destItem);   // swap origem <-> destino
            WriteCell(destType, destSlot, srcItem);
            SendPotionSlotMove(srcType, srcSlot, destItem, destType, destSlot, srcItem);
            if (GameInfoId > 0) _ = _server.Db.SaveQuickslotAsync(GameInfoId, _potionSlot);  // persiste box<->quickslot
            Log.Ok("shop", "[{0}] 0x31 potion-slot: ({1}:{2}) <-> ({3}:{4}) item={5}",
                Slot, srcType, srcSlot, destType, destSlot, srcItem);
        }

        private int ReadCell(byte type, byte slot) =>
            type == 0 ? (slot < BoxItems.Count ? BoxItems[slot] : 0)
                      : (slot < _potionSlot.Length ? _potionSlot[slot] : 0);

        private void WriteCell(byte type, byte slot, int item)
        {
            if (type == 0) { if (slot < BoxItems.Count) BoxItems[slot] = item; }
            else if (slot < _potionSlot.Length) _potionSlot[slot] = item;
        }

        /// <summary>Popula o quickslot de pocao com o que foi persistido (itembox.qslot) no login.</summary>
        public void LoadPotionSlot(System.Collections.Generic.IReadOnlyList<(int Cell, int ItemId)> entries)
        {
            System.Array.Clear(_potionSlot);
            foreach (var (cell, itemId) in entries)
                if (cell >= 0 && cell < _potionSlot.Length) _potionSlot[cell] = itemId;
        }

        /// <summary>Pinta uma celula do quickslot de pocao na abertura do inventario. A forma do frame
        /// PRECISA ser a do move ao vivo (origem type=0 box -> destino type=1): o caminho de ORIGEM type=1
        /// do handler do cliente (FUN_0047d1d0) escreve num array de widgets indexado pela celula SEM
        /// bounds-check e corrompia widgets -> AV no draw (rakion.bin+0x407ed). Origem = 1a celula VAZIA
        /// do box (escrita de item 0 = no-op visual), destino = a celula do quickslot.</summary>
        private void SendPotionSlotAdd(int itemId, byte cell)
        {
            if (BoxItems.Count >= 0x78) return;  // box cheio: sem celula vazia p/ usar de origem
            SendPotionSlotMove(0, (byte)BoxItems.Count, 0, 1, cell, itemId);
            Log.Ok("shop", "[{0}] 0x31 potion-add: item {1} -> quickslot {2}", Slot, itemId, cell);
        }

        /// <summary>
        /// Resposta do move (0x31), no mesmo layout do box-render: [u16 0x31][u16 seq][u32 srcDesc =
        /// slot|type&lt;&lt;8|novoItemOrigem&lt;&lt;16][u32 0][u16 destDesc = type|slot&lt;&lt;8][u16 novoItemDestino]
        /// [u32 lvl][u32 val]. O cliente grava cada item na celula do seu array (type 0 = box, 1 = potion slot).
        /// </summary>
        private void SendPotionSlotMove(byte srcType, byte srcSlot, int newSrcItem, byte destType, byte destSlot, int newDestItem)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x31);
            w.WriteWord(0);
            w.WriteUInt32((uint)srcSlot | ((uint)srcType << 8) | ((uint)(ushort)newSrcItem << 16));
            w.WriteUInt32(0);
            w.WriteWord((ushort)(destType | (destSlot << 8)));
            w.WriteWord((ushort)newDestItem);
            w.WriteUInt32(1);
            w.WriteUInt32(0x00403900);
            SendEncryptedFrame(w.ToArray());
        }

        /// <summary>
        /// 0x34 Buy Power User: CONCEDE o PU (o original validava no cash-shop online, offline) e responde o
        /// frame que DESTRAVA o popup "Buying Power User". O frame de sucesso real e' 0x17 no canal field
        /// (irreplicavel sem o serverSeq do cash-server). Usamos o frame [34 00][04][handle][00][status][00][17 00]
        /// — a estrutura destrava; a MENSAGEM depende do status (2 = "6 meses"). status=0 -> tenta fechar sem erro.
        /// </summary>
        private void HandleBuyPowerUser()
        {
            var cfg = _server.PuConfig;
            uint price = (uint)cfg.Price;
            int bonus = cfg.BonusPoints;
            bool granted = false;
            if (Cash >= price && GameInfoId > 0 && Game != null)
            {
                // estado anterior p/ rollback se a persistencia falhar (espelha a compra em Op_RoomMemberQuery)
                uint prevCash = Cash; uint prevPoints = PowerLevelPoint;
                bool prevPu = PuActive; bool prevExpBonus = ExpBonusActive;
                Cash -= price;
                PowerLevelPoint += (uint)bonus;
                PuActive = true; ExpBonusActive = true;   // PU passa a valer já nesta sessão (sem relog)
                granted = true;
                Log.Ok("shop", "[{0}] Buy Power User: -{1} cash, +{2} PU bonus (total {3}), +{4}d", Slot, price, bonus, PowerLevelPoint, cfg.DurationDays);

                // PERSISTE em background com rollback: se cash OU power-user falharem, reverte o estado em memoria.
                string acct = Game.Name; int gi = GameInfoId; int dur = cfg.DurationDays; int b = bonus;
                var db = _server.Db;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    bool ok = true;
                    try { await db.AddCashAsync(acct, -(int)price); await db.GrantPowerUserAsync(gi, b, dur); }
                    catch (Exception ex) { ok = false; Log.Error("shop", "[{0}] persist Buy Power User: {1}", Slot, ex.Message); }
                    if (!ok)
                    {
                        Cash = prevCash; PowerLevelPoint = prevPoints; PuActive = prevPu; ExpBonusActive = prevExpBonus;
                        Log.Warn("shop", "[{0}] persist Buy Power User FALHOU -> estado revertido (cash/pontos/PU)", Slot);
                    }
                });
            }
            // status 2 -> o cliente exibe a mensagem 641 do language.txt, que PATCHAMOS no DataSetup.xfs
            // de "...6 months in advance" p/ "Power User purchased! Relog to see your bonus points."
            SendPowerUserResponse(0x02);
            if (granted) SendPowerUserBonusLive();   // sobe o contador de PU Bonus Points SEM relog
        }

        /// <summary>
        /// Empurra ao cliente o novo total de PU Bonus Points logo apos a compra, SEM relog. Reaproveita a
        /// resposta 0x33 de alocacao (OnRecvInventoryAllocationPoint, FUN_0047dbb0): no status 0 ela grava
        /// account_info+0x58 = PU bonus (e o levelpoint) INCONDICIONALMENTE, e so' atualiza o widget se a UI
        /// de inventario estiver aberta (guard menu 0x19/0x1a/0x1b) — mesmo padrao seguro do BuyPowerUser.
        /// stat=0x0a (fora de 0..9): o switch do handler cai no default e PULA a escrita de stat -> no-op de
        /// stat; so' o contador de PU/levelpoint sobe. Forma de frame ja validada (a alocacao real a usa).
        /// </summary>
        private void SendPowerUserBonusLive()
        {
            using var w = new PacketWriter();
            w.WriteWord(0x33);
            w.WriteByte(0);                          // status sucesso
            w.WriteWord((ushort)CharLevelPoint);     // levelpoint (valor atual = no-op)
            w.WriteWord((ushort)PowerLevelPoint);    // PU Bonus Points NOVO -> account_info+0x58
            w.WriteByte(0x0a);                        // stat fora de 0..9 -> default: NAO escreve stat
            w.WriteWord(0);                          // newStat ignorado no caminho default
            SendLobby(w.ToArray());
            Log.Ok("shop", "[{0}] 0x33 push PU bonus live -> {1} (sem relog)", Slot, PowerLevelPoint);
        }

        private void SendPowerUserResponse(byte status)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x34);
            w.WriteByte(0x04);
            w.WriteByte(_invHandle[0]);
            w.WriteByte(0x00);
            w.WriteByte(_invHandle[2]);
            w.WriteByte(_invHandle[3]);
            w.WriteByte(0x00);
            w.WriteByte(status);  // 2 = "6 meses"; 0 = tentativa de fechar sem erro
            w.WriteByte(0x00);
            w.WriteWord(0x17);
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x34 power-user resposta (status={1})", Slot, status);
        }

        /// <summary>
        /// Credita o resultado do STAGE SOLO (0x53, FUN_00425010): parse [idx u8][cfgA u8][cfgB u8]
        /// [cfgB x u16 mapSlots][exp u32][gold u32]. Mesmo teto anti-cheat do caminho PvP (0x50).
        /// O level-up/persistencia ficam no WorldServer.GrantExp (curva classlevelinfo).
        /// </summary>
        private void CreditSoloResult(byte[] data)
        {
            try
            {
                var p = new PacketReader(data);
                p.Byte();                      // idx
                p.Byte();                      // cfgA
                byte cfgB = p.Byte();          // cfgB = qtde de u16 a pular
                for (int i = 0; i < cfgB && p.Remaining >= 2; i++) p.UInt16();
                uint exp = p.CanRead(4) ? p.UInt32() : 0;
                uint gold = p.CanRead(4) ? p.UInt32() : 0;
                const uint Max = 1_000_000;    // teto de sanidade (= ValidateGamePoints do 0x50)
                if (exp > Max || gold > Max)
                {
                    Log.Warn("field", "[{0}] 0x53 solo: Wrong Game Point! Exp:{1} Gold:{2} — ignorado", Slot, exp, gold);
                    return;
                }
                exp = BonusExp(exp); gold = BonusGold(gold);   // bônus de PU (pu_config) sobre o valor base
                Gold += gold;
                if (gold > 0 && GameInfoId > 0) _ = _server.Db.AddGoldAsync(GameInfoId, (int)gold);
                _server.GrantExp(this, exp);
                Log.Ok("field", "[{0}] 0x53 stage clear solo — exp={1} gold={2} creditados", Slot, exp, gold);
            }
            catch (Exception ex) { Log.Warn("field", "[{0}] 0x53 solo parse: {1}", Slot, ex.Message); }
        }
    }
}
