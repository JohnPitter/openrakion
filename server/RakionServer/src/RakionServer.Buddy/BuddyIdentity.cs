using System.Collections.Generic;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Casamento conexão-Buddy ↔ sessão-World quando há 2+ clientes no MESMO IP (sem 2º PC). O login do Buddy é
    /// cifrado/opaco, então a identidade vem da <c>messenger_session</c> que o World grava — mas por IP sozinho o
    /// vínculo fica ambíguo. A desambiguação é por PROXIMIDADE DE PORTA: o cliente conecta ao Buddy imediatamente
    /// após o login do World (a janela F9 nasce no handler da resposta de login), então as portas efêmeras dos dois
    /// sockets do MESMO processo são vizinhas e crescentes. A conexão casa com a sessão de MENOR distância circular
    /// "porta-do-World → porta-do-Buddy" (mod 64k, cobrindo o wrap do pool efêmero do SO).
    /// </summary>
    public static class BuddyIdentity
    {
        /// <summary>Índice da sessão cuja porta de World está mais próxima ANTES da porta da conexão do Buddy
        /// (distância circular de 16 bits — a efêmera do Buddy é alocada depois da do World). -1 se a lista é
        /// vazia. Porta 0 (linha legada sem porta) tem distância = a própria porta do Buddy: perde de qualquer
        /// candidata real mais próxima, mas ainda casa se for a única.</summary>
        public static int PickNearestByPort(IReadOnlyList<int> worldPorts, int buddyPort)
        {
            int best = -1, bestDist = int.MaxValue;
            for (int i = 0; i < worldPorts.Count; i++)
            {
                int dist = (buddyPort - worldPorts[i]) & 0xFFFF;
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }
    }
}
