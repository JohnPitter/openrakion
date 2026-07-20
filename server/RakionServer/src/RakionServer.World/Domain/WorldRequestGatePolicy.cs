namespace RakionServer.World.Domain
{
    public enum RequestGateAction : byte
    {
        Allow,
        Disconnect,
        ReplyStatus,
    }

    public readonly record struct RequestGateResult(RequestGateAction Action, ushort Code = 0)
    {
        public bool Allowed => Action == RequestGateAction.Allow;
    }

    public static class WorldRequestGatePolicy
    {
        private enum CharacterGate : byte { Any, Unselected, Selected }

        private readonly record struct Contract(
            CharacterGate Character, byte? Status, ushort IdentityFailure,
            RequestGateResult PhaseFailure);

        public static RequestGateResult Evaluate(
            ushort opcode, int gameInfoId, int activeCharacterId, byte status)
        {
            Contract? contract = ContractFor(opcode);
            if (contract == null) return new(RequestGateAction.Allow);

            Contract value = contract.Value;
            bool identityValid = gameInfoId > 0 && value.Character switch
            {
                CharacterGate.Unselected => activeCharacterId <= 0,
                CharacterGate.Selected => activeCharacterId > 0,
                _ => true,
            };
            if (!identityValid)
                return new(RequestGateAction.Disconnect, value.IdentityFailure);
            if (value.Status.HasValue && status != value.Status.Value)
                return value.PhaseFailure;
            return new(RequestGateAction.Allow);
        }

        private static Contract? ContractFor(ushort opcode) => opcode switch
        {
            0x0E => Unselected(0x16), 0x12 => Unselected(0x19),
            0x13 => Unselected(0x1C), 0x14 => Unselected(0x1D),
            0x0F => Account(0x1A), 0x15 => Account(0x1F), 0x19 => Account(0x28),
            0x1A => Account(0x2A), 0x1B => Account(0x2B), 0x1C => Account(0x2C),
            0x2C => InPhase(2, 0x32, 0x33), 0x2D => InPhase(2, 0x34, 0x35),
            0x2E => InPhase(2, 0x36, 0x37), 0x2F => InPhase(2, 0x39, 0x3A),
            0x31 => InPhase(2, 0x3C, 0x3D), 0x32 => InPhase(2, 0x3F, 0x40),
            0x35 => InPhase(2, 0x41, 0x42), 0x36 => InPhase(2, 0x46, 0x47),
            0x38 => new(CharacterGate.Selected, 2, 0x4A,
                new(RequestGateAction.ReplyStatus, 5)),
            0x39 => InPhase(2, 0x4E, 0x4F), 0x3A => InPhase(2, 0x50, 0x51),
            0x3B => InPhase(2, 0x52, 0x53), 0x48 => InPhase(3, 0x81, 0x82),
            0x4A => InPhase(3, 0x83, 0x84), 0x4B => InPhase(3, 0x86, 0x87),
            0x4F => InPhase(3, 0x8F, 0x90), 0x53 => InPhase(3, 0x98, 0x99),
            0x6B => Selected(0xC2), 0x6C => Selected(0xC3), 0x6D => Selected(0xC4),
            0x6F => InPhase(2, 0xD3, 0xD4), 0x70 => InPhase(2, 0xD9, 0xDA),
            0x71 => InPhase(2, 0xDB, 0xDC), 0x73 => InPhase(2, 0xDE, 0xDF),
            _ => null,
        };

        private static Contract Account(ushort failure) =>
            new(CharacterGate.Any, null, failure, default);

        private static Contract Unselected(ushort failure) =>
            new(CharacterGate.Unselected, null, failure, default);

        private static Contract Selected(ushort failure) =>
            new(CharacterGate.Selected, null, failure, default);

        private static Contract InPhase(byte status, ushort identityFailure, ushort phaseFailure) =>
            new(CharacterGate.Selected, status, identityFailure,
                new(RequestGateAction.Disconnect, phaseFailure));
    }
}
