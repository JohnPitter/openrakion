using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Codec do MOVIMENTO/AÇÃO do bot. Converte o estado de IA do <see cref="BotPlayer"/> (posição,
    /// orientação, ação) na mensagem de gameplay que o cliente já renderiza. Isola o ÚNICO ponto gated
    /// do sistema de bots: o WRAPPER externo do datagrama UDP. O CORPO da mensagem está decodificado
    /// byte-a-byte (engine.dll, ver docs/bot-movement-capture.md); síntese da posição do bot, nunca relay.
    ///
    /// MOVE+AÇÃO = CNetMessage **0x30a** (CPlayerSource::SendAction_Relay @engine.dll 0x103cb0; recv
    /// CSessionState::GetActionFromMessage @0x10afe0). Corpo (19B), na ordem de CNetMessage::Write:
    ///   [u16 dt][u8 (actState&lt;&lt;5)|slot][u8 0][s16 x][s16 y][s16 z][s16 heading][u8 flag][s16 ax][s16 ay][s16 az]
    ///   x/y/z e ax/ay/az = PackFloatToSWord(coord) = (short)(coord/0.01) = coord*100. heading = short cru.
    /// CANAL = UDP UNRELIABLE (CNet::SendData_Unreliable @0x1000e0), por-peer a cada player state==3 ≠ si.
    /// ⇒ o bot emite pelo socket UdpGameplay p/ o endpoint de cada humano, NÃO pelo relay TCP 0x4b.
    /// </summary>
    public static class BotMovement
    {
        /// <summary>Tipo CNetMessage de move+ação (CPlayerSource::SendAction → SendData_Unreliable(0x30a)).</summary>
        public const ushort MsgAction = 0x030a;

        /// <summary>Quantização de posição: coord = short*SCALE; SCALE=_DAT_3621acac=0.01 ⇒ short=coord*100.</summary>
        private const float PosScale = 0.01f;

        /// <summary>True quando o WRAPPER do datagrama UDP foi confirmado (1 captura) e validado in-game.
        /// Enquanto false, <see cref="TryBuildActionDatagram"/> devolve null e o bot não anda/ataca.</summary>
        public static bool UdpFramingKnown => false;

        /// <summary>
        /// TRAVA-MESTRA de TODO frame do bot que vai ao cliente (spawn 0x45, move 0x30a, morte 0x4f).
        /// false = bot 100% server-side (existe no domínio, conta na partida, é limpo no fim) e NENHUM
        /// byte é enviado ao cliente — assim /addbot, limpeza e re-add são testáveis SEM risco de crash.
        /// Só ligar quando o roster (info do player no slot) e o datagrama estiverem validados por captura
        /// in-game — mandar frame de um seat que o cliente não conhece pode travar o cliente (lição-mestra).
        /// </summary>
        public static bool ClientFramesEnabled => false;

        /// <summary>
        /// Corpo CNetMessage 0x30a (19B) da posição/ação do bot — DECODIFICADO (não gated). É o payload
        /// que o destino lê em GetActionFromMessage. O <paramref name="seat"/> vai nos 5 bits baixos do
        /// byte de ação (identifica o autor no relay). <paramref name="actState"/> = 3 bits altos.
        /// </summary>
        public static byte[] EncodeActionBody(BotPlayer bot, int seat, byte actState = 0)
        {
            using var w = new PacketWriter();
            w.WriteWord(0);                                   // u16 dt (delta de tempo; 0 = sem interpolação)
            w.WriteByte((byte)((actState << 5) | (seat & 0x1f)));  // u8 (actState<<5)|slot
            w.WriteByte(0);                                   // u8 reservado
            w.WriteInt16(Pack(bot.X));                        // s16 x
            w.WriteInt16(Pack(bot.Y));                        // s16 y
            w.WriteInt16(Pack(bot.Z));                        // s16 z
            w.WriteInt16((short)bot.Yaw);                     // s16 heading (cru)
            w.WriteByte(0);                                   // u8 flag (jump/estado; 0 = no chão)
            w.WriteInt16(Pack(bot.AimX));                     // s16 action vec x
            w.WriteInt16(Pack(bot.AimY));                     // s16 action vec y
            w.WriteInt16(Pack(bot.AimZ));                     // s16 action vec z
            return w.ToArray();
        }

        /// <summary>
        /// Datagrama UDP completo (FUN_36100ef0 @engine.dll 0x100ef0 + SendData_Unreliable):
        ///   [u16 msgType][u32 seq][u8 srcSlot][CNetMessage body 19B]  = 26B.
        /// msgType = 0x30a (bit 0x8000 = reliable; unreliable não seta). seq = contador por-sender. O
        /// alvo NÃO entra no pacote (é só o endereço do SendTo). O corpo está cravado (golden test).
        ///
        /// GATED em <see cref="UdpFramingKnown"/>: o wrapper está decodificado, mas só LIGAR após 1 captura
        /// golden-confirmar byte-a-byte (a forma do datagrama, seq inicial, e se o cliente ACEITA um pacote
        /// de gameplay vindo do servidor/endereço — validação de origem). Mandar forma não-vista pode
        /// crashar o cliente (lição-mestra do projeto). Até lá, null = no-op.
        /// </summary>
        public static byte[]? TryBuildActionDatagram(BotPlayer bot, int seat)
        {
            if (!UdpFramingKnown) return null;
            byte[] body = EncodeActionBody(bot, seat);
            using var w = new PacketWriter();
            w.WriteWord(MsgAction);          // u16 msgType 0x30a (unreliable; |0x8000 se reliable)
            w.WriteUInt32(bot.UdpSeq++);     // u32 seq (contador por-sender; *(CNet+4)++ no original)
            w.WriteByte((byte)seat);         // u8 srcSlot (autor da ação)
            w.WriteBytes(body);              // CNetMessage 0x30a (19B)
            return w.ToArray();              // 26B
        }

        private static short Pack(float coord) =>
            (short)System.Math.Clamp(coord / PosScale, short.MinValue, short.MaxValue);
    }
}
