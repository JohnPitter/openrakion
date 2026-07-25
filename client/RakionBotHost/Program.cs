using RakionClientRuntime;

namespace RakionBotHost;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            BotHostOptions options = BotHostOptions.Parse(args);
            using HeadlessClientSession session =
                HeadlessClientSession.Start(ToClientOptions(options));
            string target = options.Role == HeadlessPeerRole.Master
                ? $"room={options.RoomName}"
                : options.FieldId is int fieldId ? $"field={fieldId}" : "field=quick";
            Console.WriteLine(
                $"bot-host role={options.Role.ToString().ToLowerInvariant()} {target} " +
                $"pid={session.ProcessId} dedicated=1 iniciado");
            session.WaitForExit();
            Console.WriteLine(
                $"bot-host role={options.Role.ToString().ToLowerInvariant()} {target} " +
                $"encerrado code={session.ExitCode}");
            return session.ExitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"bot-host falhou: {error.Message}");
            return 1;
        }
    }

    private static HeadlessClientOptions ToClientOptions(BotHostOptions options) =>
        new(options.ClientRoot, options.User, options.Credential, options.ServerId,
            options.Role == HeadlessPeerRole.Master
                ? HeadlessClientRole.Master
                : HeadlessClientRole.Joiner,
            options.WorldName,
            options.Role == HeadlessPeerRole.Master ? options.RoomName : null,
            options.FieldId);
}
