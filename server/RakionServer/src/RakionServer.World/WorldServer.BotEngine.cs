using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World;

public sealed partial class WorldServer
{
    public async Task AddNativeBotsAsync(
        Field field,
        ClientSession host,
        BotDifficulty difficulty,
        int count)
    {
        if (!_botEngine.Enabled)
        {
            WhisperSystem(host, "nao foi possivel adicionar bot: Bot Engine Host desativado");
            return;
        }

        int targetCount;
        lock (field.SyncRoot)
            targetCount = Math.Min(count, _cfg.BotEngine.MaxBotsPerField - field.BotCount);
        if (targetCount <= 0)
        {
            WhisperSystem(host, "nao foi possivel adicionar bot: limite nativo atingido");
            return;
        }

        int added = 0;
        string failure = "";
        for (int index = 0; index < targetCount; index++)
        {
            BotManager.AddBotResult reservation = Bots.ReserveBot(
                field, host, difficulty);
            if (!reservation.Ok)
            {
                failure = reservation.Message;
                break;
            }
            // Publica o assento antes de esperar a engine: o dono vê o bot no clique.
            if (!Bots.PublishReservation(field, host, reservation))
            {
                Bots.RollbackReservation(field, reservation);
                failure = "a sala mudou de estado";
                break;
            }
            if (!await ActivateReservationAsync(
                field, host, reservation).ConfigureAwait(false))
            {
                failure = "Bot Engine Host indisponivel";
                break;
            }
            added++;
        }

        WhisperSystem(host, added > 0
            ? $"{added} bot(s) nativo(s) adicionado(s) ({difficulty})"
            : $"nao foi possivel adicionar bot: {failure}");
    }

    private async Task<bool> ActivateReservationAsync(
        Field field,
        ClientSession host,
        BotManager.AddBotResult reservation)
    {
        try
        {
            CancellationToken cancellationToken = _cts?.Token ?? CancellationToken.None;
            await _botEngine.AddBotAsync(
                field,
                (byte)reservation.Seat,
                reservation.Bot!,
                cancellationToken).ConfigureAwait(false);
            if (Bots.ConfirmReservation(field, host, reservation))
                return true;
            throw new InvalidOperationException("A reserva expirou durante a inicialização.");
        }
        catch (Exception exception)
        {
            Log.Error("bot-engine", "field={0} admissão nativa falhou: {1}",
                field.Id, exception.Message);
            Bots.RollbackReservation(field, reservation);
            await DisableNativeBotsAsync(field).ConfigureAwait(false);
            return false;
        }
    }

    private async Task SyncNativeBotsAsync(
        Field field,
        CancellationToken cancellationToken)
    {
        if (await _botEngine.TickFieldAsync(
            field, cancellationToken).ConfigureAwait(false))
        {
            PublishNativeBotSnapshots(field);
            ResolveNativeBotCombat(field);
            return;
        }
        Bots.RemoveAllBots(field);
        Log.Warn("bot-engine", "field={0} bots removidos após falha do host", field.Id);
    }

    private async Task DisableNativeBotsAsync(Field field)
    {
        await _botEngine.StopFieldAsync(
            field.Id, CancellationToken.None).ConfigureAwait(false);
        Bots.RemoveAllBots(field);
    }

    private void PublishNativeBotSnapshots(Field field)
    {
        if (_udpGame == null)
            return;
        var deliveries = new List<(PlayerRec Target, byte[] Packet)>();
        lock (field.SyncRoot)
        {
            PlayerRec[] humans = Array.FindAll(
                field.Slots, record => record.Session != null && record.Occupied);
            foreach (PlayerRec record in field.BotSlots)
            {
                BotPlayer bot = record.Bot!;
                if (!bot.EngineAttached)
                    continue;
                foreach (byte[] packet in BuildNativePackets(record, bot))
                    foreach (PlayerRec human in humans)
                        deliveries.Add((human, packet));
            }
        }
        foreach ((PlayerRec target, byte[] packet) in deliveries)
            _udpGame.SendBotGameplay(target, packet);
    }

    private static IEnumerable<byte[]> BuildNativePackets(
        PlayerRec record,
        BotPlayer bot)
    {
        if (!bot.Alive || bot.HitReactionUntilMs != 0)
            yield break;
        bool moving = (bot.EngineControls &
            (BotControls.W | BotControls.A | BotControls.S | BotControls.D)) != 0;
        yield return BotMovement.SynthesizeMove(
            (byte)record.Slot, bot.Position, bot.Heading, ++bot.MoveSeq);
        yield return BotMovement.SynthesizeKeystate(
            (byte)record.Slot, ++bot.MoveSeq, moving);
        if (bot.ShouldPublishControls(bot.EngineControls, Environment.TickCount64))
            yield return BotMovement.SynthesizeNormalAnimation(
                (byte)record.Slot,
                ++bot.MoveSeq,
                BotMovement.AnimationForControls(bot.EngineControls));
        if (bot.EngineAttacking)
            yield return BotMovement.SynthesizeAttack(
                (byte)record.Slot, ++bot.MoveSeq, bot.NextAttackVariant());
    }

    private void StopNativeBots(int fieldId)
    {
        _ = StopNativeBotsLoggedAsync(fieldId);
    }

    private async Task StopNativeBotsLoggedAsync(int fieldId)
    {
        try
        {
            await _botEngine.StopFieldAsync(
                fieldId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warn("bot-engine", "field={0} falhou ao encerrar: {1}",
                fieldId, exception.Message);
        }
    }
}
