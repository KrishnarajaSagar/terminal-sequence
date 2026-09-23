using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sequence.Contracts;
using Sequence.Domain;
using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>
/// A remote terminal client. It connects to a <see cref="GameHost"/> traffic endpoint,
/// announces a player identity, renders the <see cref="PlayerView"/> the server sends,
/// and forwards user actions as <see cref="ClientMessage"/>s over a length-prefixed TCP
/// stream. It never holds game state of its own: every render comes from the last server
/// message. A single background pump owns all socket reads, so the UI can both block on
/// the next message (opponent's turn) and notice messages arriving mid-prompt (e.g. the
/// opponent leaving while you are typing).
/// </summary>
public static class NetworkGameRunner
{
    public static void Run(string host, int port, string playerId, PlayerColor? preferredColor, TimeSpan connectTimeout)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AnsiConsole.Profile.Capabilities.Ansi = !Console.IsOutputRedirected;

        // Quit reliably from any state (acting or waiting): the peer's socket closes,
        // so the server notices and tells the opponent.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Goodbye.");
            Environment.Exit(0);
        };

        using var client = new TcpClient();
        using (var cts = new CancellationTokenSource(connectTimeout))
        {
            client.ConnectAsync(host, port, cts.Token).GetAwaiter().GetResult();
        }

        NetworkStream stream = client.GetStream();

        ProtocolFraming.WriteFrame(stream, JsonSerializer.SerializeToUtf8Bytes(new HelloMessage(playerId, preferredColor)));
        using var messages = new ServerMessageChannel(stream);
        PlayerView view = ReceiveInitialGameStarted(messages);
        messages.ViewerId = view.ViewerId;

        // Interactive consoles get a short lobby frame: it shows connection status and a
        // waiting message, and leaves as soon as an opponent-related message arrives (the
        // roster refresh the host pushes when they join) or within a short grace. The
        // grace matters because the second client receives no new message on its own join
        // and must not sit in the lobby forever. Esc disconnects and returns to the menu.
        if (!Console.IsInputRedirected)
        {
            PlayerView? lobbyView = WaitForOpponentInLobby(messages, view, host, port);
            if (lobbyView is null)
            {
                Console.WriteLine("Left the lobby. Goodbye.");
                return;
            }

            view = lobbyView;
        }

        string? footer = null;
        using (ITurnPump pump = TurnPump.Create())
        {
            while (true)
            {
                // Adopt the freshest roster before drawing: if the opponent joined or
                // reconnected while we were composing the last action, the next frame
                // shows their announced name instead of the setup default.
                while (messages.TryRead(0) is GameStartedMessage roster)
                {
                    view = roster.View;
                }

                TerminalRenderer.RenderFullScreen(view, view.SequenceTarget, footer);
                footer = null;

                if (view.Status == GameStatus.Won)
                {
                    TerminalRenderer.RenderWinner(view);
                    return;
                }

                if (view.ViewerId != view.CurrentPlayerId)
                {
                    // Opponent's turn: wait for their move, but surface their disconnect
                    // right away and let this player leave with Ctrl+C or a typed 'quit'.
                    (view, footer, bool leave) = WaitForOpponent(messages, view, footer, pump);
                    if (leave)
                    {
                        Console.WriteLine("Goodbye.");
                        return;
                    }

                    continue;
                }

                // Your turn. While prompting, print any pending notice (for example the
                // opponent joining or quitting) straight onto the open line, instead of
                // waiting until you finish typing and the next frame is rendered.
                messages.ReportNoticesInline = true;
                TurnCommand command;
                try
                {
                    command = ActionBuilder.BuildTurn(view, pump);
                }
                finally
                {
                    messages.ReportNoticesInline = false;
                }

                switch (command.Kind)
                {
                    case TurnCommandKind.Quit:
                        Console.WriteLine("Goodbye.");
                        return;

                    case TurnCommandKind.Help:
                        footer = command.Text;
                        continue;

                    case TurnCommandKind.Footer:
                        footer = command.Text;
                        continue;

                    case TurnCommandKind.Submit:
                        ProtocolFraming.WriteFrame(stream, JsonSerializer.SerializeToUtf8Bytes<ClientMessage>(command.Message!, ProtocolJson.Options));
                        (view, footer) = ReceiveNext(messages, view);
                        continue;
                }
            }
        }
    }

    private static PlayerView ReceiveInitialGameStarted(ServerMessageChannel messages)
    {
        while (true)
        {
            ServerMessage message = messages.Read();
            if (message is GameStartedMessage started)
            {
                return started.View;
            }
        }
    }

    /// <summary>
    /// The brief connected-lobby wait. Renders every tick, exits with the first message
    /// that confirms the game can proceed (a roster refresh or a state update), and falls
    /// through to the board when nothing arrives within the grace period. Returns null
    /// when the player presses Escape to leave the lobby.
    /// </summary>
    private static PlayerView? WaitForOpponentInLobby(ServerMessageChannel messages, PlayerView initial, string host, int port)
    {
        PlayerView view = initial;
        DateTime deadline = DateTime.UtcNow + ClientLobbyGrace;

        while (DateTime.UtcNow < deadline)
        {
            LobbyScreen.RenderClient(AnsiConsole.Console, host, port, view);

            if (messages.TryRead(100) is { } message)
            {
                if (message is GameStartedMessage started)
                {
                    return started.View;
                }

                if (message is GameStateUpdatedMessage updated)
                {
                    return updated.View;
                }

                // A disconnect notice or a rejection while nobody has begun is not
                // playable yet; keep polling until the roster confirms the opponent.
            }

            if (ConsoleKeys.EscapePressed())
            {
                return null;
            }
        }

        return view;
    }

    private static readonly TimeSpan ClientLobbyGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Blocks until a state-bearing message arrives. Informational noise (an opponent
    /// leaving) is accumulated into the footer; rejections end the wait so the acting
    /// client can retry its turn.
    /// </summary>
    private static (PlayerView View, string? Footer) ReceiveNext(ServerMessageChannel messages, PlayerView current)
    {
        PlayerView view = current;
        string? footer = null;

        while (true)
        {
            switch (messages.Read())
            {
                case GameStartedMessage:
                    // Roster refresh pushed when the opponent joins/reconnects. It is a
                    // cosmetic snapshot from before this action; the GameStateUpdatedMessage
                    // that follows is the authoritative view, so this one is discarded.
                    continue;

                case GameStateUpdatedMessage updated:
                    return (updated.View, footer);

                case ActionRejectedMessage rejected:
                    footer = JoinFooters(footer, rejected.Error.ToString());
                    return (view, footer);

                case PlayerDisconnectedMessage disconnected:
                    footer = JoinFooters(footer, DisconnectText(disconnected));
                    continue;

                default:
                    continue;
            }
        }
    }

    private static string DisconnectText(PlayerDisconnectedMessage notice) =>
        $"{notice.PlayerName} disconnected. They can reconnect with the same player id.";

    /// <summary>
    /// Waits while the opponent plays. Every incoming message is handed back so the frame
    /// re-renders with it immediately - a disconnect notice is shown without further
    /// interaction - and the waiting player can leave with Ctrl+C or a typed 'quit'.
    /// </summary>
    private static (PlayerView View, string? Footer, bool Leave) WaitForOpponent(
        ServerMessageChannel messages, PlayerView current, string? footer, ITurnPump pump)
    {
        PlayerView view = current;
        while (view.ViewerId != view.CurrentPlayerId)
        {
            if (messages.TryRead(250) is { } message)
            {
                switch (message)
                {
                    case GameStartedMessage started:
                        return (started.View, footer, false);

                    case GameStateUpdatedMessage updated:
                        return (updated.View, footer, false);

                    case ActionRejectedMessage rejected:
                        return (view, JoinFooters(footer, rejected.Error.ToString()), false);

                    case PlayerDisconnectedMessage disconnected:
                        return (view, JoinFooters(footer, DisconnectText(disconnected)), false);
                }
            }
            else if (LeaveRequested(pump))
            {
                return (view, footer, true);
            }
        }

        return (view, footer, false);
    }

    /// <summary>True when the player typed a quit command on the open console line while waiting.</summary>
    private static bool LeaveRequested(ITurnPump pump)
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            return false; // no live console keys to read (piped input, CI, smokes)
        }

        try
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            return false;
        }

        return IsQuit(pump.Next(null));
    }

    private static bool IsQuit(string? line) =>
        !string.IsNullOrWhiteSpace(line)
        && (line.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase)
            || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)
            || line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase));

    private static string? JoinFooters(string? existing, string next) =>
        existing is null ? next : $"{existing}\n{next}";

    /// <summary>
    /// Owns every read on the client's TCP stream. One pump task turns length-prefixed
    /// frames into an unbounded message channel, so the UI can await the next message or
    /// be told about one while the user is still typing a move.
    /// </summary>
    private sealed class ServerMessageChannel : IDisposable
    {
        private readonly NetworkStream _stream;
        private readonly Channel<ServerMessage> _messages = Channel.CreateUnbounded<ServerMessage>();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;
        private readonly object _printGate = new();
        private volatile bool _reportNoticesInline;

        public ServerMessageChannel(NetworkStream stream)
        {
            _stream = stream;
            _pump = Task.Run(ReadLoop);
        }

        /// <summary>
        /// The viewer's seat, used to name an opponent who just joined in inline notices.
        /// Written once, before the frame loop starts reading; the read loop only uses it
        /// after the initial game has started.
        /// </summary>
        public PlayerId ViewerId
        {
            get => new(_viewerIdValue);
            set => _viewerIdValue = value.Value;
        }

        private volatile int _viewerIdValue = -1; // -1 until the initial game starts (seat ids are 0/1)

        /// <summary>
        /// When true, a pending <see cref="PlayerDisconnectedMessage"/> is printed onto the
        /// console right away (used while the player is composing a move) in addition to
        /// being queued for the next frame.
        /// </summary>
        public bool ReportNoticesInline
        {
            get => _reportNoticesInline;
            set => _reportNoticesInline = value;
        }

        private async Task ReadLoop()
        {
            try
            {
                while (true)
                {
                    byte[]? frame = await ProtocolFraming.ReadFrameAsync(_stream, _cts.Token).ConfigureAwait(false);
                    if (frame is null)
                    {
                        _messages.Writer.TryComplete(); // clean goodbye
                        return;
                    }

                    ServerMessage message;
                    try
                    {
                        message = JsonSerializer.Deserialize<ServerMessage>(frame, ProtocolJson.Options)!;
                    }
                    catch (JsonException)
                    {
                        continue; // can never happen with our own server; stay robust anyway
                    }

                    if (_reportNoticesInline)
                    {
                        lock (_printGate)
                        {
                            switch (message)
                            {
                                case PlayerDisconnectedMessage notice:
                                    Console.WriteLine();
                                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(DisconnectText(notice))}[/]");
                                    break;

                                case GameStartedMessage joined when _viewerIdValue >= 0:
                                    Console.WriteLine();
                                    var joiner = joined.View.Players.Single(p => p.Id.Value != _viewerIdValue);
                                    AnsiConsole.MarkupLine($"[bold]{Markup.Escape(joiner.Name)}[/] joined the game.");
                                    break;
                            }
                        }
                    }

                    _messages.Writer.TryWrite(message);
                }
            }
            catch (OperationCanceledException)
            {
                // host is shutting down
                _messages.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                // the socket failed (reset, timeout, bad frame...); surface it on the
                // next read instead of vanishing silently
                _messages.Writer.TryComplete(ex);
            }
        }

        /// <summary>The next server message, or the pump's fatal error if it had one.</summary>
        public ServerMessage Read()
        {
            try
            {
                return _messages.Reader.ReadAsync(_cts.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (ChannelClosedException)
            {
                throw new IOException("The server closed the connection.");
            }
        }

        /// <summary>
        /// The next buffered server message within <paramref name="timeoutMilliseconds"/>,
        /// or null when the wait timed out. Throws when the pump terminated (the server
        /// closed the connection).
        /// </summary>
        public ServerMessage? TryRead(int timeoutMilliseconds)
        {
            ValueTask<bool> wait = _messages.Reader.WaitToReadAsync();
            if (!wait.AsTask().Wait(TimeSpan.FromMilliseconds(timeoutMilliseconds)))
            {
                return null;
            }

            if (_messages.Reader.TryRead(out ServerMessage? message))
            {
                return message;
            }

            throw new IOException("The server closed the connection.");
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _pump.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // expected when the pump was mid-read at shutdown
            }

            _cts.Dispose();
        }
    }
}