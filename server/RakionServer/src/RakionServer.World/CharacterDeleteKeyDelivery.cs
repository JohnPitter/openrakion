using System;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;

namespace RakionServer.World;

public static class CharacterDeleteKeyDelivery
{
    public static async Task<CharacterDeleteResult> CompleteAsync(
        CharacterDeleteOutcome outcome,
        ICharacterDeleteNotifier notifier,
        Func<string, Task<bool>> revokeAsync)
    {
        if (await notifier.SendAsync(outcome)) return CharacterDeleteResult.DeleteKeySent;
        if (!await revokeAsync(outcome.DeleteKey))
        {
            Log.Error("character", "falha ao compensar delete key da conta {0}, char {1}",
                outcome.AccountName, outcome.CharacterName);
        }
        return CharacterDeleteResult.Failed;
    }
}
