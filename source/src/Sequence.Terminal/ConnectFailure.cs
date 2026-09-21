using System.Net.Sockets;

namespace Sequence.Terminal;

/// <summary>
/// Turns the exceptions observed while a terminal client tries to reach a
/// <c>GameHost</c> into the lines shown to the user. Pure so the connection-failure
/// wording can be unit-tested without real sockets.
/// </summary>
internal static class ConnectFailure
{
    /// <summary>
    /// The user-facing explanation for a failed connect attempt. An empty collection means
    /// the exception was already a friendly message and should be printed verbatim.
    /// </summary>
    public static IReadOnlyList<string> Describe(string host, int port, int timeoutSeconds, Exception ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                return new[]
                {
                    $"Could not connect to {host}:{port} within {timeoutSeconds}s (timeout).",
                    "Check that the server is running, listening on 0.0.0.0, and that any firewall rule or port-forward is open.",
                };

            case SocketException socket when
                socket.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset:
                return new[]
                {
                    $"Connection to {host}:{port} was refused.",
                    "Start the server first with '--server'. For a remote server, check the router port-forward and the server PC's inbound firewall rule.",
                };

            case SocketException socket when
                socket.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData:
                return new[]
                {
                    $"The server host '{host}' could not be resolved.",
                };

            case SocketException socket:
                return new[]
                {
                    $"Could not connect to {host}:{port} - {socket.Message}",
                    "Start the server first with '--server'.",
                };

            case IOException io:
                return new[] { io.Message };

            default:
                return new[] { ex.Message };
        }
    }
}