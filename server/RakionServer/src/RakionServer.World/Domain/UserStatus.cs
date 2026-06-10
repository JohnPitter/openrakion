using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Estado do usuario (byte em user+0x1440 no worldserv.exe). Valores observados
    /// na RE dos handlers: o evento de conexao marca "conectado"; o login promove;
    /// entrar no canal (opcode 0x01) seta 4 (normal) ou 5 (GM/PCBang); estados de
    /// field/sala sao maiores. Mantido como byte para fidelidade ao binario.
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

    /// <summary>Sub-estado dentro do field/sala (user+0x146c).</summary>
    public static class UserSubStatus
    {
        public const byte None    = 0;
        public const byte Master  = 1;   // dono da sala
        public const byte Member  = 4;   // membro
    }
}
