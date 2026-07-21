using System;

namespace RakionServer.World.Domain
{
    public readonly record struct DeathReportResult(bool Processed, byte ScoreA, byte ScoreB);

    public sealed partial class Field
    {
        /// <summary>FUN_004087D0: aplica o reporte de morte 0x4F conforme o modo original.</summary>
        public DeathReportResult ApplyReportedDeath(int victimSeat, int killerSeat, byte cause)
        {
            var victim = RecAt(victimSeat);
            if (victim == null || !victim.Playing || victim.Dead) return default;

            victim.Cause = cause;
            return Mode switch
            {
                0 or (byte)GameMode.Golem => ApplyEliminationDeath(victim, killerSeat),
                (byte)GameMode.Deathmatch => ApplyDeathmatchDeath(victim, killerSeat, cause),
                (byte)GameMode.TeamDeath => ApplyTeamDeath(victim, killerSeat, cause),
                (byte)GameMode.Boss => ApplyBossDeath(victim, killerSeat),
                _ => default
            };
        }

        public bool ApplyPlayerDeparture(byte seat)
        {
            PlayerRec? departing = RecAt(seat);
            if (departing == null || departing.State == 0) return false;
            bool wasActive = departing.State is 3 or 4;
            bool wasLeader = seat == LeaderSlotA || seat == LeaderSlotB;
            departing.State = 0;
            if (!wasActive || State != 2 || Phase != MatchPhase.Playing) return false;

            return EndRoundAfterDeparture(seat, wasLeader);
        }

        private bool EndRoundAfterDeparture(byte seat, bool wasLeader)
        {
            if (State != 2 || Phase != MatchPhase.Playing) return false;
            MatchPhase previous = Phase;
            switch (Mode)
            {
                case 0:
                    EndStageRoundIfNeeded();
                    break;
                case (byte)GameMode.Golem:
                    EndEliminationRoundIfNeeded();
                    break;
                case (byte)GameMode.Deathmatch:
                    if (CountActivePlayers() < 2) BeginIndividualRoundEnd();
                    break;
                case (byte)GameMode.TeamDeath:
                    EndTeamRoundAfterDeparture();
                    break;
                case (byte)GameMode.Boss:
                    if (wasLeader) CompleteTeamRound(seat < 10 ? (byte)1 : (byte)0);
                    break;
            }
            return previous != Phase && Phase == MatchPhase.RoundEnd;
        }

        private DeathReportResult ApplyEliminationDeath(PlayerRec victim, int killerSeat)
        {
            victim.Dead = true;
            if (Mode == 0) EndStageRoundIfNeeded();
            else EndEliminationRoundIfNeeded();
            byte killer = (byte)killerSeat;
            return new DeathReportResult(true, killer, killer);
        }

        public bool ApplyStageClear(int senderSeat)
        {
            if (Mode != 0 || State != 2 || Phase != MatchPhase.Playing ||
                senderSeat != MasterSlot)
                return false;

            CompleteTeamRound(0, reason: 2);
            return Phase == MatchPhase.RoundEnd;
        }

        private DeathReportResult ApplyDeathmatchDeath(PlayerRec victim, int killerSeat, byte cause)
        {
            var killer = RecAt(killerSeat);
            if (killer == null || !killer.Playing) return default;

            if (cause == 1)
            {
                if (victim.RoundScore > 0) victim.RoundScore--;
            }
            else
            {
                killer.RoundScore = AddByte(killer.RoundScore, 1);
            }

            if (FragLimit > 0 && killer.RoundScore >= FragLimit) BeginIndividualRoundEnd();
            return new DeathReportResult(true, victim.RoundScore, killer.RoundScore);
        }

        private DeathReportResult ApplyTeamDeath(PlayerRec victim, int killerSeat, byte cause)
        {
            int scoringTeam;
            int delta;
            if (cause is 1 or 8)
            {
                scoringTeam = victim.Team == 0 ? 1 : 0;
                delta = 1;
            }
            else
            {
                scoringTeam = killerSeat < 10 ? 0 : 1;
                delta = 1;
            }

            if (scoringTeam == 0) Score0 = AddByte(Score0, delta);
            else Score1 = AddByte(Score1, delta);

            byte score = scoringTeam == 0 ? Score0 : Score1;
            if (FragLimit > 0 && score >= FragLimit) CompleteTeamRound((byte)scoringTeam);
            return new DeathReportResult(true, Score0, Score1);
        }

        private DeathReportResult ApplyBossDeath(PlayerRec victim, int killerSeat)
        {
            var killer = RecAt(killerSeat);
            byte killerScore = killer?.RoundScore ?? (byte)0;
            if (victim.Slot == LeaderSlotA || victim.Slot == LeaderSlotB)
                CompleteTeamRound(victim.Team == 0 ? (byte)1 : (byte)0);
            return new DeathReportResult(true, victim.RoundScore, killerScore);
        }

        private void EndEliminationRoundIfNeeded()
        {
            bool team0Alive = CountAlive(0) > 0;
            bool team1Alive = CountAlive(1) > 0;
            if (!team0Alive) CompleteTeamRound(1);
            else if (!team1Alive) CompleteTeamRound(0);
        }

        private void EndStageRoundIfNeeded()
        {
            if (CountAlive(0) == 0) CompleteTeamRound(1);
        }

        private int CountActivePlayers()
        {
            int count = 0;
            foreach (PlayerRec record in Slots)
                if (record.State is 3 or 4) count++;
            return count;
        }

        private void EndTeamRoundAfterDeparture()
        {
            int team0 = 0, team1 = 0;
            foreach (PlayerRec record in Slots)
            {
                if (record.State is not (3 or 4)) continue;
                if (record.Team == 0) team0++;
                else team1++;
            }
            if (team0 == 0 && team1 > 0) CompleteTeamRound(1);
            else if (team1 == 0 && team0 > 0) CompleteTeamRound(0);
        }

        private void BeginIndividualRoundEnd()
        {
            RoundEndReason = 1;
            BeginRoundEndTimer();
        }

        private void CompleteTeamRound(byte winningTeam, byte reason = 1)
        {
            if (ObjectiveDecided || Phase != MatchPhase.Playing) return;
            ObjectiveDecided = true;
            RoundEndReason = reason;
            LosingSideWire = winningTeam == 0 ? (byte)1 : (byte)0;
            if (winningTeam == 0) Wins0++;
            else Wins1++;
            foreach (var rec in Slots)
            {
                if (!rec.Playing || rec.Team != winningTeam) continue;
                if (winningTeam == 0) rec.CounterA = AddByte(rec.CounterA, 1);
                else rec.CounterB = AddByte(rec.CounterB, 1);
            }
            BeginRoundEndTimer();
        }

        private void BeginRoundEndTimer() => BeginRoundEndTimer(Environment.TickCount64);

        private void BeginRoundEndTimer(long now)
        {
            Phase = MatchPhase.RoundEnd;
            DeadlineMs = now + 15000;
        }

        private static byte AddByte(byte value, int delta) => unchecked((byte)(value + delta));
    }
}
