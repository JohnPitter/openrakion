using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_FieldUnitCommand(HandlerContext ctx)
        {
            var u = ctx.User;
            // FUN_00424100. userObj = this+0xd4 + slot*0x23b4.
            // Guard 0x73: usergameinfo.id (+0x1460) e characterinfo.id (+0x14a4) ativos.
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
            // Guard 0x77: usergameinfo.id (+0x1460) e characterinfo.id (+0x14a4) ativos.
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


        private static void Op_FieldUnitCharAction(HandlerContext ctx)
        {
            var u = ctx.User;
            // FUN_004246e0. userObj = this+0xd4 + slot*0x23b4.
            // Guard 0x83: usergameinfo.id (+0x1460) e characterinfo.id (+0x14a4) ativos.
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


        private static void Op_FieldForceChangeTeam(HandlerContext ctx)
        {
            var user = ctx.User;
            if (!(user.InField && user.FieldSecondary)) { user.Disconnect(0xaa); return; }
            if (user.Status != UserStatus.InField) { user.Disconnect(0xab); return; }
            if (user.SubStatus != UserSubStatus.Special) { user.Disconnect(0xac); return; }

            var field = ctx.World.GetField(user.FieldId);
            var sender = field?.FindRec(user);
            if (field == null || sender == null || field.State == 2 || sender.Slot != field.MasterSlot) return;

            byte targetSeat = ctx.P.Byte();
            if (targetSeat >= 0x12) return;
            ClientSession? target = field.RecAt(targetSeat)?.Session;
            ForcedTeamChangeResult result;
            byte newSeat;
            lock (field.SyncRoot)
                result = field.ForceChangeTeam(targetSeat, out newSeat);

            if (result == ForcedTeamChangeResult.Denied)
            {
                target?.SendMessage(0x3e, new byte[] { 2 });
                return;
            }
            if (result != ForcedTeamChangeResult.Changed) return;

            field.BroadcastField(0x3e, new byte[] { 0, targetSeat, newSeat });
            Log.Ok("field", "[{0}] forçou time/seat {1}->{2} no field {3}",
                user.Slot, targetSeat, newSeat, field.Id);
        }


        private static void Op_FieldBossTargetReport(HandlerContext ctx)
        {
            var u = ctx.User;
            // FUN_00425cc0. pvVar2 = user[slot].
            // Guard 1: user+0x1460 && user+0x14a4 != 0, senao DISC 0xb1.
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xb1); return; }
            // Guard 2: user+0x1440 (Status) == 3, senao DISC 0xb2.
            if (u.Status != 0x03) { u.Disconnect(0xb2); return; }

            // FUN_0040b7d0(user, out fieldId(param_1,u16), out slotInField(local_4,byte)).
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            var rec = field.FindRec(u);
            if (rec == null) return;
            byte slotInField = (byte)rec.Slot;

            // Gate do exe: slotInField precisa bater com um dos dois bytes de papel do field
            //   playerRecord+0x122 (lider time A)  OU  playerRecord+0x123 (lider time B).
            // So entao o comando e aplicado. Modelado via Field.LeaderSlotA/LeaderSlotB.
            lock (field.SyncRoot)
            {
                if (slotInField != field.LeaderSlotA && slotInField != field.LeaderSlotB) return;
                byte arg0 = ctx.P.Byte();
                ushort arg1 = ctx.P.UInt16();
                field.ApplyBossTarget(slotInField, arg0, arg1);
            }
        }


        private static void Op_WorldEchoReply(HandlerContext ctx)
        {
            var u = ctx.User;
            int value = ctx.P.Int32();
            u.WorldEchoValue = value;
            if (value == ctx.World.EchoReference)
            {
                ctx.World.EchoMatchCount++;
            }
        }

        private static void Op_FieldRelayAction(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary && u.Status == 0x03)) return;
            if (!ctx.P.CanRead(1)) return;
            byte targetSeat = ctx.P.Byte();
            Field? field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            if (!field.TryResolveSlotUdpRelay(u, targetSeat, out ClientSession? target, out byte senderSeat))
                return;

            target!.SendMessage(0x62, new[] { senderSeat });
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
            //   local_1000 = *(int)(iVar1+0x1460) -> payload de 4 bytes (usergameinfo.id).
            // len = 8 = seq(2)+subtype(2)+payload(4). Em C#, SendMessage ja escreve seq+subtype,
            // entao envio apenas o payload (os 4 bytes de local_1000).
            int fieldHandle = u.GameInfoId; // *(int)(user+0x1460) = usergameinfo.id

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
            int fieldSecondary = u.ActiveCharId; // local_12f0 = characterinfo.id em user+0x14a4

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
            //   local_100c[0]    : *(int)(iVar1+0x1460)  (usergameinfo.id).
            //   local_100c[1]    : reqA (echo do u32 recebido).
            //   local_1004       : reqB (echo do u16 recebido).
            //   local_1002       : fieldSecondary (int, *(user+0x14a4) = local_12f0).
            //   local_ffe        : cntA.
            //   ...em seguida: bloco A; byte cntB; bloco B; byte flagC; bloco C (se flagC != 0).
            // Em C#, SendMessage ja escreve seq+subtype(0x1f); o payload começa no usergameinfo.id.
            int fieldHandle = u.GameInfoId;    // *(int)(user+0x1460) = usergameinfo.id

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


        private static void Op_FieldUseItem(HandlerContext ctx)
        {
            var u = ctx.User;
            // FUN_00428c90. Guard: this_00+0x1460 (InField) && this_00+0x14a4 (FieldSecondary) != 0 -> senao DISC 0xd0.
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xd0); return; }
            // this_00+0x1440 (Status) deve ser '\x03' -> senao DISC 0xd1.
            if (u.Status != 0x03) { u.Disconnect(0xd1); return; }

            // Payload: [u8 cell][s16 itemId]. O byte indexa diretamente os 19 slots do user;
            // as poções equipadas ocupam as células 13..18.
            byte cell = ctx.P.Byte();
            short itemId = ctx.P.Int16();

            // FUN_0040b7d0(this_00, &local(u16), &local(byte)): le user+0x14a0 (indice do field-object) e
            // user+0x14a2. O 4o argumento de FUN_0040e5f0 e *(field-object[idx] + 0x119) = a flag/modo do field.
            var field = ctx.World.GetField(u.FieldId);
            byte fieldMode = field?.Mode ?? (byte)0;

            // FUN_0040e5f0 valida item/contagem, decrementa uma unidade e marca a célula usada.
            // Em mode 0 a marca não bloqueia novos usos; nos demais modos, bloqueia a mesma célula.
            // O original não responde nem retransmite 0x6e: o efeito trafega no EUsePotion P2P.
            if (!u.AuthorizeFieldPotionUse(cell, itemId, fieldMode))
            {
                u.Disconnect(0xd2);
                return;
            }

            Log.Info("potion", "[{0}] 0x6e autorizado: slot={1} item={2} mode={3}",
                u.Slot, cell, itemId, fieldMode);
            _ = u.PersistFieldPotionUseAsync(cell, itemId);
        }
    }
}
