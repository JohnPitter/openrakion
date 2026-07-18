namespace RakionServer.World.Domain
{
    /// <summary>
    /// Um bot ocupando um assento do <see cref="Field"/>. É um PEER SINTÉTICO server-side: entra no
    /// roster como um jogador (0x38), é movido pela IA e tem o movimento sintetizado no fio (0x030A).
    /// Teto arquitetural conhecido do RE: entrega oponente FUNCIONAL (roster, movimento, dano/morte
    /// server-side) mas NÃO o número cosmético HIT×N nativo — este exige um peer de sessão real
    /// sincronizado (limite type-7). Nenhum pacote do bot fala direto com o cliente: só via o socket
    /// do UdpGameplay.
    /// </summary>
    public sealed class BotPlayer
    {
        public string Name { get; init; } = "";
        public byte Level { get; init; } = 1;
        public byte CharClass { get; init; } = 1;
        public byte Team { get; init; }
        public byte Seat { get; set; } = Field.NoSeat;
        public BotDifficulty Difficulty { get; init; } = BotDifficulty.Normal;
        public BotProfile Profile { get; init; } = BotProfile.Normal;

        public BotVector Position;
        public BotVector Velocity;
        public float Heading;
        public bool Alive = true;
        public byte TargetSeat = Field.NoSeat;
        public uint MoveSeq;    // sequência crescente do 0x030A sintetizado (o cliente ecoa/ordena)
        public uint LifecycleSequence { get; private set; }

        // ---- combate server-side (o servidor é a autoridade do HP do bot; teto RE: sem HIT×N nativo) ----
        public int MaxHealth { get; private set; } = 100;
        public int Health { get; private set; } = 100;
        public long NextAttackReadyMs;   // cooldown do ataque sintetizado do bot
        public long HitReactionUntilMs;  // evita que o próximo 0x030A apague imediatamente a reação visual

        /// <summary>HP inicial derivado de level/classe (curva simples; server-authoritative p/ o bot).</summary>
        public void InitHealth(byte level)
        {
            MaxHealth = 100 + level * 10;
            Health = MaxHealth;
            Alive = true;
            LifecycleSequence++;
        }

        /// <summary>Aplica dano server-side. Devolve true se o bot morreu neste golpe.</summary>
        public bool TakeDamage(int amount)
        {
            if (!Alive || amount <= 0) return false;
            Health -= amount;
            if (Health > 0) return false;
            Health = 0;
            Alive = false;
            LifecycleSequence++;
            return true;
        }

        public void BeginHitReaction(long nowMs) => HitReactionUntilMs = nowMs + 300;

        // Estado interno da IA: EMA da velocidade do alvo (antecipação) e a última posição observada.
        private BotVector _targetVelocityEma;
        private BotVector _lastTargetPosition;
        private bool _hasTarget;

        /// <summary>Registra a posição atual do alvo e atualiza a EMA de velocidade (antecipação).</summary>
        public void ObserveTarget(BotVector targetPosition, float dt)
        {
            if (_hasTarget && dt > 1e-4f)
            {
                BotVector sample = (targetPosition - _lastTargetPosition) * (1f / dt);
                _targetVelocityEma = BotSteering.SmoothVelocity(_targetVelocityEma, sample, 0.4f);
            }
            _lastTargetPosition = targetPosition;
            _hasTarget = true;
        }

        /// <summary>Avança a IA um passo em direção ao alvo observado; atualiza posição/velocidade/rumo.</summary>
        public bool Tick(BotVector targetPosition, float dt)
        {
            if (!Alive) return false;
            ObserveTarget(targetPosition, dt);
            BotStep step = BotSteering.Step(
                Position, Velocity, Profile, targetPosition, _targetVelocityEma, dt);
            Position = step.Position;
            Velocity = step.Velocity;
            Heading = step.Heading;
            return step.InMelee;
        }
    }
}
