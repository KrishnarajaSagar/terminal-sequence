using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sequence.Contracts;
using Sequence.Domain;

namespace Sequence.Server;

/// <summary>
/// The TCP entry point of the authoritative server. Accepts up to two clients on the
/// configured interface (default: all interfaces, so clients can connect from another
/// machine or network), binds each to a seat via its <see cref="HelloMessage"/>, and
/// forwards client actions to the <see cref="GameServer"/>. Every accepted action is
/// broadcast as a per-player <see cref="GameStateUpdatedMessage"/>; rejections go back to
/// the acting client only. Disconnects free the seat and notify the remaining player.
/// </summary>
public sealed partial class GameHost : IAsyncDisposable
{
    private readonly GameServer _server;
    private readonly Action<GameState>? _onStateChanged;
    private readonly TcpListener _listener;
    private readonly IPAddress _bindAddress;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _acceptLoop;

    /// <summary>Binds to all interfaces so clients on other networks can connect.</summary>
    public GameHost(GameServer server, int port = 5000, Action<GameState>? onStateChanged = null)
        : this(server, port, IPAddress.Any, onStateChanged)
    {
    }

    public GameHost(GameServer server, int port, IPAddress bindAddress, Action<GameState>? onStateChanged = null)
    {
        _server = server;
        _onStateChanged = onStateChanged;
        _bindAddress = bindAddress ?? IPAddress.Any;
        _listener = new TcpListener(_bindAddress, port);
    }

    /// <summary>The port actually bound (useful when the caller asked for ephemeral port 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The interface this host listens on (e.g. 0.0.0.0 for all interfaces).</summary>
    public IPAddress BindAddress => _bindAddress;

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
    }

    public async Task StopAsync()
    {
        _listener.Stop();
        _shutdown.Cancel();

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        foreach (ClientConnection connection in _clients.Values)
        {
            CloseAndUnregister(connection);
        }
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        Stop();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break; // listener stopped
            }

            _ = Task.Run(() => HandleClientAsync(client, ct));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var connection = new ClientConnection(client);
        try
        {
            string? clientId;
            PlayerColor? preferredColor;
            (clientId, preferredColor) = await ReceiveHelloAsync(connection.Stream, ct);
            if (clientId is null)
            {
                connection.Close();
                return;
            }

            if (_clients.ContainsKey(clientId))
            {
                connection.Close(); // the identity is currently connected; refuse the twin
                return;
            }

            PlayerId seat;
            try
            {
                seat = _server.ConnectPlayer(clientId, preferredColor);
            }
            catch (InvalidOperationException)
            {
                connection.Close(); // game already has two seats
                return;
            }

            connection.ClientId = clientId;
            connection.Seat = seat;

            if (!_clients.TryAdd(clientId, connection))
            {
                connection.Close(); // lost a race: some other connection already owns the id
                return;
            }

            connection.Registered = true;

            // The announced id becomes the display name, so both clients see the same
            // names (matching the --player ids) instead of the setup defaults.
            _server.RenamePlayer(clientId, clientId);

            await connection.SendFrameAsync(EncodeServerMessage(new GameStartedMessage(_server.GetPlayerView(seat))), ct).ConfigureAwait(false);
            await NotifyRosterChangeAsync(connection, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                byte[]? frame = await ProtocolFraming.ReadFrameAsync(connection.Stream, ct).ConfigureAwait(false);
                if (frame is null)
                {
                    break; // clean goodbye
                }

                ClientMessage? message = TryDeserializeClientMessage(frame);
                if (message is null)
                {
                    var rejected = new ActionRejectedMessage(
                        new ActionError(ActionErrorCode.MalformedMessage, "The server could not understand the request."));
                    await connection.SendFrameAsync(EncodeServerMessage(rejected), ct).ConfigureAwait(false);
                    continue;
                }

                PlayerAction action = message.ToAction(connection.Seat);
                ActionResult result = _server.SubmitAction(connection.ClientId, action);

                if (!result.IsSuccess)
                {
                    await connection.SendFrameAsync(EncodeServerMessage(new ActionRejectedMessage(result.Error!)), ct).ConfigureAwait(false);
                    continue;
                }

                await BroadcastResultAsync(connection, result.Events!, ct).ConfigureAwait(false);
                _onStateChanged?.Invoke(_server.CurrentState);
            }
        }
        catch (OperationCanceledException)
        {
            // host is shutting down
        }
        catch (IOException)
        {
            // peer vanished (half-open socket, reset, etc.); fall through to cleanup
        }
        catch (SocketException)
        {
            // same, at the socket layer
        }
        catch (JsonException)
        {
            // malformed handshake or frame; the connection is abandoned below
        }
        finally
        {
            if (connection.Registered)
            {
                await CloseAndUnregisterAsync(connection).ConfigureAwait(false);
            }
            else
            {
                connection.Close();
            }
        }
    }

    /// <summary>Sends the updated view to the acting client and then to the other client.</summary>
    private async Task BroadcastResultAsync(ClientConnection acting, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        var actingUpdate = new GameStateUpdatedMessage(_server.GetPlayerView(acting.Seat), events);
        await acting.SendFrameAsync(EncodeServerMessage(actingUpdate), ct).ConfigureAwait(false);

        foreach (ClientConnection connection in _clients.Values)
        {
            if (ReferenceEquals(connection, acting) || !connection.Registered)
            {
                continue;
            }

            var update = new GameStateUpdatedMessage(_server.GetPlayerView(connection.Seat), events);
            await connection.SendFrameAsync(EncodeServerMessage(update), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pushes a fresh roster to every already-connected client when one joins or
    /// reconnects, so a sitting player's frame reflects the announced display name
    /// instead of the setup default (e.g. "Player 2") right away.
    /// </summary>
    private async Task NotifyRosterChangeAsync(ClientConnection joined, CancellationToken ct)
    {
        foreach (ClientConnection connection in _clients.Values)
        {
            if (connection.Registered && !ReferenceEquals(connection, joined))
            {
                var roster = new GameStartedMessage(_server.GetPlayerView(connection.Seat));
                await connection.SendFrameAsync(EncodeServerMessage(roster), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task CloseAndUnregisterAsync(ClientConnection connection)
    {
        if (_clients.TryRemove(connection.ClientId, out _))
        {
            _server.DisconnectPlayer(connection.ClientId);
        }

        connection.Close();
        connection.Registered = false;

        string name = _server.GetPlayerView(connection.Seat).ViewerName;
        var notice = new PlayerDisconnectedMessage(connection.Seat, name);

        foreach (ClientConnection remaining in _clients.Values)
        {
            if (remaining.Registered)
            {
                try
                {
                    await remaining.SendFrameAsync(EncodeServerMessage(notice), CancellationToken.None).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // the remaining client may also be gone; the accept loop will clean it up
                }
            }
        }
    }

    private static void CloseAndUnregister(ClientConnection connection)
    {
        if (connection.Registered)
        {
            connection.Registered = false;
        }

        connection.Close();
    }

    private static async Task<(string? ClientId, PlayerColor? PreferredColor)> ReceiveHelloAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[]? frame = await ProtocolFraming.ReadFrameAsync(stream, ct).ConfigureAwait(false);
        if (frame is null)
        {
            return (null, null);
        }

        HelloMessage hello = JsonSerializer.Deserialize<HelloMessage>(frame, ProtocolJson.Options)!;
        string clientId = hello.ClientId?.Trim() ?? string.Empty;
        if (clientId.Length == 0)
        {
            throw new JsonException("HelloMessage must carry a non-empty client id.");
        }

        return (clientId, hello.PreferredColor);
    }

    private static ClientMessage? TryDeserializeClientMessage(byte[] frame)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientMessage>(frame, ProtocolJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null; // unknown "type" discriminator
        }
    }

    private static byte[] EncodeServerMessage(ServerMessage message) =>
        ProtocolFraming.Encode(JsonSerializer.Serialize<ServerMessage>(message, ProtocolJson.Options));
}