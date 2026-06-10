using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Canal/IDC do mundo (this+0x60 = quantidade, this+0x64 = ids, this+0x68 = flags).
    /// O handler EnterChannel (opcode 0x01) procura o GroupId do usuario nesta lista.
    /// </summary>
    public sealed class Channel
    {
        public int Id;
        public string Name = "";
        /// <summary>flag em this+0x68[idx]: 0 = normal (status 4), !=0 = PCBang/GM (status 5).</summary>
        public bool Special;

        public Channel(int id, string name = "", bool special = false)
        {
            Id = id;
            Name = name.Length > 0 ? name : $"Channel{id}";
            Special = special;
        }
    }
}
