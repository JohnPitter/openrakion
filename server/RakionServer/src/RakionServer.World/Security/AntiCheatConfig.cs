namespace RakionServer.World.Security
{
    /// <summary>
    /// Config do anti-cheat server-side (secao <c>[AntiCheat]</c> do worldserver.ini).
    ///
    /// Padrao = MODO OBSERVACAO: o pipeline de deteccao/auditoria fica ligado, mas as
    /// acoes destrutivas (kick, exigir hash) ficam DESLIGADAS para nao atrapalhar o
    /// cliente offline de uso pessoal. Um operador liga a imposicao quando quiser.
    /// </summary>
    public sealed class AntiCheatConfig
    {
        /// <summary>Liga o pipeline de deteccao/auditoria. Desligado = passa tudo direto.</summary>
        public bool Enabled = true;

        /// <summary>Aplica Kick quando o score da sessao cruza <see cref="KickScore"/>.</summary>
        public bool EnforceKick = false;

        /// <summary>Exige atestacao de integridade do binario (Op_VerifyClientHash) — kick no mismatch/ausencia.</summary>
        public bool EnforceClientHash = false;

        /// <summary>Janela do rate-limit de opcodes TCP, em ms.</summary>
        public int OpcodeWindowMs = 1000;

        /// <summary>Teto de opcodes TCP por janela antes de marcar flood.</summary>
        public int MaxOpcodesPerWindow = 120;

        /// <summary>Janela do rate-limit de pacotes UDP de gameplay, em ms.</summary>
        public int GameplayWindowMs = 1000;

        /// <summary>Teto de pacotes UDP de gameplay por janela antes de marcar flood.</summary>
        public int MaxGameplayPerWindow = 400;

        /// <summary>Score acumulado que dispara Kick (quando <see cref="EnforceKick"/>).</summary>
        public int KickScore = 100;
    }
}
