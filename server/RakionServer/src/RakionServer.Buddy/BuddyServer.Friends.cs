using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy;

public sealed partial class BuddyServer
{
    private async Task AddBuddyAsync(BuddyConnection connection, byte[] payload)
    {
        BuddyFriendRecord? friend = null;
        if (connection.Authenticated && BuddyFriendCodec.TryParseAdd(
                payload, out string accountId, out byte[] extension))
            friend = await _database.AddFriendAsync(
                connection.AccountId, accountId, extension);
        await SendAsync(connection, BuddyProtocol.RET_ADD_BUDDY,
            BuddyFriendCodec.BuildAddResult(friend == null ? (ushort)1 : (ushort)0, friend));
        if (friend != null)
        {
            Log.Info("buddy-friend", "owner='{0}' adicionou buddy='{1}'",
                connection.AccountId, friend.AccountId);
            await SyncFriendPresenceAsync(connection, friend.AccountId);
        }
    }

    private async Task RemoveBuddyAsync(BuddyConnection connection, byte[] payload)
    {
        string accountId = "";
        bool removed = connection.Authenticated &&
            BuddyFriendCodec.TryParseAccount(payload, out accountId) &&
            await _database.RemoveFriendAsync(connection.AccountId, accountId);
        await SendAsync(connection, BuddyProtocol.RET_REMOVE_BUDDY,
            BuddyFriendCodec.BuildRemoveResult(removed ? (ushort)0 : (ushort)1, accountId));
        if (removed)
            Log.Info("buddy-friend", "owner='{0}' removeu buddy='{1}'",
                connection.AccountId, accountId);
    }

    private async Task AssignBuddyGroupAsync(BuddyConnection connection, byte[] payload)
    {
        bool changed = connection.Authenticated &&
            BuddyFriendCodec.TryParseGroupMembers(
                payload, out string[] accountIds, out string groupName) &&
            await _database.AssignGroupAsync(connection.AccountId, groupName, accountIds);
        await ReplyResultAsync(connection, BuddyProtocol.RET_GROUP_BUDDY,
            changed ? (ushort)0 : (ushort)1);
        if (changed)
            Log.Info("buddy-group", "owner='{0}' atualizou grupo", connection.AccountId);
    }

    private async Task RenameBuddyGroupAsync(BuddyConnection connection, byte[] payload)
    {
        bool changed = connection.Authenticated &&
            BuddyFriendCodec.TryParseRenameGroup(payload, out string oldName, out string newName) &&
            await _database.RenameGroupAsync(connection.AccountId, oldName, newName);
        await ReplyResultAsync(connection, BuddyProtocol.RET_RENAME_GROUP,
            changed ? (ushort)0 : (ushort)1);
        if (changed)
            Log.Info("buddy-group", "owner='{0}' renomeou grupo", connection.AccountId);
    }

    private async Task AddBuddyGroupAsync(BuddyConnection connection, byte[] payload)
    {
        bool added = connection.Authenticated &&
            BuddyFriendCodec.TryParseGroupAdd(payload, out BuddyGroupRecord group) &&
            await _database.AddGroupAsync(connection.AccountId, group);
        await ReplyResultAsync(connection, BuddyProtocol.RET_GROUP_ADD,
            added ? (ushort)0 : (ushort)1);
        if (added)
            Log.Info("buddy-group", "owner='{0}' criou grupo", connection.AccountId);
    }

    private async Task SendGroupListAsync(BuddyConnection connection)
    {
        if (!connection.Authenticated)
        {
            await SendAsync(connection, BuddyProtocol.RET_GROUP_GETLIST,
                BuddyFriendCodec.BuildGroupList(1, []));
            return;
        }
        var groups = await _database.LoadGroupsAsync(connection.AccountId);
        await SendAsync(connection, BuddyProtocol.RET_GROUP_GETLIST,
            BuddyFriendCodec.BuildGroupList(0, groups));
    }

    private async Task SetNickAsync(BuddyConnection connection, byte[] payload)
    {
        string requestedName = "";
        bool valid = connection.Authenticated &&
            BuddyFriendCodec.TryParseWideName(payload, out requestedName);
        BuddyAccount? account = valid
            ? await _database.LoadAccountAsync(connection.AccountId)
            : null;
        if (account == null)
        {
            await ReplyResultAsync(connection, BuddyProtocol.RET_SET_NICK, 1);
            return;
        }
        connection.DisplayName = account.DisplayName;
        connection.ActiveCharacterName = account.ActiveCharacterName;
        connection.PendingProfileSignature = "";
        await ReplyResultAsync(connection, BuddyProtocol.RET_SET_NICK, 0);
        await SendLoginOkAsync(connection);
        Log.Info("buddy", "account='{0}' solicitou nick '{1}'; perfil efetivo='{2}' e lista atualizada",
            connection.AccountId, requestedName, connection.DisplayName);
    }

    private async Task SetGuildAsync(BuddyConnection connection, byte[] payload)
    {
        string guildName = "";
        bool valid = connection.Authenticated &&
            BuddyFriendCodec.TryParseWideName(payload, out guildName);
        if (valid) await _database.SaveProfileAsync(connection.AccountId, guildName, null);
        await ReplyResultAsync(connection, BuddyProtocol.RET_SET_GUILD,
            valid ? (ushort)0 : (ushort)1);
    }

    private async Task SetExtUserAsync(BuddyConnection connection, byte[] payload)
    {
        byte[] extension = [];
        bool valid = connection.Authenticated &&
            BuddyFriendCodec.TryParseExtUser(payload, out extension);
        if (valid) await _database.SaveProfileAsync(connection.AccountId, null, extension);
        await ReplyResultAsync(connection, BuddyProtocol.RET_SET_EXTUSER,
            valid ? (ushort)0 : (ushort)1);
    }

    private async Task SetExtListAsync(BuddyConnection connection, byte[] payload)
    {
        bool valid = connection.Authenticated && BuddyFriendCodec.TryParseExtList(
            payload, out string accountId, out byte[] extension) &&
            await _database.SetFriendExtensionAsync(
                connection.AccountId, accountId, extension);
        await ReplyResultAsync(connection, BuddyProtocol.RET_SET_EXTLIST,
            valid ? (ushort)0 : (ushort)1);
    }
}
