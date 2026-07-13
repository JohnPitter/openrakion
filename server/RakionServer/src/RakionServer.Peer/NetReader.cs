using System;
using System.Buffers.Binary;
using System.Text;

namespace RakionServer.Peer
{
    /// <summary>
    /// Leitor bounds-checked do corpo de uma CNetworkMessage (espelha CNetworkMessage::Read,
    /// Sources/Engine/Network/NetworkMessage.cpp; = read-helper @0x36006250 do disasm). SEGURO POR
    /// CONSTRUÇÃO (CLAUDE.md): cada leitura confere o limite ANTES de copiar; em over-read NÃO lança
    /// IndexOutOfRange — marca <see cref="Overflowed"/>, devolve default e congela o cursor (igual à
    /// fonte: "Warning: Message over-reading!" → memset(0) → return). Frame curto/forjado = erro tratado.
    ///
    /// Little-endian (x86: os operadores >> da SE1 são memcpy de sizeof). INDEX = SLONG = Int32 4B.
    /// CTString = bytes até o NUL (a fonte lê char-a-char até 0x00 ou fim da msg).
    /// </summary>
    public sealed class NetReader
    {
        private readonly ReadOnlyMemory<byte> _buf;
        private int _pos;

        public NetReader(ReadOnlyMemory<byte> buffer)
        {
            _buf = buffer;
        }

        public NetReader(byte[] buffer) : this(buffer.AsMemory()) { }

        /// <summary>Posição do cursor (bytes já consumidos).</summary>
        public int Position => _pos;

        /// <summary>Bytes ainda não lidos.</summary>
        public int Remaining => _buf.Length - _pos;

        /// <summary>True após qualquer leitura que ultrapassou o fim do buffer (contrato da fonte).</summary>
        public bool Overflowed { get; private set; }

        /// <summary>
        /// Confere o limite antes de uma leitura de <paramref name="size"/> bytes (§2.3). Compara contra o
        /// restante SEM somar (size forjado perto de int.MaxValue estouraria _pos+size e burlaria o check).
        /// </summary>
        private bool CanRead(int size)
        {
            if (size < 0 || size > _buf.Length - _pos)
            {
                Overflowed = true;
                return false;
            }
            return true;
        }

        public byte ReadByte()
        {
            if (!CanRead(1)) return 0;
            return _buf.Span[_pos++];
        }

        public ushort ReadU16()
        {
            if (!CanRead(2)) return 0;
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_buf.Span.Slice(_pos, 2));
            _pos += 2;
            return v;
        }

        public uint ReadU32()
        {
            if (!CanRead(4)) return 0;
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buf.Span.Slice(_pos, 4));
            _pos += 4;
            return v;
        }

        public short ReadI16()
        {
            if (!CanRead(2)) return 0;
            short v = BinaryPrimitives.ReadInt16LittleEndian(_buf.Span.Slice(_pos, 2));
            _pos += 2;
            return v;
        }

        /// <summary>SLONG (Int32 4B little-endian) — também o tipo de INDEX na SE1 x86.</summary>
        public int ReadI32()
        {
            if (!CanRead(4)) return 0;
            int v = BinaryPrimitives.ReadInt32LittleEndian(_buf.Span.Slice(_pos, 4));
            _pos += 4;
            return v;
        }

        /// <summary>INDEX = SLONG 4B (alias de <see cref="ReadI32"/>; nome do tipo da fonte SE1).</summary>
        public int ReadIndex() => ReadI32();

        public float ReadFloat()
        {
            if (!CanRead(4)) return 0f;
            float v = BinaryPrimitives.ReadSingleLittleEndian(_buf.Span.Slice(_pos, 4));
            _pos += 4;
            return v;
        }

        /// <summary>
        /// CTString: lê bytes ASCII até o NUL (ou fim do buffer). Espelha operator&gt;&gt;(CTString&amp;) da
        /// fonte (lê char-a-char até 0x00 ou nm_slSize). Sem prefixo de tamanho. Buffer esgotado sem NUL
        /// devolve o que leu e marca <see cref="Overflowed"/>.
        /// </summary>
        public string ReadCString()
        {
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _buf.Length)
                {
                    Overflowed = true;
                    return sb.ToString();
                }
                byte b = _buf.Span[_pos++];
                if (b == 0) return sb.ToString();
                sb.Append((char)b);
            }
        }

        /// <summary>
        /// Sub-mensagem [SLONG size][size bytes] (ExtractSubMessage da fonte §3.1). Devolve a fatia crua;
        /// o 1º byte dela é o tipo do sub-bloco. size negativo/maior que o restante → <see cref="Overflowed"/>
        /// e fatia vazia (nunca estoura).
        /// </summary>
        public ReadOnlyMemory<byte> ReadSubMessage()
        {
            int size = ReadI32();
            if (Overflowed || size < 0 || size > _buf.Length - _pos)
            {
                Overflowed = true;
                return ReadOnlyMemory<byte>.Empty;
            }
            var slice = _buf.Slice(_pos, size);
            _pos += size;
            return slice;
        }

        /// <summary>Lê <paramref name="count"/> bytes crus. Falta de bytes → fatia vazia + overflow.</summary>
        public ReadOnlyMemory<byte> ReadBytes(int count)
        {
            if (!CanRead(count)) return ReadOnlyMemory<byte>.Empty;
            var slice = _buf.Slice(_pos, count);
            _pos += count;
            return slice;
        }
    }
}
