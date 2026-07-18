using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Handlers de field/sala/chat. Reconstruidos de worldserv.exe — sao wrappers
    /// finos que validam o estado e delegam o broadcast ao objeto Field/Room
    /// (FUN_004061f0 = broadcast no field; FUN_00404ef0 = broadcast no room).
    /// O conteudo (slots, modo, mapa) e RE incremental dos metodos do Field.
    /// </summary>
    public static partial class WorldHandlers
    {
        /// <summary>FUN_0041bca0: chat do canal + comando "/roominfo &lt;id&gt;". Requer status 2.</summary>
        private static void Op_ChannelChat(HandlerContext ctx)
        {
            var u = ctx.User;
            if (u.Status != UserStatus.FieldLobby) return;
            string text = ctx.P.CString(0x1000);

            int colon = text.IndexOf(':');
            if (colon >= 0 && text.Length >= colon + 2 + 9 &&
                string.CompareOrdinal(text, colon + 2, "/roominfo", 0, 9) == 0)
            {
                int id = ParseIntAt(text, colon + 12);
                if (ctx.World.TryGetRoomInfo(id, out FieldRoomInfoSnapshot snapshot))
                {
                    foreach (byte[] response in FieldRoomInfoFrames.Responses(snapshot))
                        u.SendLobby(response);
                    Log.Debug("room", "[{0}] /roominfo {1}: 26 linhas", u.Slot, id);
                }
                return;
            }

            // "/addbot [facil|normal|dificil] [n]" — só o host, dentro de uma sala competitiva.
            if (TryExtractCommand(text, "/addbot", out string addBotCommand))
            {
                HandleAddBotCommand(ctx, addBotCommand);
                return;
            }

            if (!ctx.World.ModerateChat(u, null, ChatScope.Channel, text, out text)) return;
            ctx.World.BroadcastChannelChat(u, text);
        }

        /// <summary>Traduz "/addbot [dificuldade] [n]" em chamadas ao <see cref="BotManager"/> e ecoa o
        /// resultado ao host (chat de canal). Regra de negócio (host-only, time oposto, pré-match) mora
        /// no BotManager; aqui só parse + feedback.</summary>
        private static void HandleAddBotCommand(HandlerContext ctx, string command)
        {
            ClientSession host = ctx.User;
            Domain.Field? field = ctx.World.GetField(host.FieldId);
            if (field == null) { ctx.World.WhisperSystem(host, "voce nao esta em uma sala"); return; }

            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Domain.BotDifficulty difficulty = Domain.BotDifficulty.Normal;
            int count = 1;
            foreach (string p in parts[1..])
            {
                switch (p.ToLowerInvariant())
                {
                    case "facil" or "easy" or "fácil": difficulty = Domain.BotDifficulty.Easy; break;
                    case "normal": difficulty = Domain.BotDifficulty.Normal; break;
                    case "dificil" or "hard" or "difícil": difficulty = Domain.BotDifficulty.Hard; break;
                    default: if (int.TryParse(p, out int n)) count = Math.Clamp(n, 1, 10); break;
                }
            }

            int added = 0;
            string lastMessage = "";
            for (int i = 0; i < count; i++)
            {
                var result = ctx.World.Bots.AddBotToField(field, host, difficulty);
                if (result.Ok) added++; else { lastMessage = result.Message; break; }
            }
            ctx.World.WhisperSystem(host, added > 0
                ? $"{added} bot(s) adicionado(s) ({difficulty})"
                : $"nao foi possivel adicionar bot: {lastMessage}");
        }

        /// <summary>FUN_004244f0: chat no field; publica seat do remetente e texto.</summary>
        private static void Op_FieldChat(HandlerContext ctx)
        {
            var u = ctx.User;
            if (!(u.InField && u.FieldSecondary)) { u.Disconnect(0x7e); return; }
            string text = ctx.P.CString(0x81);
            if (text.Length >= 0x81) { u.Disconnect(0x80); return; }
            if (TryExtractCommand(text, "/addbot", out string command))
            {
                HandleAddBotCommand(ctx, command);
                return;
            }
            if (u.Status is not (UserStatus.FieldLobby or UserStatus.InField))
            {
                u.Disconnect(0x7f);
                return;
            }
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) return;
            var sender = field.FindRec(u);
            if (sender == null) return;
            if (!ctx.World.ModerateChat(u, null, ChatScope.Field, text, out text)) return;

            field.BroadcastField(0x47, FieldChatFrames.Message((byte)sender.Slot, text));
            Log.Debug("chat", "[{0}] field {1} seat {2}: {3}", u.Slot, field.Id, sender.Slot, text);
        }

        private static bool TryExtractCommand(string text, string name, out string command)
        {
            command = "";
            int colon = text.IndexOf(':');
            int start = colon >= 0 ? colon + 1 : 0;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            if (!text.AsSpan(start).StartsWith(name, StringComparison.Ordinal)) return false;
            int end = start + name.Length;
            if (end < text.Length && !char.IsWhiteSpace(text[end])) return false;
            command = text[start..];
            return true;
        }

        private static int ParseIntAt(string s, int start)
        {
            if (start >= s.Length) return -1;
            int end = start;
            while (end < s.Length && (char.IsDigit(s[end]) || (end == start && s[end] == '-'))) end++;
            return int.TryParse(s.AsSpan(start, end - start), out int v) ? v : -1;
        }
    }
}
