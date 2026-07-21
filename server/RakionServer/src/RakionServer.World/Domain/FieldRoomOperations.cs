using RakionServer.World.Network;

namespace RakionServer.World.Domain
{
    public enum ForcedTeamChangeResult
    {
        Ignored,
        Denied,
        Changed
    }

    public sealed partial class Field
    {
        private static readonly int[] TeamASeats = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        private static readonly int[] TeamBSeats = { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };

        public bool TryRotateMaster(ClientSession requester, out byte oldSeat, out byte newSeat)
        {
            oldSeat = newSeat = 0xff;
            if (Master != requester || Count < 2) return false;
            var current = FindRec(requester);
            if (current == null) return false;
            PlayerRec? replacement = null;
            for (int offset = 1; offset < Slots.Length; offset++)
            {
                var candidate = Slots[(current.Slot + offset) % Slots.Length];
                if (candidate.Occupied && candidate.Session != null)
                {
                    replacement = candidate;
                    break;
                }
            }
            if (replacement?.Session == null) return false;
            oldSeat = (byte)current.Slot;
            newSeat = (byte)replacement.Slot;
            Master = replacement.Session;
            MasterSlot = replacement.Slot;
            return true;
        }

        public bool TryChangeTeam(ClientSession requester, out byte oldSeat, out byte newSeat)
        {
            oldSeat = newSeat = 0xff;
            var source = FindRec(requester);
            if (source == null) return false;
            int[] candidates = source.Slot < 10 ? TeamBSeats : TeamASeats;
            int candidateIndex = System.Array.FindIndex(candidates,
                seat => Slots[seat].State == 0 && Slots[seat].Session == null);
            if (candidateIndex < 0) return false;
            int targetSeat = candidates[candidateIndex];

            var target = Slots[targetSeat];
            CopyRecord(source, target);
            oldSeat = (byte)source.Slot;
            newSeat = (byte)targetSeat;
            ClearRecord(source);
            requester.FieldSeat = newSeat;
            requester.FieldObjectIndex = newSeat;
            if (Master == requester) MasterSlot = targetSeat;
            ResetLobbyReady();
            return true;
        }

        public ForcedTeamChangeResult ForceChangeTeam(byte sourceSeat, out byte newSeat)
        {
            newSeat = sourceSeat;
            if (sourceSeat >= Slots.Length) return ForcedTeamChangeResult.Ignored;
            PlayerRec source = Slots[sourceSeat];
            if (source.State is not (1 or 2)) return ForcedTeamChangeResult.Ignored;

            int start = sourceSeat < 10 ? 10 : 0;
            int end = sourceSeat < 10 ? Slots.Length : 10;
            int targetSeat = -1;
            for (int seat = start; seat < end; seat++)
            {
                if (Slots[seat].State == 0 && Slots[seat].Session == null)
                {
                    targetSeat = seat;
                    break;
                }
            }
            if (targetSeat < 0) return ForcedTeamChangeResult.Denied;

            ClientSession? session = source.Session;
            CopyRecord(source, Slots[targetSeat]);
            ClearRecord(source);
            newSeat = (byte)targetSeat;
            if (session != null)
            {
                session.FieldSeat = newSeat;
                session.FieldObjectIndex = newSeat;
            }
            if (MasterSlot == sourceSeat) MasterSlot = targetSeat;
            return ForcedTeamChangeResult.Changed;
        }

        public bool TrySetSlotLock(ClientSession requester, byte seat, bool locked)
        {
            if (Master != requester || !IsUsableTeamSeat(seat)) return false;
            var target = Slots[seat];
            if (locked && target.State == 0 && target.Session == null)
            {
                target.State = 5;
                MaxPlayers--;
                return true;
            }
            if (!locked && target.State == 5 && target.Session == null)
            {
                target.State = 0;
                MaxPlayers++;
                return true;
            }
            return false;
        }

        public void ResetLobbyReady()
        {
            foreach (var record in Slots)
                if (record.State == 2) record.State = 1;
        }

        private static bool IsUsableTeamSeat(byte seat) =>
            seat < 18 && seat is not 8 and not 9;

        private static void CopyRecord(PlayerRec source, PlayerRec target)
        {
            target.Session = source.Session;
            target.State = source.State;
            target.WeaponState = source.WeaponState;
            target.Dead = source.Dead;
            target.RoundScore = source.RoundScore;
            target.CounterA = source.CounterA;
            target.CounterB = source.CounterB;
            target.ResultPoints = source.ResultPoints;
            target.VoteState = source.VoteState;
            target.Cause = source.Cause;
        }

        private static void ClearRecord(PlayerRec record)
        {
            record.Session = null;
            record.State = 0;
            record.WeaponState = 1;
            record.Dead = false;
            record.RoundScore = 0;
            record.CounterA = 0;
            record.CounterB = 0;
            record.ResultPoints = 0;
            record.VoteState = 0;
            record.Cause = 0;
        }
    }
}
