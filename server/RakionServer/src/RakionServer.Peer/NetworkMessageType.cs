namespace RakionServer.Peer
{
    /// <summary>
    /// Tipos lógicos das CNetworkMessages da Serious Engine 1 (enum NETWORKMESSAGETYPE,
    /// Sources/Engine/Network/NetworkMessage.h, branch master). VERBATIM da fonte aberta e casado
    /// com o dispatcher do disasm da engine.dll (type = 1º byte &amp; 0x3f; jump-table @0x36109168;
    /// type 5/7 → ConnectLocal/Remote @0x36105650/0x36105f30). É o 1º byte de toda CNetworkMessage.
    ///
    /// Golden source ÚNICO do protocolo (CLAUDE.md): não duplicar estes valores em const/literal.
    /// Os tipos que o mini-peer do bot usa no handshake (Start_AtClient_t §5 do blueprint) estão
    /// TODOS abaixo de 0x2f: CONNECTREMOTE 7/8, STATEDELTA 9/10, CRC 11/12/13, SEQ_ADDPLAYER 22.
    ///
    /// NOTA (blueprint §2.1): MSG_EXTRA = '/' (47) reposiciona o fim do enum, logo REP_DISCONNECTED=48
    /// (0x30). Esse 0x30 do enum NÃO se confunde com o 0x030a do FIO (msgType de DATAGRAMA L1, codec
    /// do BotMovement) — são camadas distintas.
    /// </summary>
    public enum NetworkMessageType : byte
    {
        ReqEnumServers = 0,
        ServerInfo = 1,
        KeepAlive = 2,
        InfDisconnected = 3,
        InfPings = 4,
        ReqConnectLocalSessionState = 5,
        RepConnectLocalSessionState = 6,
        ReqConnectRemoteSessionState = 7,
        RepConnectRemoteSessionState = 8,
        ReqStateDelta = 9,
        RepStateDelta = 10,
        ReqCrcList = 11,
        ReqCrcCheck = 12,
        RepCrcCheck = 13,
        ReqConnectPlayer = 14,
        RepConnectPlayer = 15,
        ReqPause = 16,
        ReqCharacterChange = 17,
        Action = 18,
        SyncCheck = 19,
        ActionPredict = 20,
        SeqAllActions = 21,
        SeqAddPlayer = 22,
        SeqRemPlayer = 23,
        SeqPause = 24,
        SeqCharacterChange = 25,
        GameStreamBlocks = 26,
        RequestGameStreamResend = 27,
        ChatIn = 28,
        ChatOut = 29,
        SetClientSettings = 30,
        AdminCommand = 31,
        AdminResponse = 32,
        Extra = (byte)'/',          // 47 (0x2f)
        RepDisconnected = 48        // 0x30 (após MSG_EXTRA reposicionar o enum)
    }
}
