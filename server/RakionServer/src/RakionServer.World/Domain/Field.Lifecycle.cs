namespace RakionServer.World.Domain
{
    public enum MatchLifecycleEvent : byte
    {
        None,
        EngageStarted,
        RoundTimedOut,
        NextRoundStarted,
        MatchEnded
    }

    public readonly record struct MatchLifecycleTransition(MatchLifecycleEvent Event, byte Reason = 0);

    public sealed partial class Field
    {
        public MatchLifecycleTransition AdvanceLifecycle(long now)
        {
            if (State != 2 || now < DeadlineMs) return default;

            return Phase switch
            {
                MatchPhase.Pre => AdvanceEngageTimeout(now),
                MatchPhase.Playing when Mode != 0 => AdvancePlayingTimeout(now),
                MatchPhase.RoundEnd => AdvanceRoundEnd(now),
                _ => default
            };
        }

        private MatchLifecycleTransition AdvanceEngageTimeout(long now)
        {
            if (Mode == 0)
            {
                foreach (PlayerRec record in Slots)
                    if (record.State == 3) record.State = 1;

                if (!HasPlayerInState(4)) return EndLifecycleMatch(1);
            }
            else if (!HasPlayerInState(4))
            {
                return default;
            }

            StartRound(now);
            return new(MatchLifecycleEvent.EngageStarted);
        }

        private MatchLifecycleTransition AdvancePlayingTimeout(long now)
        {
            RoundEndReason = 0;
            switch ((GameMode)Mode)
            {
                case GameMode.Golem:
                    CompleteTimedTeamRound(Compare(ObjectivePairA, ObjectivePairB), now);
                    break;
                case GameMode.TeamDeath:
                    CompleteTimedTeamRound(Compare(Score0, Score1), now);
                    break;
                case GameMode.Boss:
                    CompleteTimedTeamRound(Compare(BossTargetA, BossTargetB), now);
                    break;
                case GameMode.Deathmatch:
                    BeginRoundEndTimer(now);
                    break;
            }
            return new(MatchLifecycleEvent.RoundTimedOut);
        }

        private MatchLifecycleTransition AdvanceRoundEnd(long now)
        {
            Round++;
            if (Round > MaxRounds) return EndLifecycleMatch(2);

            int team0 = CountActive(0);
            int team1 = CountActive(1);
            if (Mode is (byte)GameMode.Golem or (byte)GameMode.TeamDeath or (byte)GameMode.Boss)
            {
                if (team0 == 0 || team1 == 0) return EndLifecycleMatch(5);
            }
            else if (Mode == (byte)GameMode.Deathmatch && team0 + team1 < 2)
            {
                return EndLifecycleMatch(6);
            }

            StartRound(now);
            return new(MatchLifecycleEvent.NextRoundStarted);
        }

        private void CompleteTimedTeamRound(byte winner, long now)
        {
            ObjectiveDecided = true;
            if (winner < 2)
            {
                LosingSideWire = winner == 0 ? (byte)1 : (byte)0;
                if (winner == 0) Wins0++;
                else Wins1++;
            }
            else
            {
                LosingSideWire = 2;
            }
            BeginRoundEndTimer(now);
        }

        private MatchLifecycleTransition EndLifecycleMatch(byte reason)
        {
            EndMatch(reason);
            return new(MatchLifecycleEvent.MatchEnded, reason);
        }

        private bool HasPlayerInState(byte state)
        {
            foreach (PlayerRec record in Slots)
                if (record.State == state) return true;
            return false;
        }

        private int CountActive(int team)
        {
            int count = 0;
            foreach (PlayerRec record in Slots)
                if (record.State is 3 or 4 && record.Team == team) count++;
            return count;
        }

        private static byte Compare(long first, long second) =>
            first > second ? (byte)0 : second > first ? (byte)1 : (byte)2;
    }
}
