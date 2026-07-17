using System;
using System.Collections.Generic;

namespace RakionServer.Buddy
{
    /// <summary>
    /// Protocolo do Buddy Server, reconstruido por RE do Buddy2.dll (client-side).
    /// Dispatcher do client: FUN_10007420 (CBuddy2::OnMsg).
    ///
    /// Frame (TCP, ambas as direcoes): [u16 size][u16 CD][payload]
    ///   size = tamanho TOTAL do pacote (inclui o campo size). O parser do client
    ///   (DoProcessStream) valida `*(u16*)pkt == size` e `size &lt; 0x13881`.
    ///   CD em pkt+2; payload em pkt+4; payloadLen = size-4.
    ///
    /// Codigos CD (RET_/NTF_ = servidor->cliente) extraidos do switch do OnMsg.
    /// Os SVC_ (cliente->servidor) seguem o padrao SVC = RET &amp; ~1 (req par, ret impar).
    /// </summary>
    public static class BuddyProtocol
    {
        public const int HeaderSize = 4;        // [u16 size][u16 CD]
        public const int MaxPacket = 0x13880;   // < 0x13881

        // ---- servidor -> cliente (RET_/NTF_), do switch FUN_10007420 ----
        public const ushort RET_PRECREDENTIAL   = 0x1001;
        public const ushort RET_LOGIN           = 0x1011;
        public const ushort NTF_VIP_IPPORT      = 0x101f;
        public const ushort NTF_NOTICE          = 0x1ffe;
        public const ushort NTF_CLOSE_CONNECTION= 0x1fff;
        public const ushort NTF_SAVE_PACKET     = 0x2010;
        public const ushort NTF_TUNNEL_PACKET   = 0x2021;
        public const ushort RET_SMS_SEND        = 0x2031;
        public const ushort RET_ADD_BUDDY       = 0x3001;
        public const ushort RET_REMOVE_BUDDY    = 0x3003;
        public const ushort RET_GROUP_BUDDY     = 0x3005;
        public const ushort RET_RENAME_GROUP    = 0x3007;
        public const ushort RET_SET_NICK        = 0x3101;   // corrigido p/ OnMsg FUN_10007e10 (validado in-game); SVC 0x3100
        public const ushort RET_SET_EXTUSER     = 0x3105;   // SVC 0x3104
        public const ushort RET_SET_GUILD       = 0x3103;
        public const ushort RET_SET_EXTLIST     = 0x3111;
        public const ushort RET_GROUP_GETLIST   = 0x3151;   // SVC 0x3150
        public const ushort RET_GROUP_ADD       = 0x3153;   // SVC 0x3152
        public const ushort RET_GROUP_DEL       = 0x3155;
        public const ushort RET_GROUP_CHG       = 0x3157;
        public const ushort NTF_USER_STATE      = 0x3fff;
        public const ushort NTF_NOTICE2         = 0x5000;

        // ---- cliente -> servidor (SVC_), por SVC = RET & ~1 ----
        public const ushort SVC_PRECREDENTIAL   = 0x1000;
        public const ushort SVC_LOGIN           = 0x1010;
        public const ushort SVC_ADD_BUDDY       = 0x3000;
        public const ushort SVC_REMOVE_BUDDY    = 0x3002;
        public const ushort SVC_GROUP_BUDDY     = 0x3004;
        public const ushort SVC_RENAME_GROUP    = 0x3006;
        public const ushort SVC_SET_NICK        = 0x3100;
        public const ushort SVC_SET_EXTUSER     = 0x3104;
        public const ushort SVC_GROUP_GETLIST   = 0x3150;
        public const ushort SVC_GROUP_ADD       = 0x3152;
        public const ushort SVC_GROUP_DEL       = 0x3154;
        public const ushort SVC_GROUP_CHG       = 0x3156;
        public const ushort SVC_SMS_SEND        = 0x2030;
        public const ushort SVC_TUNNEL          = 0x2020;   // PM p/ relay (CBuddy2::TunnelPacket FUN_10008720) -> NTF 0x2021

        private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
        {
            [RET_PRECREDENTIAL] = "RET_PRECREDENTIAL", [SVC_PRECREDENTIAL] = "SVC_PRECREDENTIAL",
            [RET_LOGIN] = "RET_LOGIN", [SVC_LOGIN] = "SVC_LOGIN",
            [NTF_VIP_IPPORT] = "NTF_VIP_IPPORT", [NTF_NOTICE] = "NTF_NOTICE",
            [NTF_CLOSE_CONNECTION] = "NTF_CLOSE_CONNECTION", [NTF_SAVE_PACKET] = "NTF_SAVE_PACKET",
            [NTF_TUNNEL_PACKET] = "NTF_TUNNEL_PACKET", [RET_SMS_SEND] = "RET_SMS_SEND",
            [SVC_SMS_SEND] = "SVC_SMS_SEND", [SVC_TUNNEL] = "SVC_TUNNEL",
            [RET_ADD_BUDDY] = "RET_ADD_BUDDY", [SVC_ADD_BUDDY] = "SVC_ADD_BUDDY",
            [RET_REMOVE_BUDDY] = "RET_REMOVE_BUDDY", [SVC_REMOVE_BUDDY] = "SVC_REMOVE_BUDDY",
            [RET_GROUP_BUDDY] = "RET_GROUP_BUDDY", [RET_RENAME_GROUP] = "RET_RENAME_GROUP",
            [RET_GROUP_GETLIST] = "RET_GROUP_GETLIST", [RET_GROUP_ADD] = "RET_GROUP_ADD",
            [RET_GROUP_DEL] = "RET_GROUP_DEL", [RET_GROUP_CHG] = "RET_GROUP_CHG",
            [RET_SET_NICK] = "RET_SET_NICK", [RET_SET_GUILD] = "RET_SET_GUILD",
            [RET_SET_EXTUSER] = "RET_SET_EXTUSER", [SVC_SET_NICK] = "SVC_SET_NICK",
            [SVC_SET_EXTUSER] = "SVC_SET_EXTUSER", [SVC_GROUP_GETLIST] = "SVC_GROUP_GETLIST",
            [NTF_USER_STATE] = "NTF_USER_STATE", [NTF_NOTICE2] = "NTF_NOTICE2",
        };

        public static string Name(int cd) => Names.TryGetValue(cd, out var n) ? n : $"CD_0x{cd:x4}";
    }
}
