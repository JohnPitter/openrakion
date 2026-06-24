using System;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Serviço de domínio dos BOTS (parte do WorldServer, fora dos handlers de rede). Regra de
    /// negócio: adicionar/remover bot, escolher time/level/classe, e o ciclo efêmero (bots somem
    /// no fim do match ou quando o último humano sai). O comportamento in-field (spawn/IA/morte) é
    /// SINTETIZADO do estado — ver WorldServer.BotAi / o motor de partida —, nunca relay.
    /// </summary>
    public sealed partial class WorldServer
    {
        /// <summary>Resultado de uma tentativa de adicionar bot (p/ feedback ao host).</summary>
        public readonly record struct AddBotResult(bool Ok, string Message, int Seat, BotPlayer? Bot);

        /// <summary>Nomes temáticos dos bots (identidade efêmera; só exibição).</summary>
        private static readonly string[] BotNames =
            { "Rok", "Karion", "Ares", "Golem", "Vyl", "Drak", "Nyx", "Zair", "Brutus", "Kael" };

        private int _botNameSeq;

        /// <summary>
        /// Adiciona um bot ao field (sala) do <paramref name="requester"/>. Regras: só o HOST adiciona;
        /// só ANTES da partida começar (não durante o round); o bot entra no time OPOSTO ao host (p/ o
        /// humano jogar contra ele). Devolve o resultado p/ o handler dar feedback.
        /// </summary>
        public AddBotResult AddBotToField(Domain.Field f, ClientSession requester)
        {
            var hostRec = f.FindRec(requester);
            if (hostRec == null) return new AddBotResult(false, "voce nao esta na sala", -1, null);
            if (f.MasterSlot != hostRec.Slot) return new AddBotResult(false, "so o dono da sala adiciona bot", -1, null);
            if (f.Phase == MatchPhase.Playing) return new AddBotResult(false, "partida em andamento", -1, null);

            byte botTeam = (byte)(hostRec.Team ^ 1);     // time oposto ao host
            byte level = ChooseBotLevel(f, requester);
            byte cls = (byte)(requester.CharClass == 0 ? 1 : requester.CharClass);
            string name = NextBotName();

            var added = f.AddBot(name, level, cls, botTeam);
            if (added == null) return new AddBotResult(false, "sala cheia", -1, null);

            Log.Ok("bot", "[{0}] bot '{1}' (lvl {2} cls {3}) -> field {4} seat {5} time {6}",
                requester.Slot, name, level, cls, f.Id, added.Value.Seat, added.Value.Bot.Team);

            // Faz o cliente do host renderizar o bot no slot da sala (frame de roster sintetizado).
            NotifyBotJoinedRoom(f, added.Value.Bot, added.Value.Seat, requester);
            return new AddBotResult(true, $"{name} entrou (time {(added.Value.Bot.Team == 0 ? "vermelho" : "azul")})",
                added.Value.Seat, added.Value.Bot);
        }

        /// <summary>Remove o último bot adicionado (ou todos) do field do host. Devolve quantos saíram.</summary>
        public int RemoveBotsFromField(Domain.Field f, ClientSession requester, bool all)
        {
            var hostRec = f.FindRec(requester);
            if (hostRec == null || f.MasterSlot != hostRec.Slot) return 0;
            if (f.Phase == MatchPhase.Playing) return 0;

            int removed;
            if (all)
            {
                // notifica cada slot antes de limpar (p/ o cliente do host esvaziar o slot)
                foreach (var r in f.BotRecs()) NotifyBotLeftRoom(f, r.Slot, requester);
                removed = f.RemoveAllBots();
            }
            else
            {
                int seat = -1;
                foreach (var r in f.BotRecs()) seat = r.Slot; // último seat de bot
                if (seat < 0) return 0;
                NotifyBotLeftRoom(f, seat, requester);
                f.ClearBotSeat(seat);
                removed = 1;
            }
            Log.Ok("bot", "[{0}] {1} bot(s) removido(s) do field {2}", requester.Slot, removed, f.Id);
            return removed;
        }

        /// <summary>
        /// Descarta TODOS os bots de um field — chamado no fim do match e quando o último humano sai.
        /// A volta à criação/lista de sala mostra só humanos (bots nunca persistem no roster pós-match).
        /// </summary>
        public void DiscardBots(Domain.Field f)
        {
            if (f.BotCount == 0) return;
            // Esvazia o slot no cliente do host (0x3a member-leave por seat) ANTES de remover. Sem isto o
            // cliente segue mostrando o bot no slot após o fim do match/settle ("continuam visíveis"). Em
            // LeaveField o host já saiu (Master = sessão morta) — o send é try/catch, então é inócuo.
            var host = f.Master;
            if (host != null)
                foreach (var r in f.BotRecs())
                    NotifyBotLeftRoom(f, r.Slot, host);
            f.RemoveAllBots();
        }

        /// <summary>Level do bot: dentro da faixa da sala (MinLevel..MaxLevel) ancorado no level do host.</summary>
        private static byte ChooseBotLevel(Domain.Field f, ClientSession host)
        {
            byte lo = f.MinLevel == 0 ? (byte)1 : f.MinLevel;
            byte hi = f.MaxLevel == 0 ? (byte)99 : f.MaxLevel;
            byte anchor = host.CharLevel == 0 ? lo : host.CharLevel;
            if (anchor < lo) anchor = lo;
            if (anchor > hi) anchor = hi;
            return anchor;
        }

        /// <summary>Próximo nome de bot (pool temático + sufixo quando repete).</summary>
        private string NextBotName()
        {
            int i = _botNameSeq++;
            string baseName = BotNames[i % BotNames.Length];
            int round = i / BotNames.Length;
            return round == 0 ? baseName : $"{baseName}{round + 1}";
        }
    }
}
