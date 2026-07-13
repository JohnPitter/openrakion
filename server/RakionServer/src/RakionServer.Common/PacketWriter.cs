using System;
using System.IO;
using System.Text;

namespace RakionServer.Common
{
    /// <summary>
    /// Escritor de pacotes little-endian. Equivalente ao IPCPacket do broker
    /// (WriteByte/WriteWord/WriteString/AddCRC), generalizado para o world.
    /// Strings "Pascal" sao prefixadas por 1 byte de tamanho (formato do broker).
    /// </summary>
    public sealed class PacketWriter : IDisposable
    {
        private readonly MemoryStream _ms = new();
        private readonly BinaryWriter _bw;

        public PacketWriter() => _bw = new BinaryWriter(_ms);

        public int Length => (int)_ms.Length;

        public PacketWriter WriteByte(int value) { _bw.Write((byte)value); return this; }

        public PacketWriter WriteWord(int value) { _bw.Write((ushort)value); return this; }

        public PacketWriter WriteInt16(int value) { _bw.Write((short)value); return this; }

        public PacketWriter WriteInt32(int value) { _bw.Write(value); return this; }

        public PacketWriter WriteUInt32(uint value) { _bw.Write(value); return this; }

        /// <summary>float32 little-endian IEEE-754 (posicoes/orientacao do protocolo do world).</summary>
        public PacketWriter WriteSingle(float value) { _bw.Write(value); return this; }

        /// <summary>String prefixada por tamanho (1 byte) + ASCII, como no broker.</summary>
        public PacketWriter WriteString(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s ?? "");
            _bw.Write((byte)b.Length);
            _bw.Write(b);
            return this;
        }

        /// <summary>String C terminada em nul (formato do protocolo do world).</summary>
        public PacketWriter WriteCString(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s ?? "");
            _bw.Write(b);
            _bw.Write((byte)0);
            return this;
        }

        /// <summary>Buffer fixo de tamanho size, preenchido com nul (campo char[N]).</summary>
        public PacketWriter WriteFixed(string s, int size)
        {
            byte[] buf = new byte[size];
            byte[] b = Encoding.ASCII.GetBytes(s ?? "");
            Array.Copy(b, buf, Math.Min(b.Length, size));
            _bw.Write(buf);
            return this;
        }

        public PacketWriter WriteBytes(byte[] bytes) { _bw.Write(bytes); return this; }

        /// <summary>Anexa o BCRC (XOR de todo o conteudo) como ultimo byte.</summary>
        public PacketWriter AddCrc()
        {
            _bw.Flush();
            byte crc = IpcCodec.BCRC(_ms.ToArray());
            _bw.Write(crc);
            return this;
        }

        public byte[] ToArray()
        {
            _bw.Flush();
            return _ms.ToArray();
        }

        public void Dispose()
        {
            _bw.Dispose();
            _ms.Dispose();
        }
    }
}
