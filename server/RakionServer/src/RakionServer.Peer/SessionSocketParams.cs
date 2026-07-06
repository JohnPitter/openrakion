namespace RakionServer.Peer
{
    /// <summary>
    /// CSessionSocketParams da SE1 (Sources/Engine/Network/SessionSocket.h): 3 INDEX serializados em ordem
    /// pelo operator&lt;&lt;(CNetworkMessage&amp;, CSessionSocketParams&amp;). Casa o disasm (ConnectRemote lê
    /// ssp_iMaxBPS/iMinBPS em +0x40/+0x44 do client). DTO de borda (contrato explícito, não entidade crua).
    ///
    /// Em loopback/self-host não há throttle real: BufferActions plausível (4) e BPS altos (1e6). 12 bytes.
    /// </summary>
    /// <param name="BufferActions">ssp_iBufferActions — nº de ações em buffer por pacote.</param>
    /// <param name="MaxBps">ssp_iMaxBPS — teto de bytes/s do canal (alto em loopback).</param>
    /// <param name="MinBps">ssp_iMinBPS — piso de bytes/s do canal.</param>
    public readonly record struct SessionSocketParams(int BufferActions, int MaxBps, int MinBps)
    {
        /// <summary>Tamanho serializado fixo: 3 × INDEX (SLONG 4B).</summary>
        public const int SerializedSize = 12;

        /// <summary>Default plausível p/ loopback/self-host (sem throttle: BPS = 1.000.000).</summary>
        public static SessionSocketParams Loopback => new(BufferActions: 4, MaxBps: 1_000_000, MinBps: 1_000_000);

        /// <summary>Escreve os 3 INDEX em ordem (= operator&lt;&lt; da fonte).</summary>
        public void WriteTo(NetWriter w)
        {
            w.WriteIndex(BufferActions);
            w.WriteIndex(MaxBps);
            w.WriteIndex(MinBps);
        }

        /// <summary>Lê os 3 INDEX em ordem (= operator&gt;&gt; da fonte).</summary>
        public static SessionSocketParams ReadFrom(NetReader r)
        {
            int buffer = r.ReadIndex();
            int max = r.ReadIndex();
            int min = r.ReadIndex();
            return new SessionSocketParams(buffer, max, min);
        }
    }
}
