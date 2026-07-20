using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Whisper/localizacao/busca global por personagem (opcodes 0x16-0x1a). Transcritos de
    /// worldserv.exe. Usam enumeracao de sessoes para achar usuarios por nome (FUN_0040af20).
    /// </summary>
    public static partial class WorldHandlers
    {
        private static CharacterPresenceSnapshot Presence(ClientSession session) => new(
            session.Status,
            session.GameInfoId,
            session.ActiveCharId,
            session.SubStatus,
            session.CharName,
            session.ChannelId,
            session.FieldId);

        /// <summary>FUN_00420200: whisper global por nome. Entrega ao alvo e ecoa ao remetente.</summary>
        private static void Op_FieldWhisper(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!CharacterPresenceRules.HasSelectedCharacter(Presence(u))) { u.Disconnect(0x21); return; }
            string name = ctx.P.CString();
            if (name.Length >= 0x0d) { u.Disconnect(0x22); return; }
            string msg = ctx.P.CString();
            if (msg.Length >= 0x81) { u.Disconnect(0x23); return; }

            ClientSession? target = CharacterPresenceRules.FindTarget(
                ctx.World.Sessions, name, Presence);
            if (target != null && ctx.World.ModerateChat(
                    u, target, ChatScope.Whisper, msg, out msg))
            {
                byte[] pkt = WhisperLocationProtocol.Whisper(u.CharName, msg);
                target.SendLobby(pkt);
                u.SendLobby(pkt);
            }
            else
            {
                u.SendLobby(WhisperLocationProtocol.WhisperNotFound());
            }
        }

        /// <summary>FUN_00420410: devolve servidor e localizacao do proprio usuario.</summary>
        private static void Op_GetLocation(HandlerContext ctx)
        {
            var u = ctx.User;
            CharacterPresenceSnapshot presence = Presence(u);
            if (!CharacterPresenceRules.HasSelectedCharacter(presence)) { u.Disconnect(0x24); return; }
            if (!CharacterPresenceRules.TryGetLocation(presence, out WorldLocation location))
            {
                u.Disconnect(0x25);
                return;
            }

            u.SendLobby(WhisperLocationProtocol.WhereAmI(
                unchecked((byte)ctx.World.Config.ServerId), location));
        }

        /// <summary>FUN_00420520: busca a localizacao de outro usuario por nome.</summary>
        private static void Op_FindUser(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!CharacterPresenceRules.HasSelectedCharacter(Presence(u))) { u.Disconnect(0x26); return; }
            string name = ctx.P.CString();
            if (name.Length >= 0x0d) { u.Disconnect(0x27); return; }

            ClientSession? target = CharacterPresenceRules.FindTarget(
                ctx.World.Sessions, name, Presence);
            if (target != null &&
                CharacterPresenceRules.TryGetLocation(Presence(target), out WorldLocation location))
            {
                u.SendLobby(WhisperLocationProtocol.WhereAreYou(name, location));
            }
            else
            {
                u.SendLobby(WhisperLocationProtocol.WhereAreYouNotFound(name));
            }
        }

        private static void Op_CharacterGetUserName(HandlerContext context)
        {
            context.User.BeginCharacterIdentityLookup(context.Raw);
        }

    }
}
