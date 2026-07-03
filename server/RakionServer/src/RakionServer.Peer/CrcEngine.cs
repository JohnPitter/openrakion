using System;
using System.Collections.Generic;

namespace RakionServer.Peer
{
    /// <summary>
    /// CRC32 da Serious Engine 1 (Sources/Engine/Base/CRC.{h,cpp}), reimplementado byte-a-byte p/ o handshake
    /// do peer (MSG_REP_CRCCHECK, §5.5 do blueprint). É o CRC-32 clássico REFLETIDO (polinômio 0xEDB88320,
    /// init 0xFFFFFFFF, XOR-final 0xFFFFFFFF, table de 256), idêntico ao do zlib/ZIP. As primitivas espelham a
    /// fonte VERBATIM:
    ///   CRC_Start:   ulCRC = 0xFFFFFFFF
    ///   CRC_AddBYTE: ulCRC = (ulCRC>>8) ^ table[(ulCRC ^ ub) &amp; 0xFF]
    ///   CRC_AddLONG: alimenta os 4 bytes do long em BIG-ENDIAN (ul>>24, >>16, >>8, >>0)  ← detalhe crítico
    ///   CRC_Finish:  ulCRC ^= 0xFFFFFFFF
    ///
    /// CRCT_MakeCRCForFiles_t (CRCTable.cpp): p/ cada nome que o host lista, computa o CRC32 do CONTEÚDO do
    /// arquivo local (GetFileCRC32_t) e combina por CRC_AddLONG. Em self-host os arquivos do bot == os do host
    /// → o CRC bate por construção. Esta classe oferece o CRC de bytes (de um arquivo) e o COMBINADOR da lista.
    /// </summary>
    public static class CrcEngine
    {
        /// <summary>Polinômio refletido do CRC-32 (= forma reversa de 0x04C11DB7; o que gera a table da SE1).</summary>
        private const uint Polynomial = 0xEDB88320;

        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (Polynomial ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        /// <summary>CRC_Start: estado inicial 0xFFFFFFFF.</summary>
        public const uint Start = 0xFFFFFFFF;

        /// <summary>CRC_AddBYTE (VERBATIM): ulCRC = (ulCRC&gt;&gt;8) ^ table[(ulCRC ^ ub) &amp; 0xFF].</summary>
        public static uint AddByte(uint crc, byte ub) => (crc >> 8) ^ Table[(crc ^ ub) & 0xFF];

        /// <summary>CRC_AddLONG (VERBATIM): os 4 bytes do long em BIG-ENDIAN (MSB primeiro).</summary>
        public static uint AddLong(uint crc, uint value)
        {
            crc = AddByte(crc, (byte)(value >> 24));
            crc = AddByte(crc, (byte)(value >> 16));
            crc = AddByte(crc, (byte)(value >> 8));
            crc = AddByte(crc, (byte)(value >> 0));
            return crc;
        }

        /// <summary>CRC_Finish: inverte (XOR 0xFFFFFFFF).</summary>
        public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;

        /// <summary>
        /// CRC32 do CONTEÚDO de um bloco de bytes (= GetFileCRC32_t aplicado ao arquivo): Start → AddByte por
        /// byte → Finish. É o CRC por-arquivo que entra na combinação da lista.
        /// </summary>
        public static uint OfBytes(ReadOnlySpan<byte> data)
        {
            uint crc = Start;
            foreach (byte b in data)
                crc = AddByte(crc, b);
            return Finish(crc);
        }

        /// <summary>
        /// CRCT_MakeCRCForFiles_t (§5.5): combina, na ordem em que o host listou, o CRC32 de cada arquivo local.
        /// <paramref name="fileCrcOf"/> resolve o CRC32 do arquivo de um nome (em self-host == o do host).
        /// Nome desconhecido → CRC 0 (o host fará o mesmo p/ um arquivo ausente; em loopback a lista é a do host).
        /// Combina por CRC_AddLONG e fecha por CRC_Finish.
        /// </summary>
        public static uint CombineFileList(IEnumerable<string> fileNames, Func<string, uint> fileCrcOf)
        {
            uint crc = Start;
            foreach (string name in fileNames)
                crc = AddLong(crc, fileCrcOf(name));
            return Finish(crc);
        }
    }
}
