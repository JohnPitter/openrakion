using System.Net;
using System.Text;
using RakionLauncher;
using Xunit;

namespace RakionLauncher.Tests;

public sealed class OnlineFriendsClientTests
{
    [Fact]
    public async Task ReturnsAuthenticatedOnlineFriends()
    {
        var handler = new FriendHandler(
            "{\"friends\":[{\"displayName\":\"Amigo\"}]}");
        var client = new OnlineFriendsClient(new HttpClient(handler));

        IReadOnlyList<OnlineFriend>? friends = await client.GetAsync(
            new Uri("https://launcher.test/"), "test", "secret");

        Assert.Equal("Amigo", Assert.Single(Assert.IsAssignableFrom<
            IReadOnlyList<OnlineFriend>>(friends)).DisplayName);
        Assert.Contains("\"user\":\"test\"", handler.Body);
        Assert.Contains("\"password\":\"secret\"", handler.Body);
    }

    private sealed class FriendHandler(string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" :
                await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
