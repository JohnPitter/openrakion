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
        private BotControls _lastPublishedControls;
        private readonly BotNavigationPlanner _navigation = new();
        private float _verticalVelocity;
        private float _groundY;
        private bool _airborne;
        private bool _hasGround;
        private bool _movementBlocked;
        private long _nextLocomotionRefreshMs;
        public bool IsMoving => _isMoving;
        public BotNavigationMode NavigationMode { get; private set; }

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
            TargetSeat = Field.NoSeat;
            NextAttackReadyMs = Math.Max(NextAttackReadyMs, HitReactionUntilMs);
            _targetVelocityEma = BotVector.Zero;
            _hasTarget = false;
            ResetNavigation();
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

        public bool ShouldPublishLocomotion(bool moving)
        {
            if (_isMoving == moving) return false;
            _isMoving = moving;
            return true;
        }

        public bool ShouldPublishControls(BotControls controls, long nowMs)
        {
            const int LocomotionRefreshMilliseconds = 800;
            const BotControls movement = BotControls.W | BotControls.A |
                BotControls.S | BotControls.D | BotControls.Space;
            BotControls current = controls & movement;
            bool refresh = current != BotControls.None &&
                nowMs >= _nextLocomotionRefreshMs;
            if (_lastPublishedControls == current && !refresh) return false;
            _lastPublishedControls = current;
            _isMoving = (current & ~BotControls.Space) != 0;
            _nextLocomotionRefreshMs = current == BotControls.None
                ? 0
                : nowMs + LocomotionRefreshMilliseconds;
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
            _lastPublishedControls = BotControls.None;
            LastAttackerSeat = Field.NoSeat;
            LastAttackerHitSequence = 0;
            _targetVelocityEma = BotVector.Zero;
            _hasTarget = false;
            ResetNavigation();
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
            _lastPublishedControls = BotControls.None;
            LastAttackerSeat = Field.NoSeat;
            LastAttackerHitSequence = 0;
            _targetVelocityEma = BotVector.Zero;
            _hasTarget = false;
            ResetNavigation();
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

        public BotNavigationAction TickNavigated(
            BotVector targetPosition,
            byte targetSeat,
            byte mapId,
            long nowMs,
            float dt,
            IBotNavigationSurface surface)
        {
            if (!Alive) return default;
            ObserveTarget(targetPosition, dt);
            BotVector aimed = targetPosition +
                _targetVelocityEma * Profile.Anticipation;
            BotNavigationAction action = _navigation.Update(
                new BotNavigationInput(
                    nowMs,
                    targetSeat,
                    Position,
                    aimed,
                    Profile.MeleeRange,
                    Profile.MeleeSpacing,
                    Seat % 2 == 0,
                    _movementBlocked));
            NavigationMode = action.Mode;
            ApplyControls(action, aimed, mapId, dt, surface);
            if (Position.HorizontalDistanceTo(aimed) > 1f)
                Heading = Position.HeadingTo(aimed);
            return action;
        }

        private void ApplyControls(
            BotNavigationAction action,
            BotVector target,
            byte mapId,
            float dt,
            IBotNavigationSurface surface)
        {
            BotVector desired = ResolveDesiredVelocity(action.Controls, target);
            float acceleration = Math.Clamp(Profile.Acceleration, 0f, 1f);
            Velocity += (desired - Velocity) * acceleration;
            BotVector proposed = Position + Velocity * dt;
            BotMoveResolution resolution = surface.Resolve(mapId, Position, proposed);
            _movementBlocked = resolution.Blocked;
            Velocity = dt > 1e-4f
                ? (resolution.Position - Position) * (1f / dt)
                : BotVector.Zero;
            Position = resolution.Position;
            ApplyVerticalMotion(action.IsJumping, dt);
        }

        private BotVector ResolveDesiredVelocity(
            BotControls controls,
            BotVector target)
        {
            BotVector forward = new(
                target.X - Position.X, 0, target.Z - Position.Z);
            forward = forward.Normalized();
            BotVector right = new(-forward.Z, 0, forward.X);
            float forwardAxis =
                controls.HasFlag(BotControls.W) ? 1f :
                controls.HasFlag(BotControls.S) ? -1f : 0f;
            float strafeAxis =
                controls.HasFlag(BotControls.D) ? 1f :
                controls.HasFlag(BotControls.A) ? -1f : 0f;
            BotVector direction = (forward * forwardAxis + right * strafeAxis)
                .Normalized();
            return direction * Profile.MoveSpeed;
        }

        private void ApplyVerticalMotion(bool jump, float dt)
        {
            if (!_hasGround)
            {
                _groundY = Position.Y;
                _hasGround = true;
            }
            if (jump && !_airborne)
            {
                _airborne = true;
                _verticalVelocity = 600f;
            }
            if (!_airborne) return;

            _verticalVelocity -= 1600f * dt;
            float y = Position.Y + _verticalVelocity * dt;
            if (y <= _groundY)
            {
                y = _groundY;
                _verticalVelocity = 0;
                _airborne = false;
            }
            Position = Position with { Y = y };
        }

        private void ResetNavigation()
        {
            _navigation.Reset();
            NavigationMode = BotNavigationMode.Idle;
            _verticalVelocity = 0;
            _airborne = false;
            _hasGround = false;
            _movementBlocked = false;
            _nextLocomotionRefreshMs = 0;
        }
    }
}
