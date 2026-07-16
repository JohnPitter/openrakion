using System;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;

namespace RakionServer.Ranking;

internal static class Program
{
    private static async Task<int> Main()
    {
        string source = RequiredEnvironment("ConnectionStrings__Rakion");
        string projection = Environment.GetEnvironmentVariable("ConnectionStrings__RakionRank") ?? source;
        int activeMonths = ParsePositiveInt("Ranking__ActiveMonths", 2);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var repository = new RankingRepository(source, projection);
            await new RankingJob(repository, activeMonths).RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Log.Warn("ranking", "execução cancelada");
            return 2;
        }
        catch
        {
            return 1;
        }
    }

    private static string RequiredEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Defina {name}.");
    }

    private static int ParsePositiveInt(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return int.TryParse(value, out int parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"{name} deve ser um inteiro positivo.");
    }
}
