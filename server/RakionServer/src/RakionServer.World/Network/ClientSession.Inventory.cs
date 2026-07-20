using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        /// <summary>Abre a UI e responde o resultado WorldNet 0x2C.</summary>
        internal void HandleInventoryEnter()
        {
            byte status = _inventoryUiState.Open(InventoryMutationInProgress);
            if (status != 0)
            {
                SendEncryptedFrame(LobbyFrames.InventoryEnterResult(status));
                return;
            }

            SendEncryptedFrame(LobbyFrames.InventoryEnterAck(_invHandle));
            Log.Ok("shop", "[{0}] 0x2c InventoryEnter (handle {1})", Slot, System.Convert.ToHexString(_invHandle));
            PaintInventoryOnFirstOpen();
        }

        internal void HandleInventoryLeave()
        {
            byte status = _inventoryUiState.Close(InventoryMutationInProgress);
            SendEncryptedFrame(LobbyFrames.InventoryLeaveResult(status));
            Log.Info("shop", "[{0}] 0x2d InventoryLeave status={1}", Slot, status);
        }

        private void PaintInventoryOnFirstOpen()
        {
            for (int i = 0; i < BoxItems.Count && i < 0x78; i++)
                if (BoxItems[i] != 0)
                    SendBoxAdd(BoxItems[i], (byte)i, (byte)(1 + BoxLevel[i]), BoxCount[i]);
            if (!_potionPainted) { _potionPainted = true; PaintQuickslot(); }
        }

        /// <summary>
        /// 0x31 box-add: exibe um item no grid do BOX como um "move" da celula p/ ela mesma (srcItem=0 limpa,
        /// destItem=itemId grava). Formato de 24B CAPTURADO do box-render original (forma que o cliente ja' viu)
        /// — mantido byte-a-byte; so' o CONTADOR (v2, off17) carrega 1 p/ pocao e 0 p/ gear. Gear -> frame
        /// identico ao comprovado (v2 ja' era 0); pocao -> mostra a quantidade no box (antes vinha 0). O
        /// contador NAO causa o freeze do stage (cliente novo + contador 0 ainda travava, teste 15/06).
        /// </summary>
        public void SendBoxAdd(int itemId, byte boxSlot, byte level, int count = 1)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x31);             // opcode @0
            w.WriteWord(0);                // seq @2
            w.WriteUInt32(boxSlot);        // F1 -> srcSlot
            w.WriteUInt32(0);              // F2
            w.WriteWord((ushort)(boxSlot << 8)); // F3 (off12) = [destType=0][destSlot=boxSlot]
            w.WriteWord((ushort)itemId);   // F4 (off14) -> destItem
            // off16..23 byte-fiel ao box-render capturado, EXCETO o contador (v2, off17):
            w.WriteByte(level == 0 ? (byte)1 : level);   // off16 b2 (level; callers passam 1)
            w.WriteUInt32(IsPotion(itemId) ? (uint)System.Math.Max(1, count) : 0u);   // off17 v2 = CONTADOR (poção=quantidade do stack; gear=0)
            w.WriteByte(0x39); w.WriteByte(0x40); w.WriteByte(0); // off21..23 trailing do val 0x403900 (parse real ignora)
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x31 box-add: item {1} x{2} -> box slot {3}", Slot, itemId, count, boxSlot);
        }

        /// <summary>Popula o box consolidando poções por id (1 célula + contador); gear vai 1 por célula,
        /// carregando o nível de refino (+N) e o id canônico de useriteminfo.</summary>
        public void SetBoxItems(System.Collections.Generic.IReadOnlyList<Database.StorageItem> boxGear)
        {
            BoxItems = new System.Collections.Generic.List<int>(new int[0x78]);
            BoxCount = new System.Collections.Generic.List<int>(new int[0x78]);
            BoxLevel = new System.Collections.Generic.List<int>(new int[0x78]);
            BoxRowId = new System.Collections.Generic.List<int>(new int[0x78]);
            foreach (var it in boxGear)
            {
                int cell = IsPotion(it.ItemId) ? BoxItems.IndexOf(it.ItemId) : -1;
                if (cell < 0 && it.Cell >= 0 && it.Cell < BoxItems.Count && BoxItems[it.Cell] == 0)
                    cell = it.Cell;
                if (cell < 0) cell = BoxItems.IndexOf(0);
                if (cell < 0) continue;
                if (BoxItems[cell] == it.ItemId && IsPotion(it.ItemId))
                {
                    BoxCount[cell]++;
                    continue;
                }
                BoxItems[cell] = it.ItemId;
                BoxCount[cell] = 1;
                BoxLevel[cell] = it.Level;
                BoxRowId[cell] = it.Id;
            }
        }

        public System.Threading.Tasks.Task<bool> PersistStorageLayoutAsync(int? characterId = null) =>
            _server.Db.SaveInventoryLayoutAsync(
                GameInfoId, characterId ?? ActiveCharId,
                _potionSlot, _potionRowId, BoxItems, BoxRowId);

        /// <summary>Adiciona um item ao box: poção empilha na célula existente do mesmo id; senão (e gear) ocupa
        /// a 1a célula vazia (contador 1, com nível de refino e id da linha). Retorna a célula ocupada.</summary>
        public byte AddBoxItemStacked(int itemId, int level = 0, int rowId = 0)
        {
            if (IsPotion(itemId))
            {
                int cell = BoxItems.IndexOf(itemId);
                if (cell >= 0) { BoxCount[cell]++; return (byte)cell; }   // empilha no stack existente
            }
            int free = BoxItems.IndexOf(0);
            if (free < 0) return 0;                                       // box cheio
            BoxItems[free] = itemId; BoxCount[free] = 1; BoxLevel[free] = level; BoxRowId[free] = rowId;
            return (byte)free;
        }

        public void SetBoxCell(ushort slot, int itemId, int level, int rowId)
        {
            if (slot >= BoxItems.Count || BoxItems[slot] != 0)
                throw new InvalidOperationException("Celula do box indisponivel.");
            BoxItems[slot] = itemId;
            BoxCount[slot] = 1;
            BoxLevel[slot] = level;
            BoxRowId[slot] = rowId;
        }

        public void ApplyStorageGrant(int itemId, int slot, int level, int rowId)
        {
            if (slot < 0 || slot >= BoxItems.Count)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (BoxItems[slot] == itemId && IsPotion(itemId))
            {
                BoxCount[slot]++;
                return;
            }
            SetBoxCell((ushort)slot, itemId, level, rowId);
        }

        /// <summary>Esvazia UMA célula do box (consumo do refino). Devolve (itemId, rowId itembox) p/ o domínio
        /// persistir a remoção; (0,0) se a célula estava vazia/fora de faixa. Só estado em memória.</summary>
        public (int ItemId, int RowId) ClearBoxCell(byte slot)
        {
            if (slot >= BoxItems.Count) return (0, 0);
            int itemId = BoxItems[slot];
            if (itemId == 0) return (0, 0);
            int rowId = BoxRowId[slot];
            BoxItems[slot] = 0;
            BoxCount[slot] = 0;
            BoxLevel[slot] = 0;
            BoxRowId[slot] = 0;
            return (itemId, rowId);
        }

        /// <summary>
        /// 0x31 client->server: trocar item entre o box (user+0x1e2c) e a zona ativa de 19 slots
        /// (user+0x1da4). Handler original FUN_00421870 -> FUN_0040cf10 (swap) e
        /// responde um 0x31 com os descritores de origem e destino. SEM resposta o cliente trava em
        /// "Changing slot for item". Faz o swap no modelo da sessao e responde reaproveitando o framing
        /// do 0x31 box-render (FUN_0047d1d0): descritor = [type:lo][slot:hi], item no campo seguinte.
        /// </summary>
        private async System.Threading.Tasks.Task HandleInventoryMoveAsync(byte[] data)
        {
            if (!InventoryMoveRequest.TryParse(data, out var request))
            {
                Disconnect(0x3e);
                return;
            }
            byte srcType = request.SourceType, srcSlot = request.SourceSlot;
            byte destType = request.DestinationType, destSlot = request.DestinationSlot;
            byte mutationStatus = _inventoryUiState.MutationStatus(ShopBuyInProgress);
            if (mutationStatus != 0)
            {
                SendPotionSlotMove(srcType, srcSlot, 0, destType, destSlot, 0,
                    0, 0, 0, 0, mutationStatus);
                return;
            }
            if (!TryStartInventoryMutation())
            {
                SendPotionSlotMove(srcType, srcSlot, 0, destType, destSlot, 0,
                    0, 0, 0, 0, 2);
                return;
            }
            try
            {
            if (!InventoryMoveRules.IsCoordinateValid(srcType, srcSlot) ||
                !InventoryMoveRules.IsCoordinateValid(destType, destSlot))
            {
                Disconnect(0x3e);
                return;
            }
            int srcItem = ReadCell(srcType, srcSlot);
            int destItem = ReadCell(destType, destSlot);
            int srcCnt = ReadCount(srcType, srcSlot);    // CARREGA a quantidade do stack no move (antes virava 1)
            int destCnt = ReadCount(destType, destSlot);
            int srcLvl = ReadLevel(srcType, srcSlot), destLvl = ReadLevel(destType, destSlot);   // nível de refino (+N) e
            int srcRow = ReadRowId(srcType, srcSlot), destRow = ReadRowId(destType, destSlot);   // rowId do itembox SEGUEM o item
            if (srcItem == 0)
            {
                SendPotionSlotMove(srcType, srcSlot, 0, destType, destSlot, 0,
                    0, 0, 0, 0, 3);
                return;
            }
            if (srcType == destType && srcSlot == destSlot)
            {
                SendPotionSlotMove(srcType, srcSlot, srcItem, destType, destSlot, destItem,
                    destCnt, srcCnt, srcLvl, destLvl);
                return;
            }
            if (!CanPlaceInActiveZone(destItem, srcType, srcSlot) ||
                !CanPlaceInActiveZone(srcItem, destType, destSlot))
            {
                SendPotionSlotMove(srcType, srcSlot, 0, destType, destSlot, 0,
                    0, 0, 0, 0, 4);
                Log.Warn("shop", "[{0}] 0x31 item incompatível com slot ativo", Slot);
                return;
            }
            await _storageMutationLock.WaitAsync();
            try
            {
                // Eco do move no FORMATO FIEL (21B, = FUN_00421870). O srcType cai no offset certo (param_3 do
                // FUN_0047d1d0): srcType=1 ativa o caminho do QUICKSLOT no cliente (-> FUN_004264a0), que limpa
                // /pinta o widget de pocao correto (array +0xa74). v1/v2 = CONTADOR (widget+0x194) -> tem que ser a
                // quantidade real do stack, senao a pocao "vira 1" ao mover.
                WriteCell(srcType, srcSlot, destItem); WriteCount(srcType, srcSlot, destCnt);
                WriteLevel(srcType, srcSlot, destLvl); WriteRowId(srcType, srcSlot, destRow);
                WriteCell(destType, destSlot, srcItem); WriteCount(destType, destSlot, srcCnt);
                WriteLevel(destType, destSlot, srcLvl); WriteRowId(destType, destSlot, srcRow);
                // Persiste no quickslot (itembox.qslot) SÓ poções. Gear/arma na zona ativa NÃO é deslocado p/ qslot —
                // ele pertence ao box (qslot=0) com seu nível de refino. Antes, equipar uma arma a marcava como quickslot
                // e, ao relogar, ela caía no LoadQuickslot (consolida por itemId, SEM nível) → o +N sumia.
                bool committed = GameInfoId > 0 && ActiveCharId > 0 &&
                    await _server.Db.SaveInventoryLayoutAsync(
                        GameInfoId, ActiveCharId, _potionSlot, _potionRowId, BoxItems, BoxRowId);
                if (!committed)
                {
                    WriteCell(srcType, srcSlot, srcItem); WriteCount(srcType, srcSlot, srcCnt);
                    WriteLevel(srcType, srcSlot, srcLvl); WriteRowId(srcType, srcSlot, srcRow);
                    WriteCell(destType, destSlot, destItem); WriteCount(destType, destSlot, destCnt);
                    WriteLevel(destType, destSlot, destLvl); WriteRowId(destType, destSlot, destRow);
                    SendPotionSlotMove(srcType, srcSlot, srcItem, destType, destSlot, destItem,
                        destCnt, srcCnt, srcLvl, destLvl);
                    Log.Warn("shop", "[{0}] 0x31 move não persistiu; estado restaurado", Slot);
                    return;
                }

                int newSrcItem = ReadCell(srcType, srcSlot), newDestItem = ReadCell(destType, destSlot);
                SendPotionSlotMove(srcType, srcSlot, newSrcItem, destType, destSlot, newDestItem,
                    ReadCount(destType, destSlot), ReadCount(srcType, srcSlot),
                    ReadLevel(srcType, srcSlot), ReadLevel(destType, destSlot));
                Log.Ok("shop", "[{0}] 0x31 move: ({1}:{2} item={5} +{7}) <-> ({3}:{4} item={6} +{8})",
                    Slot, srcType, srcSlot, destType, destSlot, srcItem, destItem, srcLvl, destLvl);
            }
            finally
            {
                _storageMutationLock.Release();
            }
            }
            finally
            {
                FinishInventoryMutation();
            }
        }

        private bool CanPlaceInActiveZone(int itemId, byte type, byte slot)
        {
            if (type == 0 || itemId == 0) return true;
            Database.ItemDef? definition = _server.FindItemDef(itemId);
            return definition != null &&
                   EquipmentRules.CanPlace(definition, slot, CharClass, CharLevel);
        }

        private int ReadCell(byte type, byte slot) =>
            type == 0 ? (slot < BoxItems.Count ? BoxItems[slot] : 0)
                      : (slot < _potionSlot.Length ? _potionSlot[slot] : 0);

        private void WriteCell(byte type, byte slot, int item)
        {
            if (type == 0) { if (slot < BoxItems.Count) BoxItems[slot] = item; }
            else if (slot < _potionSlot.Length) _potionSlot[slot] = item;
        }

        private int ReadCount(byte type, byte slot) =>
            type == 0 ? (slot < BoxCount.Count ? BoxCount[slot] : 0)
                      : (slot < _potionCount.Length ? _potionCount[slot] : 0);

        private void WriteCount(byte type, byte slot, int count)
        {
            if (type == 0) { if (slot < BoxCount.Count) BoxCount[slot] = count; }
            else if (slot < _potionCount.Length) _potionCount[slot] = count;
        }

        private int ReadLevel(byte type, byte slot) =>
            type == 0 ? (slot < BoxLevel.Count ? BoxLevel[slot] : 0)
                      : (slot < _potionLevel.Length ? _potionLevel[slot] : 0);

        private void WriteLevel(byte type, byte slot, int level)
        {
            if (type == 0) { if (slot < BoxLevel.Count) BoxLevel[slot] = level; }
            else if (slot < _potionLevel.Length) _potionLevel[slot] = level;
        }

        private int ReadRowId(byte type, byte slot) =>
            type == 0 ? (slot < BoxRowId.Count ? BoxRowId[slot] : 0)
                      : (slot < _potionRowId.Length ? _potionRowId[slot] : 0);

        private void WriteRowId(byte type, byte slot, int rowId)
        {
            if (type == 0) { if (slot < BoxRowId.Count) BoxRowId[slot] = rowId; }
            else if (slot < _potionRowId.Length) _potionRowId[slot] = rowId;
        }

        /// <summary>Poção empilhável/contável (faixa de item-id 12000–12999, fiel ao FUN_0040c140/FUN_0040cd70).
        /// Único ponto de verdade da faixa de poção neste domínio.</summary>
        private static bool IsPotion(int itemId) => StorageEconomyRules.IsPotion(itemId);

        /// <summary>Popula o quickslot de pocao com o que foi persistido (itembox.qslot) no login.</summary>
        public void LoadPotionSlot(System.Collections.Generic.IReadOnlyList<(int Cell, int ItemId, int Count)> entries)
        {
            for (int cell = 13; cell < _potionSlot.Length; cell++)
            {
                _potionSlot[cell] = 0;
                _potionCount[cell] = 0;
                _potionLevel[cell] = 0;
                _potionRowId[cell] = 0;
                _fieldPotionPending[cell] = 0;
            }
            foreach (var (cell, itemId, count) in entries)
                if (cell >= 0 && cell < _potionSlot.Length &&
                    (cell < 13 || InventoryEntitlementRules.IsPotionCellUnlocked(cell, PotionSlotCount)))
                {
                    _potionSlot[cell] = itemId;
                    _potionCount[cell] = count;
                }
        }

        public void LoadActiveItems(System.Collections.Generic.IReadOnlyList<Database.UserItem> items)
        {
            for (int slot = 0; slot < 13; slot++)
            {
                _potionSlot[slot] = 0;
                _potionCount[slot] = 0;
                _potionLevel[slot] = 0;
                _potionRowId[slot] = 0;
            }
            foreach (Database.UserItem item in items)
            {
                if (item.Slot >= 13) continue;
                Database.ItemDef? definition = _server.FindItemDef(item.ItemId);
                if (definition == null ||
                    !EquipmentRules.CanPlace(definition, item.Slot, CharClass, CharLevel)) continue;
                _potionSlot[item.Slot] = item.ItemId;
                _potionCount[item.Slot] = 1;
                _potionLevel[item.Slot] = item.Level;
                _potionRowId[item.Slot] = item.Id;
            }
        }

        /// <summary>Pinta uma celula do quickslot de pocao na abertura do inventario. A forma do frame
        /// PRECISA ser a do move ao vivo (origem type=0 box -> destino type=1): o caminho de ORIGEM type=1
        /// do handler do cliente (FUN_0047d1d0) escreve num array de widgets indexado pela celula SEM
        /// bounds-check e corrompia widgets -> AV no draw (rakion.bin+0x407ed). Origem = 1a celula VAZIA
        /// do box (escrita de item 0 = no-op visual), destino = a celula do quickslot.</summary>
        private void SendPotionSlotAdd(int itemId, byte cell, int count)
        {
            int empty = BoxItems.IndexOf(0);     // 1a celula vazia do box p/ usar de origem (escrita de 0 = no-op visual)
            if (empty < 0) return;               // box cheio: sem celula vazia
            SendPotionSlotMove(0, (byte)empty, 0, 1, cell, itemId, count);
            Log.Ok("shop", "[{0}] 0x31 potion-add: item {1} x{2} -> quickslot {3}", Slot, itemId, count, cell);
        }

        /// <summary>Pinta todas as células ocupadas do quickslot de poção (0x31 box->quickslot). Chamado na
        /// ENTRADA DO LOBBY (0x14, guard _potionLoginPainted) p/ a poção AUTO-RENDERIZAR no relog SEM abrir o
        /// inventário — o widget do potion-bar persiste no cliente entre lobby↔char-select. O 0x2c reusa este
        /// mesmo paint como fallback (guard _potionPainted próprio) caso o widget ainda não exista na hora do 0x14.</summary>
        public void PaintQuickslot()
        {
            for (byte cell = 13; cell < _potionSlot.Length; cell++)
                if (InventoryEntitlementRules.IsPotionCellUnlocked(cell, PotionSlotCount) &&
                    _potionSlot[cell] != 0) SendPotionSlotAdd(_potionSlot[cell], cell, _potionCount[cell]);
        }

        public void ResetFieldPotionUsage()
        {
            lock (_fieldPotionSync)
                System.Array.Clear(_fieldPotionUsed, 0, _fieldPotionUsed.Length);
        }

        public bool AuthorizeFieldPotionUse(byte cell, short requestedItemId, byte fieldMode)
        {
            lock (_fieldPotionSync)
            {
                int equippedItemId = cell < _potionSlot.Length ? _potionSlot[cell] : 0;
                int count = cell < _potionCount.Length ? _potionCount[cell] : 0;
                bool used = cell < _fieldPotionUsed.Length && _fieldPotionUsed[cell];
                var context = new FieldPotionUseContext(
                    cell, requestedItemId, fieldMode, PotionSlotCount, equippedItemId, count, used);
                if (!FieldPotionRules.CanUse(context)) return false;

                var reservation = new FieldPotionReservationState(count, _fieldPotionPending[cell]);
                if (!FieldPotionRules.TryReserve(reservation, out reservation)) return false;
                _potionCount[cell] = reservation.AvailableCount;
                _fieldPotionPending[cell] = reservation.PendingCount;
                _fieldPotionUsed[cell] = true;
                return true;
            }
        }

        public async System.Threading.Tasks.Task PersistFieldPotionUseAsync(byte cell, int itemId)
        {
            await _storageMutationLock.WaitAsync();
            try
            {
                var request = new Database.FieldPotionConsumeRequest(
                    GameInfoId, ActiveCharId, cell, itemId);
                Database.FieldPotionConsumeResult result =
                    await _server.Db.ConsumeFieldPotionAsync(request);
                if (!result.Success)
                {
                    lock (_fieldPotionSync)
                    {
                        var reservation = FieldPotionRules.Fail(new FieldPotionReservationState(
                            _potionCount[cell], _fieldPotionPending[cell]));
                        _fieldPotionPending[cell] = reservation.PendingCount;
                    }
                    Log.Error("potion", "[{0}] consumo não persistido: char={1} slot={2} item={3}",
                        Slot, ActiveCharId, cell, itemId);
                    Disconnect(0xd2);
                    return;
                }

                lock (_fieldPotionSync)
                {
                    var reservation = FieldPotionRules.Commit(new FieldPotionReservationState(
                        _potionCount[cell], _fieldPotionPending[cell]), result.RemainingCount);
                    _potionCount[cell] = reservation.AvailableCount;
                    _fieldPotionPending[cell] = reservation.PendingCount;
                }
            }
            finally
            {
                _storageMutationLock.Release();
            }
        }

        /// <summary>
        /// Resposta do move 0x31, FIEL ao layout que o handler do cliente FUN_0047d1d0 espera (= o que o
        /// worldserv original FUN_00421870 manda, 21 bytes), confirmado pela assinatura do FUN_0047d1d0:
        ///   [u16 0x31][u8 status=0][u8 srcType][u8 srcSlot][u16 newSrcItem][u8 b1][u32 v1]
        ///            [u8 destType][u8 destSlot][u16 newDestItem][u8 b2][u32 v2]
        /// O srcType no offset 3 (param_3) DECIDE o ramo: srcType=1 -> caminho do QUICKSLOT, que limpa/pinta
        /// o widget de pocao certo (array +0xa74 via FUN_004264a0). O formato antigo (24B, estilo box-render)
        /// punha [seq=00 00] nos offsets 2-3 -> o cliente lia srcType=0 SEMPRE (nunca tocava o quickslot) e os
        /// campos desalinhavam -> "duplicou" e id corrompido -> AV no draw. b = level; **v = CONTADOR da
        /// poção** (widget+0x194, lido cifrado pelo FUN_00440430) — o cliente EXIGE > 0 p/ deixar usar a poção
        /// no jogo; cada célula = 1 poção no nosso modelo -> poção manda 1, não-poção 0 (campo vira exp/limit).
        /// </summary>
        private void SendPotionSlotMove(byte srcType, byte srcSlot, int newSrcItem, byte destType,
            byte destSlot, int newDestItem, int destCnt = 1, int srcCnt = 1,
            int newSrcLvl = 0, int newDestLvl = 0, byte status = 0)
        {
            // CONTADOR (v) = widget+0x194: > 0 deixa a poção visível/utilizável; 0 a deixa "zerada"/inerte.
            // É a quantidade real da célula e acompanha o item durante o swap.
            // Não-poção = 0 (lá o campo é exp/limit). A captura de 15/06 confirmou que esse contador
            // não causava o freeze do stage; o fluxo de entrada foi fechado separadamente no 0x4B.
            uint srcCount = IsPotion(newSrcItem) ? (uint)System.Math.Max(1, srcCnt) : 0u;
            uint destCount = IsPotion(newDestItem) ? (uint)System.Math.Max(1, destCnt) : 0u;
            var source = new InventoryMoveCell(srcType, srcSlot, (ushort)newSrcItem,
                InventoryMoveMetadata(newSrcItem, newSrcLvl), srcCount);
            var destination = new InventoryMoveCell(destType, destSlot, (ushort)newDestItem,
                InventoryMoveMetadata(newDestItem, newDestLvl), destCount);
            SendEncryptedFrame(InventoryMoveFrames.Response(status, source, destination));
        }

        private static byte InventoryMoveMetadata(int itemId, int level) =>
            itemId == 0 || IsPotion(itemId) ? (byte)0 : (byte)(level <= 0 ? 1 : level);

        /// <summary>LOBBY 0x51 (level-up) ao proprio: [51 00][level][u16 levelPoint]. MESMO frame que o
        /// caminho PvP (Op_0x50_Recon) manda apos o GrantExp; sem ele o solo sobe o nivel server-side mas o
        /// cliente fica preso no nivel antigo (barra travada, ex.: 188/133) ate um relog.</summary>
        private void SendLevelUp()
        {
            using var writer = new PacketWriter();
            writer.WriteWord(0x51).WriteBytes(ProgressionResponseBodies.LevelUp(
                CharLevel, checked((ushort)CharLevelPoint)));
            SendEncryptedFrame(writer.ToArray());
            Log.Ok("level", "[{0}] 0x51 level-up -> nivel {1} (levelPoint {2}) ao cliente", Slot, CharLevel, CharLevelPoint);
        }
    }
}
