using System;
using RakionServer.Common;

namespace RakionServer.Peer
{
    /// <summary>
    /// Escritor tipado do corpo de uma CNetworkMessage (espelha os operadores &lt;&lt; e Reinit da SE1,
    /// Sources/Engine/Network/NetworkMessage.{h,cpp}). REUSA o <see cref="PacketWriter"/> (já little-endian;
    /// WriteCString = C-NUL é o formato certo p/ CTString — NÃO usar a WriteString Pascal-prefixada do broker).
    /// Toda CNetworkMessage começa com 1 byte = tipo (Reinit grava (UBYTE)nm_mtType primeiro, §2.5).
    ///
    /// INDEX = SLONG = Int32 4B (x86). Sub-mensagem = [SLONG size][bytes] (InsertSubMessage §3.1).
    /// </summary>
    public sealed class NetWriter : IDisposable
    {
        private readonly PacketWriter _w = new();

        /// <summary>Abre uma mensagem: grava o 1º byte = tipo (Reinit da fonte).</summary>
        public NetWriter(NetworkMessageType type)
        {
            _w.WriteByte((byte)type);
        }

        /// <summary>Abre um corpo SEM byte de tipo (p/ sub-blocos/buffers crus que ganham o tipo por fora).</summary>
        private NetWriter() { }

        /// <summary>Tamanho atual do corpo em bytes (= nm_slSize da fonte).</summary>
        public int Length => _w.Length;

        public NetWriter WriteByte(byte value) { _w.WriteByte(value); return this; }

        public NetWriter WriteU16(ushort value) { _w.WriteWord(value); return this; }

        public NetWriter WriteU32(uint value) { _w.WriteUInt32(value); return this; }

        public NetWriter WriteI16(short value) { _w.WriteInt16(value); return this; }

        public NetWriter WriteI32(int value) { _w.WriteInt32(value); return this; }

        /// <summary>INDEX = SLONG 4B (alias de <see cref="WriteI32"/>; nome do tipo da fonte SE1).</summary>
        public NetWriter WriteIndex(int value) { _w.WriteInt32(value); return this; }

        public NetWriter WriteFloat(float value) { _w.WriteSingle(value); return this; }

        /// <summary>CTString: bytes ASCII + NUL final (operator&lt;&lt;(CTString&amp;), §2.4).</summary>
        public NetWriter WriteCString(string value) { _w.WriteCString(value); return this; }

        public NetWriter WriteBytes(ReadOnlySpan<byte> bytes) { _w.WriteBytes(bytes.ToArray()); return this; }

        /// <summary>
        /// Insere uma sub-mensagem: [SLONG size][bytes] (InsertSubMessage da fonte §3.1). É como um
        /// CNetworkStreamBlock entra num MSG_GAMESTREAMBLOCKS. <paramref name="sub"/> já é o corpo do
        /// sub-bloco (1º byte = tipo do sub).
        /// </summary>
        public NetWriter InsertSubMessage(ReadOnlySpan<byte> sub)
        {
            _w.WriteInt32(sub.Length);   // SLONG size
            _w.WriteBytes(sub.ToArray());
            return this;
        }

        public byte[] ToArray() => _w.ToArray();

        public void Dispose() => _w.Dispose();
    }
}
