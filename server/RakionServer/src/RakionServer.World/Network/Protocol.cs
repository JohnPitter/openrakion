using System;
using System.Collections.Generic;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Constantes do protocolo do World Server, reconstruidas por engenharia
    /// reversa do RakionWorldServ.exe (worldserv.exe v258, Ghidra). Cada simbolo
    /// cita o endereco/funcao de origem no binario — ver PROTOCOL.md.
    ///
    /// Dispatcher: FUN_0042ab40(this, slot, opcode:u16, len, payload).
    ///   switch(opcode) { cases 1..0x79 -> handlers; default -> Disconnect(0xc9). }
    ///   Apos o switch, se NAO autenticado (this+0x5b18==0): opcode cai no
    ///   LOGIN handler FUN_0041f6c0; se autenticado: FUN_0042a310 (in-game).
    /// </summary>
    public static class Protocol
    {
        /// <summary>Opcodes (1o campo dispatchado). Nomeados pelas strings de debug do exe quando disponivel.</summary>
        public static class Op
        {
            // Pre-login / sessao
            public const ushort Login = 0x0C;            // FUN_0041f6c0 (fallthrough nao-autenticado)
            public const ushort DisconnectNotify = 0x0E; // FUN_0041eb20 envia subtype 4 via FUN_0041b940(...,0xe,...)

            // Campos de jogo (nomes vindos das strings "(NN) NetworkMessage..." do exe)
            public const ushort FieldList = 0x47;        // 71  NetworkMessageFieldList
            public const ushort FieldQuickEnter = 0x4F;  // 79  NetworkMessageFieldQuickEnter
            public const ushort FieldExit = 0x51;        // 81  NetworkMessageFieldExit
            public const ushort FieldCreate = 0x53;      // 83  NetworkMessageFieldCreate
            public const ushort FieldReady = 0x61;       // 97  NetworkMessageFieldReady
            public const ushort FieldChat = 0x7F;        // 127 NetworkMessageFieldChat
        }

        /// <summary>
        /// Conjunto completo de opcodes tratados pelo dispatcher FUN_0042ab40
        /// (cases 1..0x79). Os ainda nao implementados sao logados e respondidos
        /// como "nao tratado" — servem de mapa para RE incremental por handler.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, string> KnownOpcodes = new Dictionary<int, string>
        {
            [0x01] = "h01", [0x02] = "h02", [0x03] = "h03", [0x04] = "h04", [0x05] = "h05",
            [0x08] = "h08", [0x09] = "h09", [0x0A] = "h0a", [0x0B] = "h0b",
            [0x0C] = "Login", [0x0E] = "DisconnectNotify", [0x0F] = "h0f",
            [0x10] = "h10", [0x12] = "h12", [0x13] = "h13", [0x14] = "h14", [0x15] = "h15",
            [0x16] = "h16", [0x17] = "h17", [0x18] = "h18", [0x19] = "h19", [0x1A] = "h1a",
            [0x1B] = "h1b", [0x1C] = "h1c", [0x1E] = "h1e", [0x20] = "h20", [0x22] = "h22",
            [0x29] = "h29", [0x2A] = "h2a", [0x2C] = "h2c", [0x2D] = "h2d", [0x2E] = "h2e",
            [0x2F] = "h2f", [0x31] = "h31", [0x32] = "h32", [0x33] = "h33", [0x34] = "h34",
            [0x35] = "h35", [0x36] = "h36", [0x38] = "h38", [0x39] = "h39", [0x3A] = "h3a",
            [0x3B] = "h3b", [0x3D] = "h3d", [0x3E] = "h3e", [0x3F] = "h3f",
            [0x40] = "h40", [0x41] = "h41", [0x42] = "h42", [0x43] = "h43", [0x45] = "h45",
            [0x46] = "h46", [0x47] = "FieldList", [0x48] = "h48", [0x4A] = "h4a", [0x4B] = "h4b",
            [0x4C] = "h4c", [0x4D] = "h4d", [0x4F] = "FieldQuickEnter", [0x50] = "h50",
            [0x53] = "FieldCreate", [0x56] = "h56", [0x57] = "h57", [0x59] = "h59", [0x5A] = "h5a",
            [0x5B] = "h5b", [0x5D] = "h5d", [0x5E] = "h5e", [0x60] = "h60", [0x61] = "FieldReady",
            [0x62] = "h62", [0x64] = "h64", [0x65] = "h65", [0x6B] = "h6b", [0x6C] = "h6c",
            [0x6D] = "h6d", [0x6E] = "h6e", [0x6F] = "h6f", [0x70] = "h70", [0x71] = "h71",
            [0x72] = "h72", [0x73] = "h73", [0x74] = "h74", [0x75] = "h75", [0x76] = "h76",
            [0x77] = "h77", [0x78] = "h78", [0x79] = "h79", [0x51] = "FieldExit", [0x7F] = "FieldChat",
        };

        public static string OpName(int op) =>
            KnownOpcodes.TryGetValue(op, out var n) ? n : $"op0x{op:x2}";

        /// <summary>Tipo de conexao (1o byte do pacote de login, FUN_0041f6c0).</summary>
        public static class ConnType
        {
            public const byte Pk = 0x01;     // compara nome com this+0x14d
            public const byte Normal = 0x04; // pula a checagem de nome de sessao
        }

        /// <summary>Razoes de desconexao (FUN_0041eb20(this, slot, reason, 1, 1)).</summary>
        public static class DiscReason
        {
            public const byte ServerLocked = 0x12;   // this+0x50 != 0
            public const byte SlotInUse = 0x13;      // user+0x1460 / +0x14a4 != 0 (login duplicado)
            public const byte Field2TooLong = 0x14;  // 2o campo >= 17 (o "DISC 020")
            public const byte Field3TooLong = 0x15;  // 3o campo >= 21
            public const byte GameGuard = 0x18;      // 24 — falha do anti-cheat/GameGuard (opcode 0x10)
            public const byte ClientHash = 0xBC;     // 188 — falha de integridade do binario (Op_VerifyClientHash)
            public const byte UnknownOpcode = 0xC9;  // 201 — default do dispatcher
        }

        /// <summary>Pacote de erro de login (FUN_004038e0(handler, slot, 3, {main,sub})).</summary>
        public static class LoginError
        {
            public const byte Category = 3;
            public const byte Main = 0x0C;       // 12 (opcode Login ecoado)
            public const byte SubIdMismatch = 8; // nome != sessao -> "ID doesn't exist"
            public const byte SubServerFull = 10;// capacidade (this+0x536c) atingida
        }

        /// <summary>Limites de campo do pacote de login (lstrcpyn em FUN_0041f6c0).</summary>
        public static class LoginLimits
        {
            public const int UserIdMax = 0x20;  // nome <= 32 (buffer 0x21 com nul)
            public const int Field2Max = 0x10;  // rejeita se >= 0x11 (17)
            public const int Field3Max = 0x14;  // rejeita se >= 0x15 (21)
        }

        /// <summary>Subtipos do payload (2o u16 do corpo enviado por FUN_0041b940).</summary>
        public static class SubType
        {
            public const ushort LoginComplete = 2;  // login OK (FUN_0041f6c0 sucesso)
            public const ushort Disconnect = 4;     // notify de disconnect (FUN_0041eb20)
        }

        /// <summary>Sub-comandos do canal IPC broker &lt;-&gt; world (BrokenServer/Network/IPCCommand.cs).</summary>
        public static class Ipc
        {
            public const byte RequestServerInfo = 0;
            public const byte RequestLogin = 1;
            public const byte ResponseServerInfo = 2;
            public const byte ResponseLogin = 3;

            public const ushort OpServerInfo = 257;   // 0x0101 — broker le como "info de servidor"
            public const ushort OpServerConn = 1025;  // 0x0401 — broker le como "serv-con"
        }
    }
}
