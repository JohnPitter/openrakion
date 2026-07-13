using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Nível de inteligência/dificuldade do bot. <see cref="Easy"/> = oponente fraco, lento e passivo
    /// (recua, combos curtos); <see cref="Hard"/> = rápido, agressivo, combos longos, prevê o alvo e
    /// orbita no melee. Escolhido no comando da sala (ex.: "/addbot hard", "/addbot facil 2").
    /// </summary>
    public enum BotDifficulty : byte { Easy = 0, Normal = 1, Hard = 2 }

    /// <summary>
    /// Perfil de IA do bot por dificuldade — agrupa TODOS os parâmetros ajustáveis do motor de IA num
    /// contrato explícito (CLAUDE.md: parâmetros correlatos viram um record, não constantes mágicas
    /// espalhadas pelo motor). O <see cref="WorldServer"/> lê o perfil do bot a cada tick; trocar de
    /// dificuldade é trocar de perfil, sem ramificar o motor.
    /// </summary>
    /// <param name="Difficulty">O nível ao qual este perfil pertence.</param>
    /// <param name="DecisionIntervalMs">Cadência de (re)decisão de alvo/intenção. Menor = reage mais rápido.</param>
    /// <param name="MoveSpeed">Velocidade de deslocamento em coord/tick (motor a 10 Hz).</param>
    /// <param name="Acceleration">Aceleração em coord/tick²: quão rápido atinge a velocidade plena. Humano
    /// arranca e freia, não teleporta — valor baixo = partida/parada mais suave.</param>
    /// <param name="ComboHitGapMs">Espaçamento entre golpes DENTRO de um combo.</param>
    /// <param name="ComboRecoveryMs">Descanso após terminar um combo (não golpeia "sem parar").</param>
    /// <param name="ReactionDelayMs">Atraso "humano" antes de iniciar um combo ao avistar o alvo (Hard ~0).</param>
    /// <param name="EngageRange">(Modo Golem) distância p/ LARGAR o objetivo e lutar o humano próximo.</param>
    /// <param name="StrafeBias">0..1 — quanto orbita o alvo no melee em vez de ficar parado (humano nunca
    /// fica imóvel num duelo). 0 = encara parado; 1 = orbita na velocidade plena.</param>
    /// <param name="LeadFactor">0..1 — quanto antecipa o movimento do alvo (mira/persegue à frente dele).
    /// Inteligência de antecipação: Easy não prevê, Hard lidera bastante.</param>
    /// <param name="RetreatHpPct">Abaixo deste % de HP, o bot recua/prioriza objetivo em vez de trocar golpes
    /// (jogo inteligente). 0 = nunca recua (facetank do Easy).</param>
    /// <param name="MaxHp">Energia do bot: oponente mais durável em dificuldade alta.</param>
    /// <param name="ComboPool">Índices (em <c>BotComboPatterns</c>) dos combos permitidos neste nível —
    /// Easy só cadeias curtas; Hard cadeias longas + especiais.</param>
    public sealed record BotProfile(
        BotDifficulty Difficulty,
        long DecisionIntervalMs,
        float MoveSpeed,
        float Acceleration,
        long ComboHitGapMs,
        long ComboRecoveryMs,
        long ReactionDelayMs,
        float EngageRange,
        float StrafeBias,
        float LeadFactor,
        float RetreatHpPct,
        ushort MaxHp,
        int[] ComboPool)
    {
        /// <summary>Perfil canônico de cada dificuldade. Valores derivados da captura (velocidade ~0.75
        /// coord/tick e cadência ~100 ms do humano real) escalados por nível.</summary>
        public static BotProfile For(BotDifficulty d) => d switch
        {
            BotDifficulty.Easy => new(
                d, DecisionIntervalMs: 450, MoveSpeed: 0.34f, Acceleration: 0.08f,
                ComboHitGapMs: 430, ComboRecoveryMs: 2300, ReactionDelayMs: 380,
                EngageRange: 8f, StrafeBias: 0.15f, LeadFactor: 0f, RetreatHpPct: 0f,
                MaxHp: 280, ComboPool: new[] { 2, 0 }),

            BotDifficulty.Hard => new(
                d, DecisionIntervalMs: 120, MoveSpeed: 0.58f, Acceleration: 0.16f,
                ComboHitGapMs: 190, ComboRecoveryMs: 1000, ReactionDelayMs: 60,
                EngageRange: 22f, StrafeBias: 0.45f, LeadFactor: 0.6f, RetreatHpPct: 0.25f,
                MaxHp: 520, ComboPool: new[] { 1, 4, 3, 0 }),

            _ => new(
                BotDifficulty.Normal, DecisionIntervalMs: 250, MoveSpeed: 0.46f, Acceleration: 0.13f,
                ComboHitGapMs: 300, ComboRecoveryMs: 1600, ReactionDelayMs: 180,
                EngageRange: 15f, StrafeBias: 0.32f, LeadFactor: 0.3f, RetreatHpPct: 0.12f,
                MaxHp: 400, ComboPool: new[] { 0, 2, 3 }),
        };

        /// <summary>Reconhece o token de dificuldade num comando ("/addbot hard"). PT e EN; default Normal.</summary>
        public static BotDifficulty Parse(string text)
        {
            if (text.Contains("hard") || text.Contains("dificil") || text.Contains("difícil") || text.Contains("insano"))
                return BotDifficulty.Hard;
            if (text.Contains("easy") || text.Contains("facil") || text.Contains("fácil") || text.Contains("noob"))
                return BotDifficulty.Easy;
            return BotDifficulty.Normal;
        }
    }
}
