using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Peer de bot no field, dirigido pelo Bot Engine Host. O World mantém autoridade de roster,
    /// HP, morte e respawn; posição/heading/colisão/animação nativa vêm do Host. Nenhum pacote do
    /// bot fala direto com o cliente: só via o socket do UdpGameplay.
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
        public uint MoveSeq;
        public uint LifecycleSequence { get; private set; }
        public uint DamageSequence { get; private set; }
        public byte LastAttackerSeat { get; private set; } = Field.NoSeat;
        public uint LastAttackerHitSequence { get; private set; }
        public PlayerCombatState Combat { get; } = new();
        private uint _attackSequence;

        public int MaxHealth { get; private set; } = 100;
        public int Health { get; private set; } = 100;
        public long NextAttackReadyMs;
        public long HitReactionUntilMs;
        public long RespawnAtMs { get; private set; }
        private byte _attackVariant;
        private bool _isMoving;
        private bool _staggerToggle;
        private BotControls _lastPublishedControls;
        private long _nextLocomotionRefreshMs;
        public bool IsMoving => _isMoving;
        public bool EngineAttached { get; private set; }
        public BotControls EngineControls { get; private set; }
        public bool EngineAttacking { get; private set; }
        private byte _engineAimTargetSeat = Field.NoSeat;
        private BotVector _lastEngineAimTarget;

        public void InitHealth(byte level)
        {
            MaxHealth = 100 + level * 10;
            Health = MaxHealth;
            Alive = true;
            RespawnAtMs = 0;
            LifecycleSequence++;
        }

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

        /// <summary>
        /// Reação do golpe atual. Golpe que não derruba alterna as duas poses de recuo, como a
        /// vítima humana faz na captura; o golpe fatal usa a queda.
        /// </summary>
        public BotDamageReaction NextDamageReaction(bool died)
        {
            if (died) return BotDamageReaction.Knockdown;
            _staggerToggle = !_staggerToggle;
            return _staggerToggle
                ? BotDamageReaction.StaggerA
                : BotDamageReaction.StaggerB;
        }

        public void BeginHitReaction(long nowMs)
        {
            HitReactionUntilMs = nowMs + DamageReactionMs;
            Velocity = BotVector.Zero;
            _isMoving = false;
            TargetSeat = Field.NoSeat;
            NextAttackReadyMs = Math.Max(NextAttackReadyMs, HitReactionUntilMs);
            EngineControls = BotControls.None;
            EngineAttacking = false;
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

        public bool TryStartAttack(long nowMs)
        {
            if (!Alive || nowMs < NextAttackReadyMs)
                return false;
            NextAttackReadyMs = nowMs + Profile.AttackCooldownMs;
            // Impacto imediato + janela larga: resolve no mesmo tick e tolera latência do Host.
            return Combat.TryOpenAttack(
                ++_attackSequence, nowMs, impactDelayMs: 0, activeDurationMs: 600);
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
            _attackSequence = 0;
            Combat.Reset();
            _isMoving = false;
            _lastPublishedControls = BotControls.None;
            LastAttackerSeat = Field.NoSeat;
            LastAttackerHitSequence = 0;
            EngineControls = BotControls.None;
            EngineAttacking = false;
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
            _attackSequence = 0;
            Combat.Reset();
            _isMoving = false;
            _lastPublishedControls = BotControls.None;
            LastAttackerSeat = Field.NoSeat;
            LastAttackerHitSequence = 0;
            EngineControls = BotControls.None;
            EngineAttacking = false;
            if (revive) LifecycleSequence++;
        }

        public void ApplyEngineTransform(BotVector position, float heading)
        {
            Position = position;
            Heading = heading;
        }

        public void AttachEngine()
        {
            EngineAttached = true;
        }

        public void SetEngineIntent(BotControls controls, bool attacking)
        {
            EngineControls = controls;
            EngineAttacking = attacking;
        }

        public bool ShouldRefreshEngineAim(byte targetSeat, BotVector target)
        {
            if (_engineAimTargetSeat == targetSeat &&
                _lastEngineAimTarget.HorizontalDistanceTo(target) <= 5f)
                return false;
            _engineAimTargetSeat = targetSeat;
            _lastEngineAimTarget = target;
            return true;
        }
    }
}
