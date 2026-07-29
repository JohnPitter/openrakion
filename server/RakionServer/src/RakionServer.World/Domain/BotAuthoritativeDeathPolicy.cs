namespace RakionServer.World.Domain;

public static class BotAuthoritativeDeathPolicy
{
    public static bool IsClientEcho(
        Field field,
        PlayerRec victim,
        byte reportedKiller)
    {
        if (reportedKiller != Field.NoSeat ||
            !victim.Dead ||
            field.State != 2 ||
            field.Phase is not (MatchPhase.Playing or MatchPhase.RoundEnd))
            return false;

        byte sourceSeat = victim.Vitals.LastDamageSourceSeat;
        return sourceSeat < Field.NoSeat &&
            field.RecAt(sourceSeat)?.Bot != null;
    }
}
