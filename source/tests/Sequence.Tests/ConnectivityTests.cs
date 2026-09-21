using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sequence.Contracts;
using Sequence.Domain;
using Sequence.Server;

namespace Sequence.Tests;

/// <summary>
/// Connection-level behavior introduced for internet play: the default host binds all
/// interfaces (so clients on other networks can reach it), clients connecting to the
/// machine's own LAN address are served, a closed port is refused, and a cancelled connect
/// aborts promptly instead of hanging forever.
/// </summary>
public class ConnectivityTests
{
    private const string ClientId = "alice";

    [Fact]
    public void GameHost_Binds_All_Interfaces_By_Default()
    {
        var server = new GameServer(new GameSetup("Alice", "Bob", 4242, 2));
        GameHost host = new(server, 0);

        Assert.Equal(IPAddress.Any, host.BindAddress);
    }

    [Fact]
    public async Task Any_Bound_Host_Serves_Loopback_And_The_Machines_Lan_Address()
    {
        var host = new GameHost(new GameServer(new GameSetup("Alice", "Bob", 4242, 2)), 0);
        host.Start();
        try
        {
            List<string> failures = [];
            foreach (IPAddress target in LocalIpv4Addresses().Append(IPAddress.Loopback))
            {
                try
                {
                    await AssertHandshake(target, host.Port);
                }
                catch (Exception ex)
                {
                    failures.Add($"{target}: {ex.Message}");
                }
            }

            Assert.Empty(failures);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static async Task AssertHandshake(IPAddress target, int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(target, port);
        NetworkStream stream = client.GetStream();
        await ProtocolFraming.WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(new HelloMessage(ClientId)));

        ServerMessage started = ReadServerMessage(stream);
        Assert.IsType<GameStartedMessage>(started);
    }

    [Fact]
    public async Task Connecting_To_A_Closed_Port_Is_Refused_Promptly()
    {
        // Lease and then release a loopback port so nothing is listening on it.
        var lease = new TcpListener(IPAddress.Loopback, 0);
        lease.Start();
        int freePort = ((IPEndPoint)lease.LocalEndpoint).Port;
        lease.Stop();

        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        SocketException ex = await Assert.ThrowsAsync<SocketException>(() =>
            client.ConnectAsync(IPAddress.Loopback, freePort, cts.Token).AsTask());

        Assert.Equal(SocketError.ConnectionRefused, ex.SocketErrorCode);
    }

    [Fact]
    public async Task A_Cancelled_Connect_Aborts_Promptly_Instead_Of_Hanging()
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled: the connect is aborted without touching the network

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ConnectAsync(IPAddress.Loopback, 9, cts.Token).AsTask());
    }

    private static ServerMessage ReadServerMessage(NetworkStream stream)
    {
        byte[]? frame = ProtocolFraming.ReadFrame(stream);
        Assert.NotNull(frame);
        return JsonSerializer.Deserialize<ServerMessage>(frame, ProtocolJson.Options)!;
    }

    private static IPAddress[] LocalIpv4Addresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(addr => addr.Address)
            .Distinct()
            .ToArray();
}