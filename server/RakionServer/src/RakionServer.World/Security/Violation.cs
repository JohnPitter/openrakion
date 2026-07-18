namespace RakionServer.World.Security
{
    /// <summary>
    /// Categoria de violacao observada pelo anti-cheat server-side ("OpenGuard"). Cada
    /// entrada mapeia um sinal que o SERVIDOR consegue observar sozinho (sem agente no
    /// cliente) — integridade do binario, anomalia de protocolo ou flood de pacotes.
    /// </summary>
    public enum ViolationKind : byte
    {
        ClientHashMismatch, // hash do binario reportado != esperado (Op_VerifyClientHash / file.php)
        ClientHashMissing,  // atestacao de integridade ausente quando exigida
        ProtocolSequence,   // seq TCP fora de ordem (ClientSession.DispatchAsync)
        UnknownOpcode,      // opcode fora da tabela de dispatch
        MalformedFrame,     // frame curto/forjado (size invalido ou conteudo insuficiente)
        OpcodeFlood,        // taxa de opcodes TCP acima do teto por janela
        GameplayFlood,      // taxa de pacotes UDP de gameplay acima do teto por janela
        UdpKeyMismatch,     // chave de sessao UDP (user+0x1464) invalida
    }

    /// <summary>Gravidade da violacao — pondera o score acumulado da sessao.</summary>
    public enum ViolationSeverity : byte
    {
        Info = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4,
    }

    /// <summary>
    /// Evento de anti-cheat de uma sessao. Contrato de borda (nao entidade crua nem o
    /// objeto de sessao) — o servico recebe/emite este DTO, nunca o <c>ClientSession</c>.
    /// </summary>
    public readonly record struct Violation(
        ushort Slot,
        string UserId,
        ViolationKind Kind,
        ViolationSeverity Severity,
        string Detail);

    /// <summary>
    /// Decisao SEMANTICA devolvida ao chamador. O servico nao conhece codigos de wire: decide
    /// so "dropar" e/ou "kickar"; o codigo de DISC concreto fica na borda de rede (quem chama).
    /// Assim o dominio nao acopla o protocolo (fonte unica das razoes = <c>Protocol.DiscReason</c>).
    /// </summary>
    public readonly record struct GuardDecision(bool Drop, bool Kick, string Reason)
    {
        /// <summary>Pacote segue o fluxo normal (nenhuma acao).</summary>
        public static readonly GuardDecision Pass = default;

        /// <summary>Descarta o pacote sem desconectar (mitigacao de flood em modo observacao).</summary>
        public static GuardDecision DropOnly(string reason) => new(true, false, reason);

        /// <summary>Descarta o pacote e sinaliza kick — a borda escolhe a razao de DISC.</summary>
        public static GuardDecision Kicked(string reason) => new(true, true, reason);
    }
}
