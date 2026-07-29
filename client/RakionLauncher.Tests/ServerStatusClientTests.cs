using System.Text.Json;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class ServerStatusClientTests
{
    [Fact]
    public void CamelCaseApiResponseMapsToLauncherContract()
    {
        const string json =
            """{"online":true,"onlinePlayers":7,"capacity":500}""";

        ServerStatusResponse? response =
            JsonSerializer.Deserialize<ServerStatusResponse>(
                json, ServerStatusClient.JsonOptions);

        Assert.NotNull(response);
        Assert.True(response.Online);
        Assert.Equal(7, response.OnlinePlayers);
        Assert.Equal(500, response.Capacity);
    }
}
