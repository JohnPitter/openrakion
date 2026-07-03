using System;

namespace RakionServer.Peer
{
    /// <summary>
    /// Trilha de diagnóstico FINA do mini-peer (categoria 'peer'). Domínio isolado de I/O (CLAUDE.md): o slice
    /// NÃO conhece o logger do servidor — recebe um <see cref="Sink"/> por injeção (o World liga em Log.Ok/Debug).
    /// Sem sink, é no-op (testes não poluem). Existe p/ o PRÓXIMO teste in-game cravar o ponto exato do stall:
    /// cada ESTADO do handshake, cada frame ENVIADO/RECEBIDO (tipo+role+offset+len+hex curto) e o PORQUÊ de parar.
    /// </summary>
    public static class PeerTrace
    {
        /// <summary>Destino da trilha (o World liga em Log). Null = desligado.</summary>
        public static Action<string>? Sink { get; set; }

        /// <summary>Emite uma linha de trilha (no-op se não há sink).</summary>
        public static void Emit(string line) => Sink?.Invoke(line);

        /// <summary>Formata uma linha de trilha só se há sink (evita o custo de string.Format quando desligado).</summary>
        public static void Emit(string format, params object[] args)
        {
            var sink = Sink;
            if (sink != null) sink(args.Length > 0 ? string.Format(format, args) : format);
        }

        /// <summary>True quando a trilha está ligada (permite pular trabalho de formatação caro).</summary>
        public static bool Enabled => Sink != null;

        /// <summary>Hex curto de um frame p/ a trilha (até <paramref name="max"/> bytes; sufixo "…" se truncado).</summary>
        public static string ShortHex(ReadOnlySpan<byte> data, int max = 24)
        {
            int n = Math.Min(max, data.Length);
            var sb = new System.Text.StringBuilder(n * 2 + 4);
            for (int i = 0; i < n; i++) sb.Append(data[i].ToString("x2"));
            if (data.Length > n) sb.Append('…');
            return sb.ToString();
        }
    }
}
