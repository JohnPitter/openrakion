using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Assentos de BOT no field. Um bot ocupa um <see cref="PlayerRec"/> como um jogador (mesmo
    /// State/Team), mas com <see cref="PlayerRec.Session"/> nulo e <see cref="PlayerRec.Bot"/> setado.
    /// Ciclo efêmero: os bots são limpos no fim do match ou quando o último humano sai (regra do
    /// subsistema, aplicada pelo BotManager). Não há relay do bot — o movimento é sintetizado.
    /// </summary>
    public sealed partial class Field
    {
        /// <summary>Assentos ocupados por bot (para o tick de IA e a limpeza).</summary>
        public IEnumerable<PlayerRec> BotSlots
        {
            get { foreach (var r in Slots) if (r.IsBot) yield return r; }
        }

        public int BotCount
        {
            get { int n = 0; foreach (var r in Slots) if (r.IsBot) n++; return n; }
        }

        /// <summary>
        /// Aloca um bot num assento livre do <paramref name="team"/> pedido (0 = 0..9, 1 = 10..19).
        /// O bot entra pronto (State 2), como um humano que já marcou pronto. Devolve o seat ou -1
        /// se o bloco do time estiver cheio.
        /// </summary>
        public int AddBot(BotPlayer bot, byte team)
        {
            int start = team == 0 ? 0 : 10;
            int end = start + 10;
            for (int i = start; i < end && i < Slots.Length; i++)
            {
                PlayerRec rec = Slots[i];
                if (rec.State == 0 && rec.Session == null && rec.Bot == null)
                {
                    bot.Seat = (byte)i;
                    rec.Bot = bot;
                    rec.State = 2;
                    rec.WeaponState = 1;
                    rec.Dead = false;
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Remove um bot pelo seat (re-add / kick). Libera o assento.</summary>
        public bool RemoveBot(byte seat)
        {
            PlayerRec? rec = RecAt(seat);
            if (rec == null || rec.Bot == null) return false;
            ClearBotSlot(rec);
            return true;
        }

        /// <summary>Limpa TODOS os bots (fim de match / último humano saiu).</summary>
        public int RemoveAllBots()
        {
            int removed = 0;
            foreach (var r in Slots)
                if (r.Bot != null) { ClearBotSlot(r); removed++; }
            return removed;
        }

        private static void ClearBotSlot(PlayerRec rec)
        {
            rec.Bot = null;
            rec.State = 0;
            rec.WeaponState = 1;
            rec.Dead = false;
            rec.RoundScore = 0;
            rec.CounterA = 0;
            rec.CounterB = 0;
            rec.ResultPoints = 0;
            rec.VoteState = 0;
            rec.Position = default;
            rec.Heading = 0;
            rec.Combat.Reset();
        }

        /// <summary>True se ainda há algum humano (Session != null) ocupando o field.</summary>
        public bool HasHumans
        {
            get { foreach (var r in Slots) if (r.Session != null && r.Occupied) return true; return false; }
        }
    }
}
