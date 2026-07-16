using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_VerifyClientHash(HandlerContext ctx)
        {
            string received = ctx.P.CanRead(32) ?
                System.Text.Encoding.ASCII.GetString(ctx.P.Bytes(32)) : string.Empty;
            byte? reason = Domain.ClientHashPolicy.FieldDisconnectReason(
                ctx.User.VerifyMode, ctx.User.InField, received, ctx.World.Config.ClientHashes);
            if (reason.HasValue)
            {
                Log.Warn("integrity", "[{0}] ChCode recusado (mode={1}, disc={2:X2})",
                    ctx.User.Slot, ctx.User.VerifyMode, reason.Value);
                ctx.User.Disconnect(reason.Value);
            }
        }

        private static void Op_ServerInfoDump(HandlerContext ctx)
        {
            // O original expõe 74 bytes de estado interno em S->C 0x77, mas esta build cliente não
            // possui produtor nem case de resposta. Não sintetizar ponteiros/configuração com zeros.
            Log.Warn("protocol", "[{0}] ServerInfoDump 0x77 dormente ignorado", ctx.User.Slot);
        }

        private static void Op_ClanMembersQuery(HandlerContext ctx)
        {
            _ = SendClanMembersAsync(ctx);
        }

        private static async System.Threading.Tasks.Task SendClanMembersAsync(HandlerContext ctx)
        {
            var user = ctx.User;
            var members = await ctx.World.Db.LoadClanMembersAsync(user.GameInfoId, user.ClanId);
            byte[] frame = members switch
            {
                null => LobbyFrames.ClanMembersStatus(2),
                { Count: 0 } => LobbyFrames.ClanMembersStatus(1),
                _ => LobbyFrames.ClanMembers(members)
            };
            if (user.Connected)
                user.SendEncryptedFrame(frame);
        }
    }
}
