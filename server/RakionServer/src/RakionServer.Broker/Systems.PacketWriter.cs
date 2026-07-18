using System;
using System.IO;

namespace BrokenServer
{
    public partial class Systems
    {
        private sealed class PacketWriter : IDisposable
        {
            private readonly MemoryStream _stream = new MemoryStream();
            private readonly BinaryWriter _writer;

            public PacketWriter(ushort opcode)
            {
                _writer = new BinaryWriter(_stream);
                _writer.Write((ushort)0);
                _writer.Write(opcode);
            }

            public void WriteByte(byte value) => _writer.Write(value);

            public void WriteUInt16(ushort value) => _writer.Write(value);

            public byte[] ToArray()
            {
                if (_stream.Length > ushort.MaxValue)
                    throw new InvalidOperationException("Pacote do Broker excede o tamanho do wire.");

                long end = _stream.Position;
                _stream.Position = 0;
                _writer.Write((ushort)end);
                _stream.Position = end;
                _writer.Flush();
                return _stream.ToArray();
            }

            public void Dispose()
            {
                _writer.Dispose();
                _stream.Dispose();
            }
        }
    }
}
