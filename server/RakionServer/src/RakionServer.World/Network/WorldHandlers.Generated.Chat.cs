using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_FieldGameTunnelingAll(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa1); return; }
            if (u.Status != 0x03) { return; }

            ushort len = ctx.P.UInt16();
            if (len > 1000) { u.Disconnect(0xa2); return; }
            byte[] body = ctx.P.Bytes(len);
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            lock (field.SyncRoot)
            {
                var sender = field.FindRec(u);
                if (sender?.State != 4) return;
                using var w = new PacketWriter();
                w.WriteWord(len);
                w.WriteBytes(body);
                byte[] payload = w.ToArray();
                foreach (var rec in field.Slots)
                {
                    if (rec.Session == null ||
                        !TunnelingRelayPolicy.ShouldRelayAll(field, sender, rec)) continue;
                    rec.Session.SendMessage(0x57, payload);
                }
            }
        }

        private static void Op_FieldGameTunnelingOne(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa3); return; }
            if (u.Status != 0x03) { return; }

            byte targetSeat = ctx.P.Byte();
            if (targetSeat > 0x13) { u.Disconnect(0xa4); return; }
            ushort len = ctx.P.UInt16();
            if (len > 1000) { u.Disconnect(0xa5); return; }
            byte[] body = ctx.P.Bytes(len);
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            lock (field.SyncRoot)
            {
                var sender = field.FindRec(u);
                var target = field.RecAt(targetSeat);
                if (sender == null || target?.Session == null ||
                    !TunnelingRelayPolicy.ShouldRelayOne(field, sender, target)) return;
                using var w = new PacketWriter();
                w.WriteWord(len);
                w.WriteBytes(body);
                target.Session.SendMessage(0x57, w.ToArray());
            }
        }

        private static void Op_FieldPingRequest(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa6); return; }
            if (u.Status != 0x03) { return; }

            byte targetSeat = ctx.P.Byte();
            if (targetSeat > 0x13) { u.Disconnect(0xa7); return; }
            uint tick = ctx.P.UInt32();
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            lock (field.SyncRoot)
            {
                var target = field.RecAt(targetSeat);
                var master = field.RecAt(field.MasterSlot);
                if (target == null || target.State is 0 or 5 || master?.Session == null) return;
                using var w = new PacketWriter();
                w.WriteWord(u.Slot);
                w.WriteUInt32(tick);
                master.Session.SendMessage(0x59, w.ToArray());
            }
        }

        private static void Op_FieldPingResponse(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xa8); return; }
            if (u.Status != 0x03) { return; }

            ushort targetSlot = ctx.P.UInt16();
            if (targetSlot >= ctx.World.MaxUser) { u.Disconnect(0xa9); return; }
            uint tick = ctx.P.UInt32();
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            lock (field.SyncRoot)
            {
                var responder = field.FindRec(u);
                ClientSession? target = ctx.World.GetSession(targetSlot);
                if (responder == null || target?.Status != 0x03 || target.FieldId != u.FieldId) return;
                using var w = new PacketWriter();
                w.WriteWord((ushort)responder.Slot);
                w.WriteUInt32(tick);
                target.SendEncryptedFrame(Prefix(0x5a, w.ToArray()));
            }
        }

        private static void Op_FieldVoteOpen(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xad); return; }
            if (u.Status != 0x03) { u.Disconnect(0xae); return; }

            byte targetSeat = ctx.P.Byte();
            string reason = ctx.P.CString();
            var field = ctx.World.GetField(u.FieldId);
            var sender = field?.FindRec(u);
            if (field == null || sender == null) return;
            lock (field.SyncRoot)
            {
                FieldVoteTransition transition = field.ProcessVote(
                    0, (byte)sender.Slot, targetSeat, reason, Environment.TickCount64);
                DeliverFieldVoteTransition(u, field, sender, transition);
            }
        }

        private static void Op_FieldVoteCast(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0xaf); return; }
            if (u.Status != 0x03) { u.Disconnect(0xb0); return; }

            byte choice = ctx.P.Byte();
            var field = ctx.World.GetField(u.FieldId);
            var sender = field?.FindRec(u);
            if (field == null || sender == null) return;
            lock (field.SyncRoot)
            {
                FieldVoteTransition transition = field.ProcessVote(
                    choice, (byte)sender.Slot, 0, null, Environment.TickCount64);
                DeliverFieldVoteTransition(u, field, sender, transition);
            }
        }

        private static void DeliverFieldVoteTransition(
            ClientSession requester, Field field, PlayerRec sender, FieldVoteTransition transition)
        {
            if (transition.Opened)
            {
                byte[] body = FieldVoteFrames.OpenBody(field.VoteTargetSeat, field.VoteReason);
                foreach (PlayerRec record in field.Slots)
                {
                    if (!record.Playing || record.Session == null || record == sender ||
                        record.Slot == field.VoteTargetSeat) continue;
                    record.Session.SendMessage(0x5d, body);
                }
                Log.Info("vote", "field {0}: seat {1} abriu voto contra seat {2}, motivo='{3}'",
                    field.Id, sender.Slot, field.VoteTargetSeat, field.VoteReason);
            }
            if (transition.Final != null)
            {
                byte[] body = FieldVoteFrames.ResultBody(transition.Final);
                field.BroadcastFieldPlaying(0x5f, body);
                Log.Info("vote", "field {0}: voto finalizado yes={1} no={2} abstain={3} penalidade={4}",
                    field.Id, transition.Final.Yes, transition.Final.No,
                    transition.Final.Abstain, transition.Final.PenaltyApplied);
            }
            if (transition.Status != FieldVoteStatus.Success)
                requester.SendLobby(FieldVoteFrames.Status(transition.Status));
        }

    }
}
