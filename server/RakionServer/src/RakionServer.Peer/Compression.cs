using System;
using System.IO;
using System.IO.Compression;

namespace RakionServer.Peer
{
    /// <summary>
    /// Descompressão zlib do REP_STATEDELTA (§5.6 do blueprint). A SE1 empacota o state-delta com CzlibCompressor
    /// (zlib/DEFLATE) e o peer faz comp.UnpackStream_t p/ ler. O bot DESCARTA o conteúdo (não simula mundo), mas
    /// PRECISA consumir o frame p/ o cursor do stream avançar ao CRC. .NET tem zlib nativo (<see cref="ZLibStream"/>).
    ///
    /// Tolerante: se o blob não for zlib (o host pode mandar não-comprimido p/ o cliente local iClient==0, R2),
    /// devolve os bytes crus — o objetivo é só drenar o frame, não interpretá-lo.
    /// </summary>
    public static class Compression
    {
        /// <summary>
        /// Tenta inflar um blob zlib; em falha (não-comprimido/corrompido) devolve o input intacto. Nunca lança:
        /// o caller só quer drenar o REP_STATEDELTA, que será descartado.
        /// </summary>
        public static byte[] TryUnpackZlib(ReadOnlyMemory<byte> compressed)
        {
            try
            {
                using var src = new MemoryStream(compressed.ToArray());
                using var zlib = new ZLibStream(src, CompressionMode.Decompress);
                using var dst = new MemoryStream();
                zlib.CopyTo(dst);
                return dst.ToArray();
            }
            catch
            {
                return compressed.ToArray();
            }
        }
    }
}
