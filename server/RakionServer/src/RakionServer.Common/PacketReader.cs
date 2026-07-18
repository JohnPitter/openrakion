using System;
using System.Text;

namespace RakionServer.Common
{
    /// <summary>
    /// Lancada quando um parse tenta ler alem do fim do pacote (frame curto ou forjado).
    /// O dispatch a trata como erro de protocolo (DISC limpo), nunca IndexOutOfRange.
    /// </summary>
    public sealed class EndOfPacketException : Exception
    {
        public EndOfPacketException(int pos, int need, int len)
            : base($"PacketReader: leitura alem do fim (pos={pos} need={need} len={len})") { }
    }

    /// <summary>
    /// Leitor de pacotes little-endian sobre um byte[]. Equivalente ao
    /// Systems.PacketReader do broker (Int16/Int32/Byte/Skip), com leitura de
    /// strings prefixadas e strings C (nul-terminated) usadas pelo world.
    /// SEGURO POR CONSTRUCAO: cada leitura valida limites antes (EndOfPacketException),
    /// entao input externo curto/forjado nunca causa IndexOutOfRange.
    /// </summary>
    public sealed class PacketReader
    {
        private readonly byte[] _data;
        private int _pos;

        public PacketReader(byte[] data, int offset = 0)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset > data.Length)
                throw new EndOfPacketException(offset, 0, data.Length);
            _pos = offset;
        }

        public int Position => _pos;
        public int Remaining => _data.Length - _pos;
        public bool CanRead(int n) => n >= 0 && n <= Remaining;

        /// <summary>Garante que ha >= n bytes a ler; senao lanca EndOfPacketException.</summary>
        private void Need(int n)
        {
            if (!CanRead(n))
                throw new EndOfPacketException(_pos, n, _data.Length);
        }

        public byte Byte()
        {
            Need(1);
            return _data[_pos++];
        }

        public short Int16()
        {
            Need(2);
            short v = BitConverter.ToInt16(_data, _pos);
            _pos += 2;
            return v;
        }

        public ushort UInt16()
        {
            Need(2);
            ushort v = BitConverter.ToUInt16(_data, _pos);
            _pos += 2;
            return v;
        }

        public int Int32()
        {
            Need(4);
            int v = BitConverter.ToInt32(_data, _pos);
            _pos += 4;
            return v;
        }

        public uint UInt32()
        {
            Need(4);
            uint v = BitConverter.ToUInt32(_data, _pos);
            _pos += 4;
            return v;
        }

        public void Skip(int n)
        {
            Need(n);
            _pos += n;
        }

        /// <summary>String prefixada por tamanho (1 byte) + ASCII.</summary>
        public string String()
        {
            Need(1);
            int len = _data[_pos++];
            Need(len);
            string s = Encoding.ASCII.GetString(_data, _pos, len);
            _pos += len;
            return s;
        }

        /// <summary>String C terminada em nul; avanca alem do nul. Ja limitada por _data.Length.</summary>
        public string CString(int maxLen = int.MaxValue)
        {
            int start = _pos;
            int end = start;
            long requestedLimit = (long)start + Math.Max(0, maxLen);
            int limit = (int)Math.Min(_data.Length, requestedLimit);
            while (end < limit && _data[end] != 0)
                end++;
            string s = Encoding.ASCII.GetString(_data, start, end - start);
            // posiciona apos o nul (se houver)
            _pos = (end < _data.Length && _data[end] == 0) ? end + 1 : end;
            return s;
        }

        public bool TryCString(int maxLength, out string value)
        {
            value = "";
            if (maxLength < 0 || _pos >= _data.Length) return false;
            int start = _pos;
            int limit = (int)Math.Min(_data.Length, (long)start + maxLength + 1);
            int end = start;
            while (end < limit && _data[end] != 0) end++;
            if (end >= limit || _data[end] != 0 || end - start > maxLength) return false;
            value = Encoding.ASCII.GetString(_data, start, end - start);
            _pos = end + 1;
            return true;
        }

        public byte[] Bytes(int n)
        {
            Need(n);
            byte[] b = new byte[n];
            Array.Copy(_data, _pos, b, 0, n);
            _pos += n;
            return b;
        }
    }
}
