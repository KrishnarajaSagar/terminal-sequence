using System.IO;
using System.Net.Sockets;
using Sequence.Terminal;

namespace Sequence.Tests;

/// <summary>
/// The wording shown to a client when a connect attempt fails. Each failure mode required
/// for internet play (refused, timeout, unresolved host, server closed, unexpected error)
/// maps to a distinct, actionable message.
/// </summary>
public class ConnectFailureTests
{
    private const string Host = "55.66.77.88";
    private const int Port = 5000;
    private const int TimeoutSeconds = 10;

    private static IReadOnlyList<string> Describe(Exception ex) =>
        ConnectFailure.Describe(Host, Port, TimeoutSeconds, ex);

    [Fact]
    public void Connection_Refused_Tells_The_User_How_To_Fix_It()
    {
        var ex = new SocketException((int)SocketError.ConnectionRefused);
        IReadOnlyList<string> lines = Describe(ex);

        Assert.Contains(lines, l => l.Contains("was refused"));
        Assert.Contains(lines, l => l.Contains("router port-forward"));
    }

    [Fact]
    public void A_Cancelled_Connect_Is_Reported_As_A_Timeout()
    {
        IReadOnlyList<string> lines = Describe(new OperationCanceledException());

        Assert.Contains(lines, l => l.Contains($"{Host}:{Port} within {TimeoutSeconds}s"));
        Assert.Contains(lines, l => l.Contains("0.0.0.0"));
    }

    [Fact]
    public void An_Unresolvable_Host_Is_Called_Out_By_Name()
    {
        var ex = new SocketException((int)SocketError.HostNotFound);
        IReadOnlyList<string> lines = Describe(ex);

        Assert.Contains(lines, l => l.Contains("could not be resolved"));
    }

    [Fact]
    public void A_Generic_Socket_Error_Keeps_The_Cause_And_The_Remedy()
    {
        var ex = new SocketException((int)SocketError.NetworkUnreachable);
        IReadOnlyList<string> lines = Describe(ex);

        Assert.Contains(lines, l => l.StartsWith($"Could not connect to {Host}:{Port}"));
        Assert.Contains(lines, l => l.Contains("Start the server first"));
    }

    [Fact]
    public void A_Server_That_Closed_The_Connection_Reuses_Its_Own_Message()
    {
        var ex = new IOException("The server closed the connection.");
        Assert.Equal(new[] { "The server closed the connection." }, Describe(ex));
    }

    [Fact]
    public void An_Unexpected_Failure_Surfaces_Its_Message()
    {
        Assert.Equal(new[] { "boom" }, Describe(new InvalidOperationException("boom")));
    }
}