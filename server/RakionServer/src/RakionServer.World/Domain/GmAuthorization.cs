namespace RakionServer.World.Domain
{
    public enum GmPermission
    {
        ServerLock,
        VariablesRead,
        VariablesWrite,
        QueryEntry,
        ClientHashWrite
    }

    public enum AccountAuthority
    {
        Player = 0,
        Moderator = 1,
        GameMaster = 2,
        Administrator = 3
    }

    public static class GmAuthorization
    {
        public static bool IsAllowed(int authority, bool enabled, GmPermission permission)
        {
            if (!enabled || !System.Enum.IsDefined(permission)) return false;
            var role = (AccountAuthority)authority;
            return permission switch
            {
                GmPermission.VariablesRead or GmPermission.QueryEntry =>
                    role >= AccountAuthority.Moderator,
                GmPermission.VariablesWrite => role >= AccountAuthority.GameMaster,
                GmPermission.ServerLock or GmPermission.ClientHashWrite =>
                    role >= AccountAuthority.Administrator,
                _ => false
            };
        }

        public static byte LobbyStatus(int authority, bool enabled, bool specialChannel) =>
            specialChannel && IsAllowed(authority, enabled, GmPermission.VariablesRead)
                ? UserStatus.LobbyGm
                : UserStatus.Lobby;
    }
}
