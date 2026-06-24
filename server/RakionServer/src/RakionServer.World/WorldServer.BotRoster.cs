using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Renderização do bot no ROSTER da sala (os slots RED/BLUE do game room). Faz o cliente do host
    /// desenhar o bot num slot ao adicioná-lo e esvaziar o slot ao removê-lo. O frame é SINTETIZADO da
    /// estrutura do member-join do worldserv (caminho multi-membro), nunca replay.
    /// </summary>
    public sealed partial class WorldServer
    {
        /// <summary>
        /// Notifica o cliente do host que um bot entrou num slot da sala (frame de member-join).
        /// TODO (task #3): cravar o wire exato do member-join no worldserv (roomflow.out.txt) — o caminho
        /// multi-membro nunca foi exercitado (servidor sempre foi solo). Até lá, o bot existe no domínio
        /// e spawna no stage (0x45, frame conhecido); a aparição no slot da sala depende desta RE.
        /// </summary>
        private void NotifyBotJoinedRoom(Domain.Field f, BotPlayer bot, int seat, ClientSession host)
        {
            Log.Debug("bot", "roster: bot '{0}' seat {1} (render no slot pendente de RE do member-join)", bot.Name, seat);
        }

        /// <summary>Notifica o host que um slot de bot esvaziou (frame de member-leave). TODO task #3.</summary>
        private void NotifyBotLeftRoom(Domain.Field f, int seat, ClientSession host)
        {
            Log.Debug("bot", "roster: slot {0} liberado (render pendente de RE do member-leave)", seat);
        }
    }
}
