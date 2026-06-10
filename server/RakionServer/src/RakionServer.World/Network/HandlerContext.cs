using RakionServer.Common;

namespace RakionServer.World.Network
{
    /// <summary>Contexto passado a cada handler de opcode (dispatcher FUN_0042ab40).</summary>
    public sealed class HandlerContext
    {
        public readonly WorldServer World;
        public readonly ClientSession User;
        public readonly ushort Opcode;
        public readonly PacketReader P;   // leitor do payload (apos [opcode][seq])
        public readonly byte[] Raw;       // payload cru

        public HandlerContext(WorldServer world, ClientSession user, ushort opcode, PacketReader p, byte[] raw)
        {
            World = world;
            User = user;
            Opcode = opcode;
            P = p;
            Raw = raw;
        }
    }
}
