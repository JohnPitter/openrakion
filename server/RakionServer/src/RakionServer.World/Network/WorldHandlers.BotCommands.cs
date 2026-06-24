using System;
using System.Text;
using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Comandos de chat da SALA que controlam os bots ("/addbot", "/removebot"). O cliente é um
    /// binário fechado — não dá p/ adicionar um botão novo sem RE de UI (XFS/uitoolkit); o caminho
    /// idiomático e zero-patch é o comando de chat (a própria sala anuncia "Type /?, commands").
    /// O servidor lê o texto C→S em claro (a cifra é só outbound), então intercepta aqui antes do
    /// broadcast normal de chat. Toda a regra vive no serviço de domínio (WorldServer.AddBotToField).
    /// </summary>
    public static partial class WorldHandlers
    {
        private const int MaxBotsPerCommand = 7;

        /// <summary>
        /// Tenta tratar o corpo de um chat de sala como comando de bot. Devolve true se consumiu o
        /// comando (o chamador NÃO deve broadcastar como chat normal). Tolerante ao wrapping do body
        /// (byte de canal/terminador): extrai o texto imprimível e procura o comando a partir do '/'.
        /// </summary>
        internal static bool TryHandleBotChatCommand(HandlerContext ctx, byte[] body)
        {
            string text = ExtractPrintable(body);
            int slash = text.IndexOf('/');
            if (slash < 0) return false;
            string cmd = text.Substring(slash).Trim();
            string lower = cmd.ToLowerInvariant();

            bool isAdd = lower.StartsWith("/addbot") || lower == "/bot" || lower.StartsWith("/bot ");
            bool isRemove = lower.StartsWith("/removebot") || lower.StartsWith("/delbot") || lower.StartsWith("/clearbot");
            if (!isAdd && !isRemove) return false;

            var u = ctx.User;
            var field = ctx.World.GetField(u.FieldId);
            if (field == null) { BotChatFeedback(ctx, "sala invalida"); return true; }

            if (isRemove)
            {
                bool all = lower.Contains("all") || lower.StartsWith("/clearbot");
                int removed = ctx.World.RemoveBotsFromField(field, u, all);
                BotChatFeedback(ctx, removed > 0 ? $"{removed} bot(s) removido(s)" : "nenhum bot para remover");
                return true;
            }

            int count = Math.Clamp(ParseCount(lower), 1, MaxBotsPerCommand);
            int ok = 0; string lastMsg = "";
            for (int i = 0; i < count; i++)
            {
                var r = ctx.World.AddBotToField(field, u);
                if (r.Ok) { ok++; lastMsg = r.Message; }
                else { BotChatFeedback(ctx, ok > 0 ? $"{ok} bot(s); {r.Message}" : r.Message); return true; }
            }
            BotChatFeedback(ctx, ok == 1 ? lastMsg : $"{ok} bots adicionados");
            return true;
        }

        /// <summary>Extrai só os caracteres ASCII imprimíveis do corpo do chat (descarta canal/terminador/cifra-lixo).</summary>
        private static string ExtractPrintable(byte[] body)
        {
            var sb = new StringBuilder(body.Length);
            foreach (byte b in body)
                if (b >= 0x20 && b < 0x7f) sb.Append((char)b);
            return sb.ToString();
        }

        /// <summary>"/addbot 3" -> 3; sem número -> 1.</summary>
        private static int ParseCount(string lower)
        {
            int sp = lower.IndexOf(' ');
            if (sp < 0 || sp + 1 >= lower.Length) return 1;
            return int.TryParse(lower.Substring(sp + 1).Trim(), out int n) && n > 0 ? n : 1;
        }

        /// <summary>
        /// Devolve uma linha de chat de sistema ao host (canal LOBBY, opcode 0x56) confirmando o comando.
        /// Mesma forma que o cliente já renderiza: [0x56][senderSlot][u16 len][texto]. O sender é o próprio
        /// host (o cliente desenha como uma linha normal). É só feedback — a ação não depende disto.
        /// </summary>
        private static void BotChatFeedback(HandlerContext ctx, string msg)
        {
            var u = ctx.User;
            byte[] textb = Encoding.ASCII.GetBytes("[BOT] " + msg);
            using var w = new PacketWriter();
            w.WriteByte((byte)u.Slot);
            w.WriteWord(textb.Length);
            w.WriteBytes(textb);
            try { u.SendLobby(Prefix(0x56, w.ToArray())); } catch { }
            Log.Info("bot", "[{0}] {1}", u.Slot, msg);
        }
    }
}
