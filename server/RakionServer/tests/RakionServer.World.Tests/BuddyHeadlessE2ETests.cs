using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;
using RakionServer.Buddy;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class BuddyHeadlessE2ETests
{
    [Fact]
    public async Task TwoOriginalProtocolClientsExchangePresenceAndAcknowledgedSms()
    {
        string? value = Environment.GetEnvironmentVariable("RAKION_MYSQL_SMOKE_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) return;

        var admin = new MySqlConnectionStringBuilder(value) { Database = "" };
        string databaseName = "rakion_buddy_e2e_" + Guid.NewGuid().ToString("N");
        var scoped = new MySqlConnectionStringBuilder(value) { Database = databaseName };
        await ExecuteAsync(admin.ConnectionString, $"CREATE DATABASE `{databaseName}`");
        BuddyServer? server = null;
        try
        {
            await CreateFixtureAsync(scoped.ConnectionString);
            var database = new BuddyDatabase(scoped.ConnectionString);
            await database.EnsureSchemaAsync();
            await SeedFriendshipAsync(database);
            int port = FindTcpAndUdpPort();
            server = StartServer(database, scoped.ConnectionString, port);

            await using var alice = new BuddyHeadlessClient();
            await using var bob = new BuddyHeadlessClient();
            await alice.ConnectAndLoginAsync("alice", port);
            BuddyFrame aliceLogin = await alice.ReadUntilAsync(BuddyProtocol.RET_LOGIN);
            AssertLogin(aliceLogin.Payload, "bob", "Bob");
            uint firstToken = ReadUdpToken(aliceLogin.Payload);
            await alice.SetNicknameAsync("Alice");
            BuddyFrame nickResult = await alice.ReadUntilAsync(BuddyProtocol.RET_SET_NICK);
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(nickResult.Payload));
            Assert.False(await alice.ReceivesAsync(
                BuddyProtocol.RET_LOGIN, TimeSpan.FromMilliseconds(300)));
            await ExecuteAsync(scoped.ConnectionString,
                "UPDATE usergameinfo SET charname='AliceNewChar' WHERE name='alice'");
            await Task.Delay(1500);
            await ExecuteAsync(scoped.ConnectionString,
                "UPDATE usergameinfo SET buddyname='AliceNova' WHERE name='alice'");
            await alice.SetNicknameAsync("Alice");
            nickResult = await alice.ReadUntilAsync(BuddyProtocol.RET_SET_NICK);
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(nickResult.Payload));
            Assert.False(await alice.ReceivesAsync(
                BuddyProtocol.RET_LOGIN, TimeSpan.FromMilliseconds(300)));
            await bob.ConnectAndLoginAsync("bob", port);
            BuddyFrame bobLogin = await bob.ReadUntilAsync(BuddyProtocol.RET_LOGIN);
            AssertLogin(bobLogin.Payload, "alice", "AliceNova");
            await bob.SetNicknameAsync("Bob");
            nickResult = await bob.ReadUntilAsync(BuddyProtocol.RET_SET_NICK);
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(nickResult.Payload));

            await alice.RegisterUdpAsync(firstToken, port);
            AssertVip(await alice.ReadUntilAsync(BuddyProtocol.NTF_VIP_IPPORT));
            await bob.RegisterUdpAsync(ReadUdpToken(bobLogin.Payload), port);
            AssertVip(await bob.ReadUntilAsync(BuddyProtocol.NTF_VIP_IPPORT));
            AssertOnline(await alice.ReadUntilAsync(BuddyProtocol.NTF_USER_STATE), "bob");
            AssertOnline(await bob.ReadUntilAsync(BuddyProtocol.NTF_USER_STATE), "alice");

            const string text = "mensagem headless ponta a ponta";
            byte[] encrypted = alice.Crypto.Encrypt(BuddySmsCodec.BuildSend("bob", text));
            await alice.SendAsync(BuddyProtocol.SVC_SMS_SEND, encrypted);
            BuddyFrame sent = await alice.ReadUntilAsync(BuddyProtocol.RET_SMS_SEND);
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(sent.Payload));

            BuddyFrame saved = await bob.ReadUntilAsync(BuddyProtocol.NTF_SAVE_PACKET);
            byte[] clear = bob.Crypto.Decrypt(saved.Payload);
            uint messageId = AssertSavedSms(clear, "alice", "AliceNova", text);
            byte[] acknowledgement = new byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(acknowledgement, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(acknowledgement.AsSpan(2), messageId);
            await bob.SendAsync(BuddyProtocol.SVC_SAVE_PACKET_ACK, acknowledgement);

            await AssertAcknowledgedAsync(scoped.ConnectionString, messageId);
        }
        finally
        {
            server?.Stop();
            await ExecuteAsync(admin.ConnectionString, $"DROP DATABASE IF EXISTS `{databaseName}`");
        }
    }

    private static async Task SeedFriendshipAsync(BuddyDatabase database)
    {
        Assert.NotNull(await database.AddFriendAsync("alice", "bob", new byte[32]));
        Assert.True(await database.AddGroupAsync("alice", new BuddyGroupRecord(1, "Friends", 0)));
        Assert.True(await database.AddGroupAsync("bob", new BuddyGroupRecord(1, "Friends", 0)));
        Assert.True(await database.AssignGroupAsync("alice", "Friends", ["bob"]));
        Assert.True(await database.AssignGroupAsync("bob", "Friends", ["alice"]));
    }

    private static BuddyServer StartServer(
        BuddyDatabase database, string connectionString, int port)
    {
        var server = new BuddyServer(new BuddyConfig
        {
            Ports = [port],
            ConnectionString = connectionString,
            AbuseFile = "missing-headless-abusestring.txt"
        }, database);
        server.Start();
        return server;
    }

    private static void AssertLogin(byte[] payload, string account, string display)
    {
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(payload));
        Assert.NotEqual(0u, ReadUdpToken(payload));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6)));
        Assert.Equal(account, ReadLatin(payload.AsSpan(8, 20)));
        Assert.Equal(display, ReadWide(payload.AsSpan(28, 40)));
        Assert.Equal("Friends", ReadWide(payload.AsSpan(68, 40)));
    }

    private static uint ReadUdpToken(byte[] login) =>
        BinaryPrimitives.ReadUInt32LittleEndian(login.AsSpan(2));

    private static void AssertVip(BuddyFrame frame)
    {
        Assert.Equal(6, frame.Payload.Length);
        Assert.Equal([127, 0, 0, 1], frame.Payload[..4]);
        Assert.NotEqual(0, BinaryPrimitives.ReadUInt16BigEndian(frame.Payload.AsSpan(4)));
    }

    private static void AssertOnline(BuddyFrame frame, string account)
    {
        Assert.Equal(35, frame.Payload.Length);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload));
        Assert.Equal(account, ReadLatin(frame.Payload.AsSpan(2, 20)));
        Assert.Equal(1, frame.Payload[22]);
    }

    private static uint AssertSavedSms(
        byte[] clear, string sender, string display, string expectedText)
    {
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(clear));
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(clear.AsSpan(2));
        Assert.NotEqual(0u, id);
        Assert.Equal(sender, ReadLatin(clear.AsSpan(6, 20)));
        Assert.Equal(display, ReadWide(clear.AsSpan(26, 40)));
        Assert.Equal(BuddySmsCodec.P2PSendSms,
            BinaryPrimitives.ReadUInt16LittleEndian(clear.AsSpan(70)));
        int length = BinaryPrimitives.ReadUInt16LittleEndian(clear.AsSpan(72));
        Assert.Equal(expectedText, Encoding.Latin1.GetString(clear.AsSpan(74, length)));
        return id;
    }

    private static async Task AssertAcknowledgedAsync(string connectionString, uint id)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new MySqlCommand(
                "SELECT delivered_at IS NOT NULL AND acked_at IS NOT NULL " +
                "FROM buddy_sms WHERE id=@id", connection);
            command.Parameters.AddWithValue("@id", id);
            if (Convert.ToBoolean(await command.ExecuteScalarAsync())) return;
            await Task.Delay(50);
        }
        Assert.Fail($"SMS {id} não foi marcado como entregue e confirmado");
    }

    private static int FindTcpAndUdpPort()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                return port;
            }
            catch (SocketException) { }
            finally { tcp.Stop(); }
        }
        throw new InvalidOperationException("não foi possível reservar uma porta TCP/UDP");
    }

    private static async Task CreateFixtureAsync(string connectionString)
    {
        await ExecuteAsync(connectionString,
            "CREATE TABLE user(id VARCHAR(16) PRIMARY KEY,password VARCHAR(32) NOT NULL) ENGINE=InnoDB;" +
            "CREATE TABLE usergameinfo(name VARCHAR(16) PRIMARY KEY,buddyname VARCHAR(20) NOT NULL," +
            "charname VARCHAR(20) NOT NULL) ENGINE=InnoDB;" +
            "CREATE TABLE buddylist(Id CHAR(16),Category CHAR(20),Buddy CHAR(16)) ENGINE=MyISAM;" +
            "INSERT INTO user VALUES('alice','x'),('bob','x');" +
            "INSERT INTO usergameinfo VALUES('alice','Alice','AliceChar'),('bob','Bob','BobChar')");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ReadLatin(ReadOnlySpan<byte> source)
    {
        int end = source.IndexOf((byte)0);
        if (end < 0) end = source.Length;
        return Encoding.Latin1.GetString(source[..end]);
    }

    private static string ReadWide(ReadOnlySpan<byte> source)
    {
        int end = 0;
        while (end + 1 < source.Length && (source[end] != 0 || source[end + 1] != 0)) end += 2;
        return Encoding.Unicode.GetString(source[..end]);
    }
}
