using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Buddy;

public sealed partial class BuddyServer
{
    private static readonly TimeSpan CharacterRefreshInterval = TimeSpan.FromMilliseconds(500);
    private int _characterRefreshFailures;

    private async Task CharacterSelectionRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(CharacterRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await RefreshSelectedCharactersAsync();
                    if (Interlocked.Exchange(ref _characterRefreshFailures, 0) > 0)
                        Log.Info("buddy", "monitor de seleção recuperado");
                }
                catch (Exception exception)
                {
                    if (Interlocked.Increment(ref _characterRefreshFailures) == 1)
                        Log.Warn("buddy", "falha no monitor de seleção: {0}", exception.Message);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshSelectedCharactersAsync()
    {
        BuddyConnection[] connections = _online.Values
            .Where(connection => connection.Authenticated)
            .ToArray();
        if (connections.Length == 0) return;

        IReadOnlyDictionary<string, BuddyAccount> accounts =
            await _database.LoadAccountsAsync(connections.Select(connection => connection.AccountId)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        long now = Environment.TickCount64;
        foreach (BuddyConnection connection in connections)
            if (accounts.TryGetValue(connection.AccountId, out BuddyAccount? account))
                await RefreshSelectedCharacterAsync(connection, account, now);
    }

    private async Task RefreshSelectedCharacterAsync(
        BuddyConnection connection, BuddyAccount account, long now)
    {
        if (string.Equals(connection.ActiveCharacterName, account.ActiveCharacterName,
                StringComparison.OrdinalIgnoreCase))
        {
            connection.PendingCharacterName = "";
            return;
        }
        if (!string.Equals(connection.PendingCharacterName, account.ActiveCharacterName,
                StringComparison.OrdinalIgnoreCase))
        {
            connection.PendingCharacterName = account.ActiveCharacterName;
            connection.PendingCharacterSince = now;
            return;
        }
        if (now - connection.PendingCharacterSince < CharacterRefreshInterval.TotalMilliseconds)
            return;
        if (!_online.TryGetValue(connection.AccountId, out BuddyConnection? current) ||
            !ReferenceEquals(current, connection))
            return;

        connection.ActiveCharacterName = account.ActiveCharacterName;
        connection.DisplayName = account.DisplayName;
        connection.PendingCharacterName = "";
        await SendLoginOkAsync(connection);
        Log.Info("buddy", "account='{0}' atualizou Messenger após selecionar char='{1}'",
            connection.AccountId, connection.ActiveCharacterName);
    }
}
