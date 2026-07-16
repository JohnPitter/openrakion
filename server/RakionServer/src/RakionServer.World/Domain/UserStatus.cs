using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Fase da sessão. No original, user+0x1440 usa 0 livre, 1 conectado/seleção,
    /// 2 lobby/lista de salas, 3 membro de sala e 4/5 no fluxo do opcode 0x01.
    /// O servidor atual conserva aliases históricos para os interceptadores.
    /// </summary>
    public static class UserStatus
    {
        public const byte Disconnected = 0;
        public const byte Connected    = 1;   // socket aceito (user+0x1440 != 0)
        public const byte LoggedIn     = 2;   // apos login (opcode 0x0C ok)
        public const byte ChannelList  = 3;   // viu lista de canais
        public const byte Lobby        = 4;   // dentro de um canal (normal)
        public const byte LobbyGm      = 5;   // dentro de um canal como GM / PCBang
        // Estados de field (handlers reconstruidos): 2 = area de lista de salas
        // (RoomCreate exige), 3 = dentro de um field/partida (chat/gameplay exigem).
        public const byte FieldLobby   = 2;   // = LoggedIn: na area de salas
        public const byte InField      = 3;   // dentro de uma sala/partida
    }

    /// <summary>
    /// Categoria do usuário em user+0x146c. Não representa ownership da sala;
    /// o master é exclusivamente field+0x121/Field.MasterSlot.
    /// </summary>
    public static class UserSubStatus
    {
        public const byte Normal = 0;
        public const byte Special = 1;
        public const byte Gm = 0x34; // ASCII '4'
    }
}
