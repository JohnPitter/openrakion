using System;

namespace RakionServer.Peer
{
    /// <summary>
    /// O CPlayerCharacter da SE1 (Sources/Engine/Entities/PlayerCharacter.{h,cpp}) serializado para o WIRE de
    /// rede — o que o host lê via <c>operator&gt;&gt;(CNetworkMessage&amp;, CPlayerCharacter&amp;)</c> ao processar
    /// um MSG_REQ_CONNECTPLAYER (cliente→host) e o que viaja no MSG_SEQ_ADDPLAYER (host→session-states).
    ///
    /// LAYOUT DE WIRE (FATO, fonte SE1 PlayerCharacter.cpp:198-204 — caminho CNetworkMessage, NÃO o de arquivo):
    ///   [CTString pc_strName][CTString pc_strTeam][16 × UBYTE pc_aubGUID][32 × UBYTE pc_aubAppearance]
    /// IMPORTANTE: o caminho de REDE NÃO escreve o magic "PLC4" — esse só aparece em Read_t/Write_t (stream de
    /// ARQUIVO, PlayerCharacter.cpp:107/118). No fio é só nome+team+GUID+appearance, CRU (sem prefixo de bloco).
    ///
    /// CTString = bytes ASCII + NUL (operator&lt;&lt;(CTString&amp;) escreve char-a-char até 0). GUID e appearance
    /// são buffers fixos copiados por <c>Write(buf, N)</c> = N bytes crus.
    ///
    /// Contrato explícito de borda (CLAUDE.md): o domínio (bot por nome/classe/level) traduz para este DTO; não
    /// trafega entidade crua. O <see cref="Appearance"/> é o blob proprietário do Rakion (gear/cores/modelo) —
    /// 32B no formato SE1 stock; a EXTENSÃO Rakion vive DENTRO desses 32B (ver nota abaixo).
    /// </summary>
    public readonly struct PlayerCharacter
    {
        /// <summary>Tamanho fixo do GUID (PLAYERGUIDSIZE da SE1).</summary>
        public const int GuidSize = 16;

        /// <summary>Tamanho fixo do appearance (MAX_PLAYERAPPEARANCE da SE1).</summary>
        public const int AppearanceSize = 32;

        /// <summary>pc_strName — nome do personagem (CTString). Vazio vira "&lt;unnamed player&gt;" no host stock.</summary>
        public string Name { get; }

        /// <summary>pc_strTeam — time do personagem (CTString). "" se sem time.</summary>
        public string Team { get; }

        /// <summary>pc_aubGUID[16] — identificador único e estável (sobrevive a rename). Sempre 16 bytes.</summary>
        public ReadOnlyMemory<byte> Guid { get; }

        /// <summary>pc_aubAppearance[32] — blob de aparência (modelo/gear/cores). Sempre 32 bytes.</summary>
        public ReadOnlyMemory<byte> Appearance { get; }

        /// <summary>
        /// Monta o DTO normalizando os buffers fixos: <paramref name="guid"/> e <paramref name="appearance"/> são
        /// truncados/zero-preenchidos para 16/32 bytes (igual ao memset+Read da fonte). Nome/time aceitam "".
        /// </summary>
        public PlayerCharacter(string name, string team, ReadOnlySpan<byte> guid, ReadOnlySpan<byte> appearance)
        {
            Name = name ?? "";
            Team = team ?? "";
            Guid = FixedBuffer(guid, GuidSize);
            Appearance = FixedBuffer(appearance, AppearanceSize);
        }

        /// <summary>Buffer de tamanho fixo: copia até <paramref name="size"/> bytes da origem, resto fica 0.</summary>
        private static byte[] FixedBuffer(ReadOnlySpan<byte> src, int size)
        {
            var buf = new byte[size];
            src.Slice(0, Math.Min(src.Length, size)).CopyTo(buf);
            return buf;
        }

        /// <summary>
        /// Serializa no corpo de uma CNetworkMessage na ORDEM da fonte (operator&lt;&lt;): name, team, GUID(16),
        /// appearance(32). Sem "PLC4". O <paramref name="w"/> já está posicionado (não escreve byte de tipo).
        /// </summary>
        public void WriteTo(NetWriter w)
        {
            w.WriteCString(Name);
            w.WriteCString(Team);
            w.WriteBytes(Guid.Span);
            w.WriteBytes(Appearance.Span);
        }

        /// <summary>
        /// Desserializa (operator&gt;&gt;): name, team, GUID(16), appearance(32). Frame curto → campos parciais +
        /// reader.Overflowed marcado (segurança por construção). Usado p/ ler o appearance REAL de uma captura.
        /// </summary>
        public static PlayerCharacter ReadFrom(NetReader r)
        {
            string name = r.ReadCString();
            string team = r.ReadCString();
            byte[] guid = r.ReadBytes(GuidSize).ToArray();
            byte[] appearance = r.ReadBytes(AppearanceSize).ToArray();
            return new PlayerCharacter(name, team, guid, appearance);
        }
    }
}
