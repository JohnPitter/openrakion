using System;
using System.Text;

namespace RakionServer.Common
{
    /// <summary>
    /// Codec do canal IPC broker &lt;-&gt; world (UDP). Golden source: a logica e
    /// identica a do broker original (BrokenServer/Network/Servers.cs), extraida
    /// para ser compartilhada pelos dois lados sem duplicacao.
    ///
    /// - BCRC: checksum XOR de todos os bytes (1 byte, anexado no fim do pacote).
    /// - IPCenCode/IPCdeCode: cifra XOR simetrica com chave (campo "code" do servidor).
    ///   Os 3 primeiros bytes do pacote nunca sao cifrados (sao a "semente" b).
    ///   Quando code == "" nao ha cifra (caso padrao da nossa stack).
    /// </summary>
    public static class IpcCodec
    {
        public static byte BCRC(byte[] bytes) => BCRC(bytes, bytes.Length);

        public static byte BCRC(byte[] bytes, int len)
        {
            byte b = 0;
            for (int i = 0; i < len; i++)
                b ^= bytes[i];
            return b;
        }

        /// <summary>Verifica a integridade de um pacote IPC: como a BCRC final é o XOR de todos os bytes
        /// anteriores, o XOR do pacote INTEIRO (incluindo o byte de CRC) é 0 quando íntegro. Usado p/ separar
        /// IPC legítimo do gameplay UDP que compartilha a mesma porta (gameplay/forjado dá XOR != 0).</summary>
        public static bool VerifyCrc(byte[] bytes) => bytes.Length >= 1 && BCRC(bytes, bytes.Length) == 0;

        public static void Encode(byte[] data, string code) => Encode(data, code, data.Length);

        public static void Encode(byte[] data, string code, int len)
        {
            if (string.IsNullOrEmpty(code))
                return;

            int k = 0;
            byte seed = (byte)(data[0] ^ data[1] ^ data[2]);
            byte[] key = Encoding.ASCII.GetBytes(code);
            for (int i = 3; i < len; i++)
            {
                data[i] = (byte)((data[i] ^ key[k++] ^ 150) ^ i ^ seed);
                if (k >= key.Length)
                    k = 0;
            }
        }

        // A cifra e simetrica: decodificar == codificar.
        public static void Decode(byte[] data, string code) => Encode(data, code, data.Length);

        public static void Decode(byte[] data, string code, int len) => Encode(data, code, len);
    }
}
