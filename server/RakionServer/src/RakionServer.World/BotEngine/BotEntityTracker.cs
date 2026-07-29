using System.Collections.Concurrent;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.BotEngine;

internal sealed class BotEntityTracker
{
    private readonly ConcurrentDictionary<byte, BotEntityTraceState> _states = new();

    public void Trace(
        Field field,
        IEnumerable<byte> seats,
        IReadOnlyDictionary<byte, BotEnginePlayerSnapshot> snapshots)
    {
        List<BotEntityTransition> transitions = CaptureTransitions(
            field, seats, snapshots);
        foreach (BotEntityTransition transition in transitions)
            Write(field.Id, transition);
    }

    private List<BotEntityTransition> CaptureTransitions(
        Field field,
        IEnumerable<byte> seats,
        IReadOnlyDictionary<byte, BotEnginePlayerSnapshot> snapshots)
    {
        List<BotEntityTransition> transitions = [];
        lock (field.SyncRoot)
        {
            foreach (byte seat in seats)
            {
                BotPlayer? bot = field.RecAt(seat)?.Bot;
                if (bot == null)
                    continue;
                snapshots.TryGetValue(seat, out BotEnginePlayerSnapshot snapshot);
                BotEntityTraceState current = BotEntityTraceState.Capture(
                    field, bot, snapshot);
                if (_states.TryGetValue(seat, out BotEntityTraceState previous) &&
                    previous == current)
                    continue;
                _states[seat] = current;
                transitions.Add(new BotEntityTransition(
                    seat, current, bot.Position, bot.Heading));
            }
        }
        return transitions;
    }

    private static void Write(int fieldId, BotEntityTransition transition)
    {
        BotEntityTraceState state = transition.State;
        Log.Info(
            "bot-entity",
            "field={0} bot={1} phase={2} activity={3} controls={4} " +
            "target={5} nativeReady={6} nativeAlive={7} domainAlive={8} " +
            "hp={9} pos=({10:F2},{11:F2},{12:F2}) heading={13:F3}",
            fieldId,
            transition.Seat,
            state.Phase,
            state.Activity,
            state.Controls,
            state.TargetSeat,
            state.NativeReady,
            state.NativeAlive,
            state.DomainAlive,
            state.Health,
            transition.Position.X,
            transition.Position.Y,
            transition.Position.Z,
            transition.Heading);
    }

    private enum BotEntityActivity : byte
    {
        Paused,
        Idle,
        Moving,
        Attacking,
        HitReaction,
        Dead
    }

    private readonly record struct BotEntityTraceState(
        MatchPhase Phase,
        BotEntityActivity Activity,
        BotControls Controls,
        byte TargetSeat,
        bool NativeReady,
        bool NativeAlive,
        bool DomainAlive,
        int Health)
    {
        public static BotEntityTraceState Capture(
            Field field,
            BotPlayer bot,
            BotEnginePlayerSnapshot snapshot) =>
            new(
                field.Phase,
                ResolveActivity(field, bot),
                bot.EngineControls,
                bot.TargetSeat,
                snapshot.Ready,
                snapshot.Alive,
                bot.Alive,
                bot.Health);

        private static BotEntityActivity ResolveActivity(
            Field field,
            BotPlayer bot)
        {
            if (field.State != 2 || field.Phase != MatchPhase.Playing)
                return BotEntityActivity.Paused;
            if (!bot.Alive)
                return BotEntityActivity.Dead;
            if (bot.HitReactionUntilMs != 0)
                return BotEntityActivity.HitReaction;
            if (bot.EngineAttacking)
                return BotEntityActivity.Attacking;
            return bot.EngineControls != BotControls.None
                ? BotEntityActivity.Moving
                : BotEntityActivity.Idle;
        }
    }

    private readonly record struct BotEntityTransition(
        byte Seat,
        BotEntityTraceState State,
        BotVector Position,
        float Heading);
}
