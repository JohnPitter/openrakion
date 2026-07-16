using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy
{
    public sealed partial class BuddyServer
    {
        private async Task DispatchAsync(
            BuddyConnection connection, ushort command, byte[] payload)
        {
            Log.Debug("buddy", "[{0}] RECV CD=0x{1:x4} ({2}) len={3}",
                connection.RemoteIp, command, BuddyProtocol.Name(command), payload.Length);
            switch (command)
            {
                case BuddyProtocol.SVC_PRECREDENTIAL:
                    await SendPrecredentialAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_LOGIN:
                    await LoginAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_SMS_SEND:
                    await SendSmsAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_SAVE_PACKET_ACK:
                    await AcknowledgeSavedPacketsAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_TUNNEL_PACKET:
                    await RelayTunnelAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_SET_NICK:
                    await SetNickAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_SET_GUILD:
                    await SetGuildAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_SET_EXTUSER:
                    await SetExtUserAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_SET_EXTLIST:
                    await SetExtListAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_GROUP_GETLIST:
                    await SendGroupListAsync(connection);
                    break;
                case BuddyProtocol.SVC_ADD_BUDDY:
                    await AddBuddyAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_REMOVE_BUDDY:
                    await RemoveBuddyAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_GROUP_BUDDY:
                    await AssignBuddyGroupAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_RENAME_GROUP:
                    await RenameBuddyGroupAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_GROUP_ADD:
                    await AddBuddyGroupAsync(connection, payload);
                    break;
                case BuddyProtocol.SVC_GROUP_DEL:
                    await ReplyResultAsync(connection, BuddyProtocol.RET_GROUP_DEL, 1);
                    break;
                case BuddyProtocol.SVC_GROUP_CHG:
                    await ReplyResultAsync(connection, BuddyProtocol.RET_GROUP_CHG, 1);
                    break;
                default:
                    Log.Debug("buddy", "[{0}] comando 0x{1:x4} sem handler", connection.RemoteIp, command);
                    break;
            }
        }

        private async Task SendPrecredentialAsync(BuddyConnection connection, byte[] payload)
        {
            if (payload.Length != 0)
            {
                Log.Warn("buddy", "[{0}] SVC_PRECREDENTIAL com {1} byte(s)",
                    connection.RemoteIp, payload.Length);
                return;
            }
            connection.CredentialSeed = RandomUInt32();
            connection.CredentialCookie = RandomUInt32();
            byte[] response = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(response, connection.CredentialSeed);
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(4), connection.CredentialCookie);
            await SendAsync(connection, BuddyProtocol.RET_PRECREDENTIAL, response);
        }

        private async Task LoginAsync(BuddyConnection connection, byte[] payload)
        {
            if (connection.CredentialSeed == 0 ||
                !BuddyCrypto.TryReadCredential(payload, out BuddyCredential credential) ||
                credential.Seed != connection.CredentialSeed)
            {
                await RejectLoginAsync(connection, 1, "credencial inválida");
                return;
            }
            BuddyAccount? account = await _database.LoadAccountAsync(credential.AccountId);
            if (account == null || !BuddyCrypto.TryOpenLogin(payload, account.Password,
                    connection.CredentialSeed, out _, out var crypto, out _))
            {
                await RejectLoginAsync(connection, 2, "autenticação inválida");
                return;
            }

            connection.AccountId = account.AccountId;
            connection.DisplayName = account.DisplayName;
            connection.Crypto = crypto;
            BuddyChatState chat = await _database.LoadChatStateAsync(account.AccountId);
            connection.ChatState.Load(chat.MutedUntilUtc, chat.BlockedAccounts);
            if (_online.TryGetValue(account.AccountId, out BuddyConnection? previous) &&
                !ReferenceEquals(previous, connection))
                try { previous.Socket.Shutdown(System.Net.Sockets.SocketShutdown.Both); }
                catch (System.Net.Sockets.SocketException) { }
            _online[account.AccountId] = connection;

            await SendLoginOkAsync(connection);
            await DeliverPendingAsync(connection);
            Log.Ok("buddy", "[{0}] login account='{1}' autenticado", connection.RemoteIp, account.AccountId);
        }

        private async Task SendSmsAsync(BuddyConnection sender, byte[] payload)
        {
            if (!sender.Authenticated || !sender.Crypto.TryDecrypt(payload, out byte[] clear) ||
                !BuddySmsCodec.TryParseSend(clear, out string targetAccount, out string text))
            {
                await ReplyResultAsync(sender, BuddyProtocol.RET_SMS_SEND, 1);
                return;
            }

            BuddyChatState targetState = await _database.LoadChatStateAsync(targetAccount);
            ChatModerationDecision decision;
            if (targetState.BlockedAccounts.Contains(sender.AccountId,
                    StringComparer.OrdinalIgnoreCase))
                decision = new ChatModerationDecision(
                    ChatModerationAction.Blocked, text, "recipient-block");
            else
                decision = _moderation.Evaluate(sender.ChatState, ChatScope.Sms, text,
                    DateTime.UtcNow, Environment.TickCount64);

            if (decision.Action != ChatModerationAction.Allowed)
            {
                await _database.AuditAsync(sender.AccountId, targetAccount, decision, text);
                if (decision.AutoMuteUntil.HasValue)
                    await _database.SaveAutomaticMuteAsync(sender.AccountId,
                        decision.AutoMuteUntil.Value, decision.Rule);
            }
            if (!decision.Allowed)
            {
                await ReplyResultAsync(sender, BuddyProtocol.RET_SMS_SEND, 3);
                Log.Warn("buddy-sms", "sender='{0}' target='{1}' action={2} rule='{3}'",
                    sender.AccountId, targetAccount, decision.Action, decision.Rule);
                return;
            }

            var senderAccount = new BuddyAccount(sender.AccountId, "", sender.DisplayName);
            BuddySmsMessage? message = await _database.QueueSmsAsync(
                senderAccount, targetAccount, decision.Text);
            if (message == null)
            {
                await ReplyResultAsync(sender, BuddyProtocol.RET_SMS_SEND, 2);
                return;
            }
            await ReplyResultAsync(sender, BuddyProtocol.RET_SMS_SEND, 0);
            if (_online.TryGetValue(targetAccount, out BuddyConnection? target))
                await DeliverAsync(target, [message]);
            Log.Info("buddy-sms", "id={0} sender='{1}' target='{2}' queued",
                message.Id, sender.AccountId, targetAccount);
        }

        private async Task DeliverPendingAsync(BuddyConnection connection)
        {
            IReadOnlyList<BuddySmsMessage> pending =
                await _database.LoadPendingSmsAsync(connection.AccountId);
            if (pending.Count > 0) await DeliverAsync(connection, pending);
        }

        private async Task DeliverAsync(
            BuddyConnection target, IReadOnlyList<BuddySmsMessage> messages)
        {
            if (!target.Authenticated || messages.Count == 0) return;
            byte[] clear = BuddySmsCodec.BuildSavedBatch(messages);
            byte[] encrypted = target.Crypto.Encrypt(clear);
            await SendAsync(target, BuddyProtocol.NTF_SAVE_PACKET, encrypted);
            var ids = new uint[messages.Count];
            for (int i = 0; i < messages.Count; i++) ids[i] = messages[i].Id;
            await _database.MarkDeliveredAsync(target.AccountId, ids);
        }

        private async Task AcknowledgeSavedPacketsAsync(
            BuddyConnection connection, byte[] payload)
        {
            if (!connection.Authenticated ||
                !BuddySmsCodec.TryParseAcknowledgement(payload, out uint[] ids))
                return;
            await _database.AcknowledgeAsync(connection.AccountId, ids);
            Log.Debug("buddy-sms", "target='{0}' confirmou {1} mensagem(ns)",
                connection.AccountId, ids.Length);
        }

        private async Task RejectLoginAsync(BuddyConnection connection, ushort result, string reason)
        {
            await ReplyResultAsync(connection, BuddyProtocol.RET_LOGIN, result);
            Log.Warn("buddy", "[{0}] login rejeitado: {1}", connection.RemoteIp, reason);
        }

        private async Task SendLoginOkAsync(BuddyConnection connection)
        {
            IReadOnlyList<BuddyFriendRecord> friends =
                await _database.LoadFriendsAsync(connection.AccountId);
            uint udpToken = IssueUdpToken(connection);
            byte[] payload = BuddyFriendCodec.BuildLogin(udpToken, friends);
            await SendAsync(connection, BuddyProtocol.RET_LOGIN, payload);
        }

        private async Task ReplyResultAsync(
            BuddyConnection connection, ushort command, ushort result)
        {
            byte[] payload = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, result);
            await SendAsync(connection, command, payload);
        }

        private static uint RandomUInt32()
        {
            Span<byte> bytes = stackalloc byte[4];
            uint value;
            do
            {
                RandomNumberGenerator.Fill(bytes);
                value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            } while (value == 0);
            return value;
        }
    }
}
