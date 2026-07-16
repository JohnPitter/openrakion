using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace RakionServer.Common
{
    /// <summary>
    /// Cifra de pacote do canal "lobby/field" do world. Reconstruida da RE de
    /// worldserv.exe: FUN_00401670 e **AES** (implementacao T-table classica, 4
    /// tabelas em 0x442548/0x442948/0x442d48/0x443148, round-keys em ctx+8, nº de
    /// rounds em ctx+4 = 10/12/14 -> AES-128/192/256), ativa quando ctx+0x208 &amp; 1.
    /// O empacotamento (FUN_00401040) processa o plaintext em blocos de 12 bytes e
    /// gera 16 por bloco: cada bloco AES = [4 bytes de ctx+0x20c (IV/contador)] + [12 plaintext].
    ///
    /// Esta classe implementa esse esquema com o AES nativo do .NET (ECB por bloco,
    /// que reproduz o transform T-table do binario). **Pendente:** RE do key-setup
    /// (onde a chave, o IV ctx+0x20c e o flag ctx+0x208 sao inicializados no handshake)
    /// para interoperar com o client real. Enquanto Enabled=false, passa em texto.
    /// </summary>
    public sealed class PacketCrypto
    {
        public bool Enabled { get; private set; }
        private byte[] _key = Array.Empty<byte>();
        private uint _iv;   // 4 bytes que prefixam cada bloco (ctx+0x20c)

        public const int PlainBlock = 12;
        public const int CipherBlock = 16;

        /// <summary>
        /// Chave AES-128 do World Server v258 — constante HARDCODED no worldserv.exe
        /// (FUN_00403c10, montada em local_20 antes de FUN_00401000). Achada por RE.
        /// </summary>
        public static readonly byte[] WorldKey =
        {
            0xE1, 0x3A, 0x7E, 0xF5, 0x37, 0x2C, 0x10, 0x4D,
            0x4E, 0xCE, 0xB3, 0x0C, 0x56, 0x26, 0xA4, 0x8E,
        };

        /// <summary>IV/seed do contexto (ctx+0x20c = 0xc47f), prefixo de 4 bytes de cada bloco.</summary>
        public const uint WorldIv = 0xc47f;

        /// <summary>Liga a cifra com a chave/IV reais do worldserv.exe.</summary>
        public void EnableWorldDefault() => Enable(WorldKey, WorldIv);

        /// <summary>Liga a cifra com a chave (16/24/32 bytes) e o IV de 4 bytes do handshake.</summary>
        public void Enable(byte[] key, uint iv)
        {
            if (key.Length is not (16 or 24 or 32))
                throw new ArgumentException("chave AES deve ter 16/24/32 bytes", nameof(key));
            _key = (byte[])key.Clone();
            _iv = iv;
            Enabled = true;
        }

        public void Disable() => Enabled = false;

        private Aes NewAes()
        {
            var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.ECB;       // o transform do binario e por-bloco (T-table = ECB)
            aes.Padding = PaddingMode.None;
            return aes;
        }

        /// <summary>Cifra: para cada 12 bytes de plaintext gera 16 (= [4B IV][12B] -&gt; AES).</summary>
        public byte[] Encrypt(ReadOnlySpan<byte> plain)
        {
            if (!Enabled) return plain.ToArray();
            int blocks = (plain.Length + PlainBlock - 1) / PlainBlock;
            byte[] outBuf = new byte[blocks * CipherBlock];
            byte[] block = new byte[CipherBlock];
            using var aes = NewAes();
            using var enc = aes.CreateEncryptor();
            for (int i = 0; i < blocks; i++)
            {
                Array.Clear(block);
                // prefixo de 4 bytes = IV CONSTANTE (ctx+0x20c). Confirmado na RE: FUN_00401670
                // escreve so a saida (param_2), NAO atualiza o IV -> sem encadeamento (mesmo prefixo por bloco).
                BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0, 4), _iv);
                int n = Math.Min(PlainBlock, plain.Length - i * PlainBlock);
                plain.Slice(i * PlainBlock, n).CopyTo(block.AsSpan(4));
                enc.TransformBlock(block, 0, CipherBlock, outBuf, i * CipherBlock);
            }
            return outBuf;
        }

        /// <summary>Decifra: cada 16 bytes -&gt; AES inverso -&gt; descarta os 4 bytes de IV, mantem 12.</summary>
        public byte[] Decrypt(ReadOnlySpan<byte> cipher)
        {
            if (!Enabled) return cipher.ToArray();
            int blocks = cipher.Length / CipherBlock;
            byte[] outBuf = new byte[blocks * PlainBlock];
            byte[] block = new byte[CipherBlock];
            using var aes = NewAes();
            using var dec = aes.CreateDecryptor();
            for (int i = 0; i < blocks; i++)
            {
                dec.TransformBlock(cipher.Slice(i * CipherBlock, CipherBlock).ToArray(), 0, CipherBlock, block, 0);
                Array.Copy(block, 4, outBuf, i * PlainBlock, PlainBlock);
            }
            return outBuf;
        }

        public bool TryDecrypt(ReadOnlySpan<byte> cipher, out byte[] plain)
        {
            plain = Array.Empty<byte>();
            if (!Enabled || cipher.Length == 0 || cipher.Length % CipherBlock != 0)
                return false;
            int blocks = cipher.Length / CipherBlock;
            byte[] output = new byte[blocks * PlainBlock];
            byte[] block = new byte[CipherBlock];
            using var aes = NewAes();
            using var dec = aes.CreateDecryptor();
            for (int i = 0; i < blocks; i++)
            {
                byte[] input = cipher.Slice(i * CipherBlock, CipherBlock).ToArray();
                dec.TransformBlock(input, 0, CipherBlock, block, 0);
                if (BinaryPrimitives.ReadUInt32LittleEndian(block) != _iv) return false;
                Array.Copy(block, 4, output, i * PlainBlock, PlainBlock);
            }
            plain = output;
            return true;
        }

        /// <summary>
        /// Auto-teste: Encrypt seguido de Decrypt recupera o payload (padded a 12).
        /// Garante que a implementacao AES + empacotamento esta internamente consistente.
        /// </summary>
        public static bool SelfTest()
        {
            var c = new PacketCrypto();
            c.EnableWorldDefault();
            byte[] msg = { 0x02, 0x00, 0x41, 0x42, 0x43, 0x00, 0x99, 0x12, 0x34, 0x56, 0x78, 0x9a, 0xbc };
            byte[] ct = c.Encrypt(msg);
            if (ct.Length % CipherBlock != 0) return false;
            byte[] back = c.Decrypt(ct);
            for (int i = 0; i < msg.Length; i++)
                if (back[i] != msg[i]) return false;
            return true;
        }
    }
}
