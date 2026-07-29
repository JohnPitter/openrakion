using RakionServer.World;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World.Tests;

internal static class TestBotAdmission
{
    public static BotManager.AddBotResult Add(
        BotManager manager,
        Field field,
        ClientSession host,
        BotDifficulty difficulty)
    {
        BotManager.AddBotResult reservation = manager.ReserveBot(
            field, host, difficulty);
        if (!reservation.Ok)
            return reservation;
        if (manager.PublishReservation(field, host, reservation) &&
            manager.ConfirmReservation(field, host, reservation))
            return reservation with { Message = "bot adicionado pela fixture" };
        manager.RollbackReservation(field, reservation);
        return new BotManager.AddBotResult(
            false, "reserva da fixture expirou", -1, null);
    }
}
