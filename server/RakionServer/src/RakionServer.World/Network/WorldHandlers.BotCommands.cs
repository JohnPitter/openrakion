using System;
using System.Text;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Comandos de chat da SALA que controlam os bots ("/addbot", "/removebot"). O cliente é um
    /// binário fechado — não dá p/ adicionar um botão novo sem RE de UI (XFS/uitoolkit); o caminho
    /// idiomático e zero-patch é o comando de chat (a própria sala anuncia "Type /?, commands").
    /// O servidor lê o texto C→S em claro (a cifra é só outbound), então intercepta aqui antes do
    /// broadcast normal de chat. Toda a regra vive no serviço de domínio (WorldServer.AddBotToField).
    ///
    /// O chat DA SALA chega como opcode 0x47 (3D chat), roteado pelo <see cref="ClientSession"/> no
    /// LobbyFlow (não pela tabela WorldHandlers, onde 0x56=Stub). Por isso a entrada é context-free
    /// (recebe a sessão e o servidor), chamada de ClientSession.HandleLobbyFlow case 0x47.
    /// </summary>
    public static partial class WorldHandlers
    {
        private const int MaxBotsPerCommand = 7;

        /// <summary>
        /// Tenta tratar o corpo de um chat de sala como comando de bot. Devolve true se consumiu o
        /// comando (o chamador NÃO deve broadcastar como chat normal). Tolerante ao wrapping do body
        /// (byte de canal/terminador): extrai o texto imprimível e procura o comando a partir do '/'.
        /// </summary>
        internal static bool TryHandleBotChatCommand(ClientSession u, WorldServer world, byte[] body)
        {
            string text = ExtractPrintable(body);
            int slash = text.IndexOf('/');
            if (slash < 0) return false;
            string cmd = text.Substring(slash).Trim();
            string lower = cmd.ToLowerInvariant();

            bool isAdd = lower.StartsWith("/addbot") || lower == "/bot" || lower.StartsWith("/bot ");
            bool isRemove = lower.StartsWith("/removebot") || lower.StartsWith("/delbot") || lower.StartsWith("/clearbot");
            bool isStatus = lower.StartsWith("/botstatus") || lower.StartsWith("/bots");
            if (!isAdd && !isRemove && !isStatus) return false;

            var field = world.GetOrCreateRoomField(u);
            if (field == null) { BotChatFeedback(u, "crie uma sala primeiro"); return true; }

            if (isRemove)
            {
                bool all = lower.Contains("all") || lower.StartsWith("/clearbot");
                int removed = world.Bots.RemoveBotsFromField(field, u, all);
                BotChatFeedback(u, removed > 0 ? $"{removed} bot(s) removido(s)" : "nenhum bot para remover");
                return true;
            }

            if (isStatus)
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (var r in field.BotRecs())
                {
                    var b = r.Bot!;
                    string tgt = b.TargetSeat >= 0 ? $"#{b.TargetSeat}" : "none";
                    sb.AppendLine($"{b.Name} s{r.Slot} [{b.Difficulty}] hp={b.Hp} pos=({b.X:F1},{b.Z:F1}) tgt={tgt}");
                    n++;
                }
                BotChatFeedback(u, n > 0 ? $"{n} bot(s): {sb}" : "nenhum bot");
                return true;
            }

            int count = Math.Clamp(ParseCount(lower), 1, MaxBotsPerCommand);
            var diff = BotProfile.Parse(lower);     // "/addbot hard 2", "/addbot facil" -> dificuldade
            ushort npcCls = BotMovement.ParseNpcClass(lower);   // classe NPC opcional ("/addbot mago|arqueiro|golem")
            int ok = 0; string lastMsg = "";
            for (int i = 0; i < count; i++)
            {
                var r = world.Bots.AddBotToField(field, u, diff);
                if (r.Ok)
                {
                    if (npcCls != 0 && r.Bot != null) r.Bot.NpcClassId = npcCls;   // override da classe do avatar
                    ok++; lastMsg = r.Message;
                }
                else { BotChatFeedback(u, ok > 0 ? $"{ok} bot(s); {r.Message}" : r.Message); return true; }
            }
            BotChatFeedback(u, ok == 1 ? lastMsg : $"{ok} bots adicionados ({diff})");
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

        /// <summary>Primeiro token numérico do comando ("/addbot 3", "/addbot hard 2" -> 3/2); sem número -> 1.</summary>
        private static int ParseCount(string lower)
        {
            foreach (var tok in lower.Split(' ', '\t'))
                if (int.TryParse(tok.Trim(), out int n) && n > 0) return n;
            return 1;
        }

        /// <summary>
        /// Devolve uma linha de chat ao host confirmando o comando. O chat da sala é 0x47 (mesma forma
        /// que o cliente acabou de MANDAR: texto cru "&lt;nome&gt; : &lt;msg&gt;"); o cliente já tem o
        /// caminho de RECEBER 0x47 (chat multiplayer da sala), então ecoamos no mesmo opcode/forma. É só
        /// feedback — a ação (bot adicionado) não depende disto; o log [bot] é a prova de fluxo.
        /// </summary>
        private static void BotChatFeedback(ClientSession u, string msg)
        {
            byte[] textb = Encoding.ASCII.GetBytes("Bot : " + msg + "\0");
            try { u.SendLobby(Prefix(0x47, textb)); } catch { }
            Log.Info("bot", "[{0}] {1}", u.Slot, msg);
        }
    }
}
