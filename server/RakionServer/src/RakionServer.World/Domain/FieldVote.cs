using System;
using RakionServer.World.Network;

namespace RakionServer.World.Domain
{
    public enum FieldVoteStatus : byte
    {
        Success = 0,
        AlreadyActive = 1,
        AlreadyVoted = 2,
        TargetMismatch = 3,
        Inactive = 4,
        TargetCannotVote = 5,
        PenaltyTableFull = 6,
        MasterOnly = 7,
        Pending = 8,
        NotEnoughPlayers = 9
    }

    public sealed record FieldVoteFinal(
        byte Result, byte Eligible, byte Yes, byte No, byte Abstain, byte TargetSeat,
        bool PenaltyApplied);

    public sealed record FieldVoteTransition(
        FieldVoteStatus Status, bool Opened, FieldVoteFinal? Final);

    public sealed partial class Field
    {
        private const int VotePenaltyCapacity = 10;
        private const long VoteDurationMs = 60_000;
        private const long VotePenaltyDurationMs = 1_800_000;
        private readonly string?[] _votePenaltyIdentity = new string?[VotePenaltyCapacity];
        private readonly long[] _votePenaltyDeadlineMs = new long[VotePenaltyCapacity];
        private bool _voteActive;
        private byte _votePenaltySlot = VotePenaltyCapacity;
        private byte _voteTargetSeat;
        private string _voteReason = string.Empty;
        private long _voteDeadlineMs;

        public bool VoteActive => _voteActive;
        public byte VoteTargetSeat => _voteTargetSeat;
        public string VoteReason => _voteReason;

        public FieldVoteTransition ProcessVote(
            byte choice, byte senderSeat, byte targetSeat, string? reason, long nowMs)
        {
            if (_voteActive)
            {
                if (choice == 0)
                    return new(FieldVoteStatus.AlreadyActive, false, null);
                return CastVote(choice, senderSeat, _voteTargetSeat, nowMs);
            }

            PlayerRec? sender = RecAt(senderSeat);
            if (sender == null || sender.VoteState != 0)
                return new(FieldVoteStatus.AlreadyVoted, false, null);

            ExpireVotePenalties(nowMs);
            _votePenaltySlot = FindFreePenaltySlot();
            if (_votePenaltySlot == VotePenaltyCapacity)
                return new(FieldVoteStatus.PenaltyTableFull, false, null);
            if (senderSeat != MasterSlot)
                return new(FieldVoteStatus.MasterOnly, false, null);
            if (CountPlaying() < 3)
                return new(FieldVoteStatus.NotEnoughPlayers, false, null);

            _voteActive = true;
            _voteDeadlineMs = nowMs + VoteDurationMs;
            _voteTargetSeat = targetSeat;
            if (reason != null) _voteReason = reason;
            sender.VoteState = 1;
            return new(FieldVoteStatus.Success, true, null);
        }

        public FieldVoteFinal? TickVote(long nowMs)
        {
            ExpireVotePenalties(nowMs);
            return _voteActive ? TallyVote(nowMs) : null;
        }

        public FieldVoteFinal? CancelVoteForDeparture(byte departedSeat)
        {
            if (!_voteActive || departedSeat != _voteTargetSeat) return null;
            foreach (PlayerRec record in Slots) record.VoteState = 0;
            _voteActive = false;
            return new FieldVoteFinal(1, 0, 0, 0, 0, _voteTargetSeat, false);
        }

        public bool IsVotePenalized(ClientSession session, long nowMs)
        {
            ExpireVotePenalties(nowMs);
            string identity = VoteIdentity(session);
            for (int i = 0; i < VotePenaltyCapacity; i++)
                if (_votePenaltyIdentity[i] == identity && _votePenaltyDeadlineMs[i] > nowMs)
                    return true;
            return false;
        }

        private FieldVoteTransition CastVote(
            byte choice, byte senderSeat, byte expectedTarget, long nowMs)
        {
            if (!_voteActive) return new(FieldVoteStatus.Inactive, false, null);
            PlayerRec? sender = RecAt(senderSeat);
            if (sender == null || sender.VoteState != 0)
                return new(FieldVoteStatus.AlreadyVoted, false, null);
            if (_voteTargetSeat != expectedTarget)
                return new(FieldVoteStatus.TargetMismatch, false, null);
            if (senderSeat == _voteTargetSeat)
                return new(FieldVoteStatus.TargetCannotVote, false, null);

            sender.VoteState = choice;
            FieldVoteFinal? final = TallyVote(nowMs);
            return new(FieldVoteStatus.Success, false, final);
        }

        private FieldVoteFinal? TallyVote(long nowMs)
        {
            byte eligible = 0, yes = 0, no = 0, abstain = 0;
            foreach (PlayerRec record in Slots)
            {
                if (!record.Playing) continue;
                eligible++;
                if (record.VoteState == 1) yes++;
                else if (record.VoteState == 2) no++;
                else if (record.VoteState == 3) abstain++;
            }
            if (nowMs < _voteDeadlineMs && yes + no + abstain < eligible - 1)
                return null;
            return FinalizeVote(eligible, yes, no, abstain, nowMs);
        }

        private FieldVoteFinal FinalizeVote(
            byte eligible, byte yes, byte no, byte abstain, long nowMs)
        {
            foreach (PlayerRec record in Slots) record.VoteState = 0;
            PlayerRec? target = RecAt(_voteTargetSeat);
            bool passed = eligible <= (yes + no) * 2 && no < yes && target?.State != 0;
            if (passed && target?.Session != null && _votePenaltySlot < VotePenaltyCapacity)
            {
                _votePenaltyIdentity[_votePenaltySlot] = VoteIdentity(target.Session);
                _votePenaltyDeadlineMs[_votePenaltySlot] = nowMs + VotePenaltyDurationMs;
            }
            var result = new FieldVoteFinal(0, eligible, yes, no, abstain, _voteTargetSeat, passed);
            _voteActive = false;
            return result;
        }

        private byte FindFreePenaltySlot()
        {
            for (byte i = 0; i < VotePenaltyCapacity; i++)
                if (_votePenaltyIdentity[i] == null) return i;
            return VotePenaltyCapacity;
        }

        private void ExpireVotePenalties(long nowMs)
        {
            for (int i = 0; i < VotePenaltyCapacity; i++)
            {
                if (_votePenaltyIdentity[i] == null || _votePenaltyDeadlineMs[i] >= nowMs) continue;
                _votePenaltyIdentity[i] = null;
                _votePenaltyDeadlineMs[i] = 0;
            }
        }

        private static string VoteIdentity(ClientSession session) =>
            !string.IsNullOrEmpty(session.UserId) ? session.UserId :
            session.Game != null ? session.Game.UserId.ToString() : $"slot:{session.Slot}";
    }
}
