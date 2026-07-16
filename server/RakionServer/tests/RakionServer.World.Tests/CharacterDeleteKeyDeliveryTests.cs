using System.Threading.Tasks;
using RakionServer.World;
using RakionServer.World.Database;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class CharacterDeleteKeyDeliveryTests
{
    private static readonly CharacterDeleteOutcome Outcome = new(
        CharacterDeleteResult.DeleteKeySent,
        "account", "Hero", "user@example.test", "AB01ab23AB");

    [Fact]
    public async Task SuccessfulDeliveryKeepsIssuedKey()
    {
        bool revoked = false;
        CharacterDeleteResult result = await CharacterDeleteKeyDelivery.CompleteAsync(
            Outcome, new Notifier(true), _ =>
            {
                revoked = true;
                return Task.FromResult(true);
            });

        Assert.Equal(CharacterDeleteResult.DeleteKeySent, result);
        Assert.False(revoked);
    }

    [Fact]
    public async Task FailedDeliveryRevokesIssuedKey()
    {
        string? revokedKey = null;
        CharacterDeleteResult result = await CharacterDeleteKeyDelivery.CompleteAsync(
            Outcome, new Notifier(false), key =>
            {
                revokedKey = key;
                return Task.FromResult(true);
            });

        Assert.Equal(CharacterDeleteResult.Failed, result);
        Assert.Equal(Outcome.DeleteKey, revokedKey);
    }

    private sealed class Notifier(bool result) : ICharacterDeleteNotifier
    {
        public Task<bool> SendAsync(CharacterDeleteOutcome outcome) => Task.FromResult(result);
    }
}
