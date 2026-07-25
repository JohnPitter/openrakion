using RakionClientRuntime;

namespace RakionLauncher;

internal sealed partial class MainForm
{
    private sealed record HeadlessBotSession(
        string User, HeadlessClientSession Client);

    private async void OnStartBot(object? sender, EventArgs eventArgs)
    {
        SetBotActionsEnabled(false);
        try
        {
            RemoveExitedBots();
            RefreshClientCount();
            if (_clients <= _headlessBots.Count)
                throw new InvalidOperationException(
                    "Inicie o cliente gráfico e entre em uma sala antes do bot.");

            AuthenticatedAccount account = GetAvailableBotAccount();
            ClientCompatibility.ValidateInstalled(_binDir);
            int version = UpdateClient.GetInstalledVersion(
                _clientDir, _launcherConfig.BaseVersion);
            LaunchAuthentication authentication =
                await EnsureAuthenticationAsync(account, version);
            HeadlessClientSession client = HeadlessClientSession.Start(
                CreateJoinerOptions(account, authentication));
            account.Authentication = null;

            var bot = new HeadlessBotSession(account.User, client);
            _headlessBots.Add(bot);
            _ = MonitorBotAsync(bot);
            RefreshClientCount();
            WindowMode.Log(
                $"bot headless iniciado: user='{account.User}' pid={client.ProcessId}");
            Status(
                $"Bot {account.User} entrando na sala disponível. Aguarde o READY.",
                false);
        }
        catch (Exception exception)
        {
            Status($"Não foi possível iniciar o bot: {exception.Message}", true);
        }
        finally
        {
            SetBotActionsEnabled(true);
        }
    }

    private AuthenticatedAccount GetAvailableBotAccount()
    {
        AuthenticatedAccount active = _accounts.Active ??
            throw new InvalidOperationException("Faça login na conta do jogador.");
        return _accounts.GetAvailableOtherThan(
                active, _headlessBots.Select(bot => bot.User)) ??
            throw new InvalidOperationException(
                "Faça login em outra conta que ainda não esteja executando um bot.");
    }

    private HeadlessClientOptions CreateJoinerOptions(
        AuthenticatedAccount account, LaunchAuthentication authentication) =>
        new(
            _clientDir,
            account.User,
            authentication.Credential,
            ServerId,
            HeadlessClientRole.Joiner,
            @"LevelsSV\Mammoth\Mammoth.wld");

    private async Task MonitorBotAsync(HeadlessBotSession bot)
    {
        int? exitCode = null;
        try
        {
            await Task.Run(bot.Client.WaitForExit);
            exitCode = bot.Client.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            if (!IsDisposed)
            {
                bot.Client.Dispose();
                _headlessBots.Remove(bot);
                RefreshClientCount();
                UpdateStartBotEnabled();
                if (exitCode is int code)
                {
                    WindowMode.Log(
                        $"bot headless encerrado: user='{bot.User}' code={code}");
                    Status($"Bot {bot.User} encerrado (código {code}).", code != 0);
                }
            }
        }
    }

    private void RemoveExitedBots()
    {
        foreach (HeadlessBotSession bot in _headlessBots.ToArray())
        {
            if (!bot.Client.HasExited) continue;
            bot.Client.Dispose();
            _headlessBots.Remove(bot);
        }
    }

    private void StopHeadlessBots()
    {
        foreach (HeadlessBotSession bot in _headlessBots)
            bot.Client.Dispose();
        _headlessBots.Clear();
    }

    private void SetBotActionsEnabled(bool enabled)
    {
        _play.Enabled = enabled;
        _switchAccount.Enabled = enabled;
        _accountSwitch.Enabled = enabled;
        _startBot.Enabled = enabled;
        if (enabled) UpdateStartBotEnabled();
    }

    private void UpdateStartBotEnabled()
    {
        AuthenticatedAccount? active = _accounts.Active;
        _startBot.Enabled = active is not null &&
            _accounts.GetAvailableOtherThan(
                active, _headlessBots.Select(bot => bot.User)) is not null;
    }
}
