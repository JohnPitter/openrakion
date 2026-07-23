using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Um bot ocupando um assento do <see cref="Field"/>. É um PEER SINTÉTICO server-side: entra no
    /// roster como um jogador (0x38), é movido pela IA e tem o movimento sintetizado no fio (0x030A).
    /// O World mantém a autoridade de HP/morte. A DLL limita-se a apresentar no engine o dano já
    /// confirmado pelo servidor. Nenhum pacote do bot fala direto com o cliente: só via o socket do
    /// UdpGameplay.
    /// </summary>
    public sealed class BotPlayer
    {
        public const int DamageReactionMs = 1800;
        public const int LocomotionRefreshMs = 1200;
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
        public uint DamageSequence { get; private set; }
        public byte LastAttackerSeat { get; private set; } = Field.NoSeat;
        public uint LastAttackerHitSequence { get; private set; }

        // ---- combate server-side: o servidor é a autoridade do HP e da morte do bot ----
        public int MaxHealth { get; private set; } = 100;
        public int Health { get; private set; } = 100;
        public long NextAttackReadyMs;   // cooldown do ataque sintetizado do bot
        public long HitReactionUntilMs;  // evita que o próximo 0x030A apague imediatamente a reação visual
        public long RespawnAtMs { get; private set; }
        private byte _attackVariant;
        private bool _isMoving;
        private long _nextLocomotionRefreshMs;

        /// <summary>HP inicial derivado de level/classe (curva simples; server-authoritative p/ o bot).</summary>
        public void InitHealth(byte level)
        {
            MaxHealth = 100 + level * 10;
            Health = MaxHealth;
            Alive = true;
            RespawnAtMs = 0;
            LifecycleSequence++;
        }

        /// <summary>Aplica dano server-side. Devolve true se o bot morreu neste golpe.</summary>
        public bool TakeDamage(
            int amount, byte attackerSeat = Field.NoSeat, uint attackerHitSequence = 0)
        {
            if (!Alive || amount <= 0) return false;
            DamageSequence++;
            LastAttackerSeat = attackerSeat;
            LastAttackerHitSequence = attackerHitSequence;
            Health -= amount;
            if (Health > 0) return false;
            Health = 0;
            Alive = false;
            LifecycleSequence++;
            return true;
        }

        public void BeginHitReaction(long nowMs)
        {
            HitReactionUntilMs = nowMs + DamageReactionMs;
            Velocity = BotVector.Zero;
            _isMoving = false;
            _nextLocomotionRefreshMs = 0;
            TargetSeat = Field.NoSeat;
            NextAttackReadyMs = Math.Max(NextAttackReadyMs, HitReactionUntilMs);
            _targetVelocityEma = BotVector.Zero;
            _hasTarget = false;
        }

        public bool TryFinishHitReaction(long nowMs)
        {
            if (HitReactionUntilMs == 0 || nowMs < HitReactionUntilMs) return false;
            HitReactionUntilMs = 0;
            return Alive;
        }

        public BotAttackVariant NextAttackVariant()
        {
            BotAttackVariant variant = (BotAttackVariant)(_attackVariant % 3);
            _attackVariant++;
            return variant;
        }

        public bool ShouldPublishLocomotion(bool moving, long nowMs)
        {
            bool changed = _isMoving != moving;
            _isMoving = moving;
            if (!changed && (!moving || nowMs < _nextLocomotionRefreshMs)) return false;
            _nextLocomotionRefreshMs = moving ? nowMs + LocomotionRefreshMs : 0;
            return true;
        }

        public void ScheduleRespawn(long nowMs, int delayMs)
        {
            RespawnAtMs = !Alive && delayMs > 0 ? nowMs + delayMs : 0;
        }

        public bool TryRespawn(long nowMs)
        {
            if (Alive || RespawnAtMs == 0 || nowMs < RespawnAtMs) return false;
            Health = MaxHealth;
            Alive = true;
            RespawnAtMs = 0;
            Velocity = default;
            TargetSeat = Field.NoSeat;
            NextAttackReadyMs = 0;
            HitReactionUntilMs = 0;
            _attackVariant = 0;
            _isMoving = false;
            _nextLocomotionRefreshMs = 0;
            LastAttackerSeat = Field.NoSeat;
            LastAttackerHitSequence = 0;
            _targetVelocityEma = BotVector.Zero;
            _hasTarget = false;
            LifecycleSequence++;
            return true;
        }

        public void ResetForLobby()
        {
            bool revive = !Alive;
            Health = MaxHealth;
            Alive = true;
            Position = BotVector.Zero;
            Velocity = BotVector.Zero;
            Heading = 0;
            TargetSeat = Field.NoSeat;
            MoveSeq = 0;
            NextAttackReadyMs = 0;
            HitReactionUntilMs = 0;
            RespawnAtMs = 0;
            _attackVariant = 0;
            _isMoving = false;
            _nextLocomotionRefreshMs = 0;
            LastAttackerSeat = Field.NoSeat;
            LastAttackerHitSequence = 0;
            _targetVelocityEma = BotVector.Zero;
            _hasTarget = false;
            if (revive) LifecycleSequence++;
        }

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
            if (Position.HorizontalDistanceTo(targetPosition) > 1f) Heading = step.Heading;
            return step.InMelee;
        }
    }
}
