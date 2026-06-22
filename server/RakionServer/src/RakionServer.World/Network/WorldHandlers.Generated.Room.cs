using System.Linq;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
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

            // PREVIEW (FUN_00421e10): a PROBABILIDADE do refino roda no CLIENTE (CUser::CheckEnchantReinforce
            // client-side, fórmula FP). O servidor original SÓ valida os itens e CONFIRMA via subtype 0x28 p/ o
            // cliente avançar ao COMMIT (opcode 0x28 do canal lobby -> Op_EnchantCommit / FUN_0041de40, que aplica
            // o result code rolado pelo cliente e consome os itens). O server-side roll de FUN_0040c310 é
            // DESCARTADO no original (mov [esp+0x12],bl zera o result após o call) -> não o replicamos.
            // Reply FIEL ao struct local_1010 (seq+subtype primeiro, depois os slots+SNs na ordem exata):
            //   [u16 seq][u16 0x28][u32 +0x1460][slot+SN arma][slot+SN catalyzer][u8 qtd][3x(slot+SN material)]
            //   [u8 0][u32 +0x14a4][u8 snapCount]. SN sintético = itemId da célula (não-zero; o commit usa slots).
            uint Sn(byte slot) => (uint)(slot < u.BoxItems.Count ? u.BoxItems[slot] : 0);

            using var w = new PacketWriter();
            w.WriteWord(0);                           // seq (user+0x1488)
            w.WriteWord(0x28);                        // subtype
            w.WriteUInt32((uint)u.FieldHandleRaw);    // local_100c = +0x1460
            w.WriteByte(valA);   w.WriteUInt32(Sn(valA));    // arma:      slot + SN
            w.WriteByte(b1);     w.WriteUInt32(Sn(b1));      // catalyzer: slot + SN
            w.WriteByte(count);                              // qtd de materiais
            for (int i = 0; i < 3; i++) { w.WriteByte(data[i]); w.WriteUInt32(Sn(data[i])); } // 3x slot+SN
            w.WriteByte(0);                           // local_fee
            w.WriteUInt32((uint)u.FieldSecondaryRaw); // local_fed = +0x14a4
            w.WriteByte(0);                           // snapCount (snapshot do inventário vazio)
            u.SendLobby(w.ToArray());
            Log.Debug("enchant", "[{0}] 0x74 preview -> 0x28 (arma {1} cat {2} +{3} mat)", u.Slot, valA, b1, count);

            // UPGRADE (0x74 = clique de upgrade; o preenchimento do refinador usa 0x31). SERVER-AUTHORITATIVE:
            // o servidor ROLA a probabilidade, aplica o +N e consome (WorldServer.ApplyEnchant — regra de domínio),
            // e devolve o RESULTADO no reply [u16 0x74][result][slot arma][slot cat][qtd][slots mats] (formato do
            // FUN_0041de40). É esse reply que limpa o "Upgrading Now" e mostra sucesso/falha no cliente.
            byte[] mats = new byte[count];
            for (int i = 0; i < count; i++) mats[i] = data[i];
            byte result = ctx.World.ApplyEnchant(u, valA, b1, mats);
            using (var wr = new PacketWriter())
            {
                wr.WriteWord(0x74);
                wr.WriteByte(result);
                wr.WriteByte(valA);
                wr.WriteByte(b1);
                wr.WriteByte(count);
                for (int i = 0; i < count; i++) wr.WriteByte(data[i]);
                u.SendLobby(wr.ToArray());
            }
            Log.Ok("enchant", "[{0}] 0x74 upgrade -> result={1}", u.Slot, result);
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
    }
}
