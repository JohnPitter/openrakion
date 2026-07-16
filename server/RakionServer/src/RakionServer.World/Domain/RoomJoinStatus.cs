namespace RakionServer.World.Domain
{
    public enum RoomJoinStatus : byte
    {
        Success = 0,
        Unavailable = 1,
        Full = 2,
        InvalidPassword = 3,
        InGame = 6,
        Ineligible = 7,
        VotePenalty = 8
    }
}
