using System;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Serviço de domínio dos BOTS — subsistema AUTÔNOMO, extraído do <see cref="WorldServer"/> p/ isolar o
    /// acoplamento. Regra de negócio: adicionar/remover bot, escolher time/level/classe, o ciclo efêmero (bots
    /// somem no fim do match ou quando o último humano sai), o motor de IA/combate (partials .Ai/.Objective) e o
    /// render no cliente (.Roster/.Npc/.Peer). O comportamento in-field é SINTETIZADO do estado (nunca relay).
    /// Depende do <see cref="WorldServer"/> APENAS pela porta do gameplay UDP (<see cref="_gameplayPort"/>); todo
    /// o resto trafega por <see cref="Domain.Field"/> (parâmetro), domínio (<see cref="BotPlayer"/>) e estáticos
    /// (BotMovement/BotProfile/BridgeIo/GolemWarLayouts). Instância única, dona do <see cref="WorldServer.Bots"/>.
    /// </summary>
    public sealed partial class BotManager
    {
        /// <summary>Única dependência do servidor: a porta do gameplay UDP (destino loopback dos datagramas do bot).
        /// Lazy pois o <see cref="Network.UdpGameplay"/> nasce no Start, após a construção do BotManager.</summary>
        private readonly Func<int> _gameplayPort;

        public BotManager(Func<int> gameplayPort) => _gameplayPort = gameplayPort;

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
        public AddBotResult AddBotToField(Domain.Field f, ClientSession requester,
                                          BotDifficulty difficulty = BotDifficulty.Normal)
        {
            var hostRec = f.FindRec(requester);
            if (hostRec == null) return new AddBotResult(false, "voce nao esta na sala", -1, null);
            if (f.MasterSlot != hostRec.Slot) return new AddBotResult(false, "so o dono da sala adiciona bot", -1, null);
            if (f.Phase == MatchPhase.Playing) return new AddBotResult(false, "partida em andamento", -1, null);

            byte botTeam = (byte)(hostRec.Team ^ 1);     // time oposto ao host
            byte level = ChooseBotLevel(f, requester);
            byte cls = (byte)(requester.CharClass == 0 ? 1 : requester.CharClass);
            string name = NextBotName();

            var added = f.AddBot(name, level, cls, botTeam, difficulty);
            if (added == null) return new AddBotResult(false, "sala cheia", -1, null);

            Log.Ok("bot", "[{0}] bot '{1}' (lvl {2} cls {3} dif {4}) -> field {5} seat {6} time {7}",
                requester.Slot, name, level, cls, difficulty, f.Id, added.Value.Seat, added.Value.Bot.Team);

            // Faz TODOS os clientes da sala renderizarem o bot no slot (mesmo trilho do join humano).
            NotifyBotJoinedRoom(f, added.Value.Bot, added.Value.Seat);
            string difLabel = difficulty switch { BotDifficulty.Easy => "fácil", BotDifficulty.Hard => "difícil", _ => "normal" };
            return new AddBotResult(true,
                $"{name} entrou (time {(added.Value.Bot.Team == 0 ? "vermelho" : "azul")}, {difLabel})",
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
                // notifica cada slot antes de limpar (p/ todos os clientes esvaziarem o slot) + fecha o link de rede
                foreach (var r in f.BotRecs()) { NotifyBotLeftRoom(f, r.Slot); if (r.Bot != null) DisposeLink(r.Bot); }
                removed = f.RemoveAllBots();
            }
            else
            {
                PlayerRec? last = null;
                foreach (var r in f.BotRecs()) last = r; // último bot
                if (last == null) return 0;
                NotifyBotLeftRoom(f, last.Slot);
                if (last.Bot != null) DisposeLink(last.Bot);   // fecha socket/peer do bot removido
                f.ClearBotSeat(last.Slot);
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
            // Esvazia o slot em todos os clientes (0x3a member-leave por seat) ANTES de remover. Sem isto o
            // cliente segue mostrando o bot no slot após o fim do match/settle ("continuam visíveis"). Em
            // LeaveField a sessão que saiu já morreu — o send é try/catch, então é inócuo.
            foreach (var r in f.BotRecs())
            {
                NotifyBotLeftRoom(f, r.Slot);
                if (r.Bot != null) DisposeLink(r.Bot);   // fecha socket/peer (o link mora no BotManager, não no domínio)
            }
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
