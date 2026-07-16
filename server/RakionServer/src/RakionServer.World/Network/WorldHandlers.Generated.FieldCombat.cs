using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
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
                target?.SendMessage(0x3e, FieldForceChangeTeamFrames.Denied());
                return;
            }
            if (result != ForcedTeamChangeResult.Changed) return;

            field.BroadcastField(0x3e,
                FieldForceChangeTeamFrames.Changed(targetSeat, newSeat));
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
