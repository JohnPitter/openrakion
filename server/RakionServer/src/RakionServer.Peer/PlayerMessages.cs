using System;

namespace RakionServer.Peer
{
    /// <summary>
    /// Identidade de borda do peer-bot (contrato explícito, não entidade crua). Carrega o
    /// <see cref="PlayerCharacter"/> serializável (nome/team/GUID/appearance) + o slot lógico do peer na sessão.
    /// O domínio (BotPlayer por nome/classe/level) TRADUZ para este DTO; a serialização de wire mora aqui.
    /// </summary>
    /// <param name="Character">O CPlayerCharacter da SE1 (o que vai cru no wire do CONNECTPLAYER/ADDPLAYER).</param>
    /// <param name="PlayerIndex">Índice/slot do player na sessão (iNewPlayer do ADDPLAYER; -1 se o host atribui).</param>
    public readonly record struct PeerIdentity(PlayerCharacter Character, int PlayerIndex)
    {
        /// <summary>Atalho p/ o nome (logs/diagnóstico).</summary>
        public string Name => Character.Name;
    }

    /// <summary>
    /// Builders das CNetworkMessages que carregam um <see cref="PlayerCharacter"/> — o REQ_CONNECTPLAYER que o
    /// PEER manda ao host (modelo arquitetural REAL: bot é cliente que JOINA o humano-host) e o SEQ_ADDPLAYER
    /// que o host gera e distribui (sintetizável p/ teste/golden e p/ o caminho relay-side).
    ///
    /// LAYOUTS CRAVADOS (FATO, fonte SE1):
    ///  - REQ_CONNECTPLAYER (type 14): PlayerSource.cpp:65-66 → <c>nm&lt;&lt;pls_pcCharacter</c>. Corpo:
    ///    [u8 14][CTString name][CTString team][16 GUID][32 appearance].
    ///  - SEQ_ADDPLAYER (type 22): Server.cpp:1298-1300 → <c>nsb&lt;&lt;iNewPlayer; nsb&lt;&lt;pcCharacter</c>;
    ///    lido em SessionState.cpp:1329-1330 (<c>&gt;&gt;iNewPlayer; &gt;&gt;pcCharacter</c>). Corpo:
    ///    [u8 22][INDEX iNewPlayer][CTString name][CTString team][16 GUID][32 appearance].
    ///
    /// CORREÇÃO vs versão anterior do slice: o corpo do ADDPLAYER NÃO é [name][charIndex][playerIndex]. É o
    /// INDEX do player SEGUIDO do CPlayerCharacter inteiro (name+team+GUID+appearance). Mandar o layout antigo
    /// fazia o host ler lixo no lugar de team/GUID/appearance — appearance corrompido → modelo NULL → AV (o
    /// crash do AddPlayer headless §10-12 de headless-engine-re.md). Daí o requisito de appearance VÁLIDO.
    /// </summary>
    public static class PlayerMessages
    {
        /// <summary>
        /// Corpo do MSG_REQ_CONNECTPLAYER (type 14) que o peer manda RELIABLE ao host p/ registrar o bot. O host
        /// responde MSG_REP_CONNECTPLAYER(15) com o índice atribuído e, internamente, gera o SEQ_ADDPLAYER que
        /// faz o session-state criar o CPlayerEntity. Este é o caminho da fonte (CPlayerSource::Start_t).
        /// </summary>
        public static byte[] BuildConnectPlayerRequest(PlayerCharacter character)
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.ReqConnectPlayer);
            character.WriteTo(w);
            return w.ToArray();
        }

        /// <summary>
        /// Corpo do sub-bloco SEQ_ADDPLAYER (type 22): [u8 22][INDEX iNewPlayer][CPlayerCharacter]. É o que o host
        /// stock põe num CNetworkStreamBlock e distribui; e o que o session-state lê p/ instanciar o player. O
        /// frame volta pronto p/ <see cref="GameStream.WrapBlock"/>.
        /// </summary>
        public static byte[] BuildAddPlayer(PeerIdentity identity)
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.SeqAddPlayer);
            w.WriteIndex(identity.PlayerIndex);     // INDEX iNewPlayer
            identity.Character.WriteTo(w);          // CPlayerCharacter: name, team, GUID(16), appearance(32)
            return w.ToArray();
        }
    }
}
