using System;

namespace RakionServer.World.Domain
{
    public sealed class BotNavigationPlanner
    {
        private const float ProgressThreshold = 45f;
        private const float MovementThreshold = 30f;
        private const float ManeuverMovementThreshold = 75f;
        private const float ManeuverDistanceRegression = 100f;
        private const int StuckMilliseconds = 1600;
        private const int DiagonalMilliseconds = 6000;
        private const int StrafeMilliseconds = 5000;
        private const int BackstepMilliseconds = 1200;
        private const int JumpCycleMilliseconds = 900;
        private const int JumpWindowMilliseconds = 180;

        private byte _targetSeat = Field.NoSeat;
        private float _bestDistance;
        private BotVector _lastPosition;
        private long _lastProgressAt;
        private long _maneuverStartedAt;
        private long _maneuverUntil;
        private BotVector _maneuverStart;
        private float _maneuverStartDistance;
        private BotVector _bypassDirection;
        private uint _failedAttempts;
        private float _side;
        private BotNavigationMode _mode;

        public BotNavigationAction Update(BotNavigationInput input)
        {
            if (input.TargetSeat == Field.NoSeat)
            {
                Reset();
                return default;
            }
            if (_targetSeat != input.TargetSeat) BeginTracking(input);

            float distance = input.Position.HorizontalDistanceTo(input.Target);
            bool moved = input.Position.HorizontalDistanceTo(_lastPosition) >= MovementThreshold;
            _lastPosition = input.Position;
            TrackProgress(input.NowMs, distance);

            if (distance >= input.MinimumSpacing && distance <= input.AttackRange)
                return EnterAttack(input.NowMs);
            if (distance < input.MinimumSpacing)
                return SetAction(BotControls.S, BotNavigationMode.Backward);
            if (input.NowMs < _maneuverUntil)
                return BuildManeuverAction(input);

            if (IsBypassing())
                CompleteManeuver(input, distance);
            else if (input.MovementBlocked ||
                     !moved && input.NowMs - _lastProgressAt >= StuckMilliseconds)
            {
                StartBypass(input, distance);
                return BuildManeuverAction(input);
            }

            return SetAction(BotControls.W, BotNavigationMode.Approach);
        }

        public void Reset()
        {
            _targetSeat = Field.NoSeat;
            _bestDistance = 0;
            _lastPosition = default;
            _lastProgressAt = 0;
            _maneuverStartedAt = 0;
            _maneuverUntil = 0;
            _maneuverStart = default;
            _maneuverStartDistance = 0;
            _bypassDirection = default;
            _failedAttempts = 0;
            _side = 0;
            _mode = BotNavigationMode.Idle;
        }

        private void BeginTracking(BotNavigationInput input)
        {
            Reset();
            _targetSeat = input.TargetSeat;
            _bestDistance = input.Position.HorizontalDistanceTo(input.Target);
            _lastPosition = input.Position;
            _lastProgressAt = input.NowMs;
            _side = input.PreferLeft ? -1f : 1f;
            _mode = BotNavigationMode.Approach;
        }

        private void TrackProgress(long nowMs, float distance)
        {
            if (distance > _bestDistance - ProgressThreshold) return;
            _bestDistance = distance;
            _lastProgressAt = nowMs;
        }

        private BotNavigationAction EnterAttack(long nowMs)
        {
            _maneuverUntil = 0;
            _lastProgressAt = nowMs;
            return SetAction(BotControls.Attack, BotNavigationMode.Attack);
        }

        private void StartBypass(BotNavigationInput input, float distance)
        {
            uint strategy = _failedAttempts++ % 4;
            _maneuverStartedAt = input.NowMs;
            _lastProgressAt = input.NowMs;
            _bestDistance = distance;
            _maneuverStart = input.Position;
            _maneuverStartDistance = distance;
            _bypassDirection = ResolveRight(input.Position, input.Target) * _side;

            if (strategy < 2)
                BeginManeuver(input.NowMs, DiagonalMilliseconds,
                    BotNavigationMode.BypassDiagonal);
            else if (strategy == 2)
                BeginManeuver(input.NowMs, StrafeMilliseconds,
                    BotNavigationMode.BypassStrafe);
            else
                BeginManeuver(input.NowMs, BackstepMilliseconds,
                    BotNavigationMode.Backstep);
        }

        private void BeginManeuver(long nowMs, int duration, BotNavigationMode mode)
        {
            _mode = mode;
            _maneuverUntil = nowMs + duration;
        }

        private void CompleteManeuver(BotNavigationInput input, float distance)
        {
            float displacement = input.Position.HorizontalDistanceTo(_maneuverStart);
            bool regressed = distance >
                _maneuverStartDistance + ManeuverDistanceRegression;
            if (displacement < ManeuverMovementThreshold || regressed)
                _side = -_side;

            _mode = BotNavigationMode.Approach;
            _lastProgressAt = input.NowMs;
            _bestDistance = distance;
        }

        private BotNavigationAction BuildManeuverAction(BotNavigationInput input)
        {
            BotControls side = ResolveSideControl(input.Position, input.Target);
            BotControls controls = _mode switch
            {
                BotNavigationMode.BypassDiagonal => BotControls.W | side,
                BotNavigationMode.BypassStrafe => side,
                BotNavigationMode.Backstep => BotControls.S | side,
                _ => BotControls.None
            };
            if (ShouldJump(input.NowMs)) controls |= BotControls.Space;
            return new BotNavigationAction(controls, _mode);
        }

        private BotControls ResolveSideControl(BotVector position, BotVector target)
        {
            BotVector right = ResolveRight(position, target);
            float alignment = right.X * _bypassDirection.X +
                              right.Z * _bypassDirection.Z;
            return alignment >= 0 ? BotControls.D : BotControls.A;
        }

        private bool ShouldJump(long nowMs) =>
            (nowMs - _maneuverStartedAt) % JumpCycleMilliseconds <
            JumpWindowMilliseconds;

        private bool IsBypassing() =>
            _mode is BotNavigationMode.BypassDiagonal or
                BotNavigationMode.BypassStrafe or BotNavigationMode.Backstep;

        private BotNavigationAction SetAction(
            BotControls controls, BotNavigationMode mode)
        {
            _mode = mode;
            return new BotNavigationAction(controls, mode);
        }

        private static BotVector ResolveRight(BotVector position, BotVector target)
        {
            BotVector forward = new(
                target.X - position.X, 0, target.Z - position.Z);
            float length = MathF.Sqrt(
                forward.X * forward.X + forward.Z * forward.Z);
            return length <= 0.001f
                ? new BotVector(1, 0, 0)
                : new BotVector(-forward.Z / length, 0, forward.X / length);
        }
    }
}
