using System;

namespace RakionServer.Peer
{
    /// <summary>
    /// A CNetworkMessage da SE1: um par {tipo lógico + buffer}. O 1º byte do buffer É o tipo (Reinit, §2.5).
    /// Fábrica de leitura/escrita: <see cref="BeginWrite"/> abre um <see cref="NetWriter"/> já com o byte de
    /// tipo; <see cref="BeginRead"/> abre um <see cref="NetReader"/> posicionado APÓS o byte de tipo (que já
    /// foi consumido em <see cref="From"/>). Buffer de ENTRADA grande (disasm: 88405B p/ caber REP_STATEDELTA);
    /// o bot só ESCREVE mensagens pequenas, então a escrita não precisa do buffer grande.
    ///
    /// Builders do corpo de cada mensagem ficam em <see cref="ConnectMessages"/> (síntese do domínio/constantes
    /// nomeadas, nunca replay de blob — CLAUDE.md).
    /// </summary>
    public sealed class NetMessage
    {
        /// <summary>Capacidade de LEITURA (disasm: ctor aloca 0x15955=88405B; aumentado vs. os 2048 da fonte).</summary>
        public const int MaxReadSize = 88405;

        public NetworkMessageType Type { get; }

        /// <summary>Corpo completo incluindo o 1º byte (o tipo).</summary>
        public ReadOnlyMemory<byte> Body { get; }

        private NetMessage(NetworkMessageType type, ReadOnlyMemory<byte> body)
        {
            Type = type;
            Body = body;
        }

        /// <summary>Abre a escrita de uma mensagem nova: grava o byte de tipo e devolve o writer.</summary>
        public static NetWriter BeginWrite(NetworkMessageType type) => new(type);

        /// <summary>
        /// Interpreta um buffer recebido como CNetworkMessage: lê o 1º byte (tipo) e guarda o corpo.
        /// Buffer vazio → tipo 0 e corpo vazio (não estoura). Maior que <see cref="MaxReadSize"/> é truncado
        /// na leitura pelo bounds-check do <see cref="NetReader"/>, mas aqui só registramos o que veio.
        /// </summary>
        public static NetMessage From(ReadOnlyMemory<byte> buffer)
        {
            if (buffer.Length == 0) return new NetMessage((NetworkMessageType)0, buffer);
            return new NetMessage((NetworkMessageType)buffer.Span[0], buffer);
        }

        /// <summary>Reader posicionado logo após o byte de tipo (pronto p/ ler os campos do corpo).</summary>
        public NetReader BeginRead()
        {
            var r = new NetReader(Body);
            r.ReadByte();   // consome o byte de tipo
            return r;
        }
    }
}
