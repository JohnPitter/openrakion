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

        /// <summary>Popula o box consolidando poções por id (1 célula + contador); gear vai 1 por célula.</summary>
        public void SetBoxItems(System.Collections.Generic.List<int> boxGear)
        {
            BoxItems = new System.Collections.Generic.List<int>(new int[0x78]);
            BoxCount = new System.Collections.Generic.List<int>(new int[0x78]);
            foreach (var it in boxGear) AddBoxItemStacked(it);
        }

        /// <summary>Adiciona um item ao box: poção empilha na célula existente do mesmo id; senão (e gear) ocupa
        /// a 1a célula vazia (contador 1). Retorna a célula ocupada.</summary>
        public byte AddBoxItemStacked(int itemId)
        {
            if (IsPotion(itemId))
            {
                int cell = BoxItems.IndexOf(itemId);
                if (cell >= 0) { BoxCount[cell]++; return (byte)cell; }   // empilha no stack existente
            }
            int free = BoxItems.IndexOf(0);
            if (free < 0) return 0;                                       // box cheio
            BoxItems[free] = itemId; BoxCount[free] = 1;
            return (byte)free;
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
            int srcCnt = ReadCount(srcType, srcSlot);    // CARREGA a quantidade do stack no move (antes virava 1)
            int destCnt = ReadCount(destType, destSlot);
            // Eco do move no FORMATO FIEL (21B, = FUN_00421870). O srcType cai no offset certo (param_3 do
            // FUN_0047d1d0): srcType=1 ativa o caminho do QUICKSLOT no cliente (-> FUN_004264a0), que limpa
            // /pinta o widget de pocao correto (array +0xa74). v1/v2 = CONTADOR (widget+0x194) -> tem que ser a
            // quantidade real do stack, senao a pocao "vira 1" ao mover.
            if (srcItem != 0 && srcItem == destItem && IsPotion(srcItem))
            {
                // MESMA pocao nos dois lados -> FUNDE os stacks (destino soma, origem esvazia)
                int merged = srcCnt + destCnt;
                WriteCell(srcType, srcSlot, 0);       WriteCount(srcType, srcSlot, 0);
                WriteCell(destType, destSlot, destItem); WriteCount(destType, destSlot, merged);
                SendPotionSlotMove(srcType, srcSlot, 0, destType, destSlot, destItem, destCnt: merged, srcCnt: 0);
            }
            else
            {
                // SWAP: origem recebe (destItem, destCnt), destino recebe (srcItem, srcCnt) — contadores junto
                WriteCell(srcType, srcSlot, destItem); WriteCount(srcType, srcSlot, destCnt);
                WriteCell(destType, destSlot, srcItem); WriteCount(destType, destSlot, srcCnt);
                SendPotionSlotMove(srcType, srcSlot, destItem, destType, destSlot, srcItem, destCnt: srcCnt, srcCnt: destCnt);
            }
            if (GameInfoId > 0) _ = _server.Db.SaveQuickslotAsync(GameInfoId, _potionSlot);  // persiste box<->quickslot (linhas por itemId)
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

        private int ReadCount(byte type, byte slot) =>
            type == 0 ? (slot < BoxCount.Count ? BoxCount[slot] : 0)
                      : (slot < _potionCount.Length ? _potionCount[slot] : 0);

        private void WriteCount(byte type, byte slot, int count)
        {
            if (type == 0) { if (slot < BoxCount.Count) BoxCount[slot] = count; }
            else if (slot < _potionCount.Length) _potionCount[slot] = count;
        }

        /// <summary>Poção empilhável/contável (faixa de item-id 12000–12999, fiel ao FUN_0040c140/FUN_0040cd70).
        /// Único ponto de verdade da faixa de poção neste domínio.</summary>
        private static bool IsPotion(int itemId) => itemId >= 12000 && itemId <= 12999;

        /// <summary>Popula o quickslot de pocao com o que foi persistido (itembox.qslot) no login.</summary>
        public void LoadPotionSlot(System.Collections.Generic.IReadOnlyList<(int Cell, int ItemId, int Count)> entries)
        {
            System.Array.Clear(_potionSlot);
            System.Array.Clear(_potionCount);
            foreach (var (cell, itemId, count) in entries)
                if (cell >= 0 && cell < _potionSlot.Length) { _potionSlot[cell] = itemId; _potionCount[cell] = count; }
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
            for (byte cell = 0; cell < _potionSlot.Length; cell++)
                if (_potionSlot[cell] != 0) SendPotionSlotAdd(_potionSlot[cell], cell, _potionCount[cell]);
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
        private void SendPotionSlotMove(byte srcType, byte srcSlot, int newSrcItem, byte destType, byte destSlot, int newDestItem, int destCnt = 1, int srcCnt = 1)
        {
            // CONTADOR (v) = widget+0x194: > 0 deixa a poção visível/utilizável; 0 a deixa "zerada"/inerte.
            // É a QUANTIDADE REAL do stack (não mais 1 fixo) -> mover/fundir preserva o total no widget.
            // Não-poção = 0 (lá o campo é exp/limit). O contador NÃO causa o freeze do stage (independe
            // dele; cliente NOVO + contador 0 ainda travava, teste 15/06 -> RE pendente).
            uint srcCount  = IsPotion(newSrcItem)  ? (uint)System.Math.Max(1, srcCnt)  : 0u;
            uint destCount = IsPotion(newDestItem) ? (uint)System.Math.Max(1, destCnt) : 0u;
            using var w = new PacketWriter();
            w.WriteWord(0x31);                  // off0  msgType (dispatch do cliente)
            w.WriteByte(0);                     // off2  status = 0 (sucesso; != 0 mostraria erro)
            w.WriteByte(srcType);               // off3  srcType (0=box, 1=quickslot) — DECIDE o ramo
            w.WriteByte(srcSlot);               // off4  srcSlot
            w.WriteWord((ushort)newSrcItem);    // off5  novo item na origem (0 = celula esvaziada)
            w.WriteByte(0);                     // off7  b1 (level)
            w.WriteUInt32(srcCount);            // off8  v1 = CONTADOR da poção na origem (widget+0x194)
            w.WriteByte(destType);              // off12 destType
            w.WriteByte(destSlot);              // off13 destSlot
            w.WriteWord((ushort)newDestItem);   // off14 novo item no destino
            w.WriteByte(0);                     // off16 b2 (level)
            w.WriteUInt32(destCount);           // off17 v2 = CONTADOR da poção no destino (widget+0x194)
            SendEncryptedFrame(w.ToArray());
        }

        /// <summary>
        /// 0x73 = mudar slot de item no BOX (worldserv FUN_00421a50 -> FUN_0040c140): EMPILHAR pocoes
        /// iguais. Valida que origem e destino sao pocoes (12000-12999) e responde a lista do box
        /// (subtype 0x27); senao responde o erro curto [0x73][status]. FUN_0040c140 NAO muta o box (so'
        /// le + computa campos que p/ pocao sao 0: deltas e soma de exp@0x1f94) — entao tambem nao
        /// mexemos no modelo: o cliente no-GG ja fez a juncao local e so' espera o ack p/ tirar o aviso
        /// "Changing item slot". Sem resposta o 0x73 caia no catch-all 'em campo (sem resp)' e travava.
        /// payload: [u8 srcSlot][u8 destSlot] (o resto e' widget do cliente, ignorado como no original).
        /// </summary>
        private void HandleItemSlotChange(byte[] data)
        {
            if (data.Length < 2) { SendItemSlotError(3); return; }            // frame curto -> destrava com erro
            byte srcSlot = data[0], destSlot = data[1];
            if (srcSlot >= 0x78 || destSlot >= 0x78) { SendItemSlotError(3); return; } // bounds (orig DISC 0xe0/0xe1)

            int srcItem  = srcSlot  < BoxItems.Count ? BoxItems[srcSlot]  : 0;
            int destItem = destSlot < BoxItems.Count ? BoxItems[destSlot] : 0;
            // FUN_0040c140: sucesso so' quando origem e destino sao pocoes (12000-12999) da mesma categoria.
            bool stackablePotions = IsPotion(srcItem) && IsPotion(destItem);
            if (!stackablePotions)
            {
                SendItemSlotError(3);   // nao-empilhavel -> aviso some com "erro" (fiel ao retorno 3/4 do original)
                Log.Ok("shop", "[{0}] 0x73 item-slot {1}<-{2} nao-empilhavel (src={3} dest={4}) -> erro 3",
                    Slot, destSlot, srcSlot, srcItem, destItem);
                return;
            }
            // NAO mutamos o box: FUN_0040c140 tambem nao muta (so' valida + computa) e ESTE cliente NAO junta
            // as pocoes ao receber o ack — mantem as DUAS celulas. Esvaziar a origem aqui descasava do cliente
            // (ele seguia mostrando a pocao na origem) -> a pocao "sumia" no nosso modelo. So' valida e confirma.
            SendItemSlotStackAck(srcSlot, destSlot);
            Log.Ok("shop", "[{0}] 0x73 slot {1}<->{2} (pocao {3}) -> ack 0x27 (sem merge, mantem as 2)", Slot, destSlot, srcSlot, srcItem);
        }

        /// <summary>Erro curto do 0x73 (FUN_004038e0, len 3): [u16 0x73][u8 status]. Destrava o aviso do cliente.</summary>
        private void SendItemSlotError(byte status)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x73);
            w.WriteByte(status);
            SendLobby(w.ToArray());
        }

        /// <summary>
        /// Ack de sucesso do 0x73 (FUN_00421a50 -> subtype 0x27): cabecalho do move + a lista atual do box
        /// (mesma forma do 0x15: count1=0 + count2 = itens do box). P/ pocao os campos delta/soma sao 0.
        /// Forma de lista que o cliente ja conhece -> tira o aviso e mantem a juncao local que ele fez.
        /// </summary>
        private void SendItemSlotStackAck(byte srcSlot, byte destSlot)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x27);              // subtype (dispatch do cliente)
            w.WriteWord(0);                 // seq
            w.WriteUInt32((uint)FieldId);   // off4  fieldId (*(0x1460))
            w.WriteByte(srcSlot);           // off8
            w.WriteByte(destSlot);          // off9
            w.WriteUInt32(0);               // off10 srcDelta  (user+0x1bc4; 0 p/ pocao)
            w.WriteUInt32(0);               // off14 destDelta (0x1bc4)
            w.WriteUInt32(0);               // off18 mergedExp (soma de 0x1f94; 0 p/ pocao)
            w.WriteUInt32(0);               // off22 fsecHandle (0x14a4; 0 como na lista 0x15)
            w.WriteByte(0);                 // off26 count1 (delta de loadout) = 0
            // count2 = itens atuais do box (ids u32 + slots u8), igual ao bloco2 do 0x15
            byte n = 0;
            using var ids = new PacketWriter();
            using var slots = new PacketWriter();
            for (int i = 0; i < BoxItems.Count && i < 0x78; i++)
                if (BoxItems[i] != 0) { ids.WriteUInt32((uint)BoxItems[i]); slots.WriteByte((byte)i); n++; }
            w.WriteByte(n);                 // count2
            w.WriteBytes(ids.ToArray());    // count2 x u32 (itemIds)
            w.WriteBytes(slots.ToArray());  // count2 x u8  (slots)
            w.WriteByte(0);                 // tail
            SendLobby(w.ToArray());
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
                byte stage = p.Byte();         // b0 (<100) = stage id (confirmado in-game: idx=3 = Stage 3)
                byte rank = p.Byte();          // b1 (<6) = grade do rank (confirmado: 4 = Rank A; 0=nenhum, 1=D, 2=C, 3=B, 4=A, 5=S)
                byte cfgB = p.Byte();          // count (<5) = qtde de u16 (map slots) a pular
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
                if (rank > 0 && ActiveCharId > 0) _ = _server.Db.SaveStageRankAsync(ActiveCharId, stage, rank); // melhor rank por stage (userstageinfo)
                int ups = _server.GrantExp(this, exp);
                if (ups > 0) SendLevelUp();                    // 0x51 ao cliente: o PvP (Op_0x50_Recon) manda apos o GrantExp;
                                                               // sem ele o solo sobe o nivel so' no DB (aparece no relog), nunca na tela -> barra presa
                Log.Ok("field", "[{0}] 0x53 stage clear solo — stage={1} rank={2} exp={3} gold={4}{5}", Slot, stage, rank, exp, gold,
                    ups > 0 ? $" (+{ups} nivel -> {CharLevel})" : "");
            }
            catch (Exception ex) { Log.Warn("field", "[{0}] 0x53 solo parse: {1}", Slot, ex.Message); }
        }

        /// <summary>LOBBY 0x51 (level-up) ao proprio: [51 00][level][u16 levelPoint]. MESMO frame que o
        /// caminho PvP (Op_0x50_Recon) manda apos o GrantExp; sem ele o solo sobe o nivel server-side mas o
        /// cliente fica preso no nivel antigo (barra travada, ex.: 188/133) ate um relog.</summary>
        private void SendLevelUp()
        {
            byte[] f = { 0x51, 0x00, CharLevel,
                         (byte)(CharLevelPoint & 0xff), (byte)(CharLevelPoint >> 8 & 0xff) };
            SendEncryptedFrame(f);
            Log.Ok("level", "[{0}] 0x51 level-up -> nivel {1} (levelPoint {2}) ao cliente", Slot, CharLevel, CharLevelPoint);
        }
    }
}
