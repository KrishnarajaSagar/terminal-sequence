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
    public static void Run(string host, int port, string playerId, TimeSpan connectTimeout)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AnsiConsole.Profile.Capabilities.Ansi = !Console.IsOutputRedirected;

        using var client = new TcpClient();
        using (var cts = new CancellationTokenSource(connectTimeout))
        {
            client.ConnectAsync(host, port, cts.Token).GetAwaiter().GetResult();
        }

        NetworkStream stream = client.GetStream();

        ProtocolFraming.WriteFrame(stream, JsonSerializer.SerializeToUtf8Bytes(new HelloMessage(playerId)));
        using var messages = new ServerMessageChannel(stream);
        PlayerView view = ReceiveInitialGameStarted(messages);

        string? footer = null;
        using (ITurnPump pump = TurnPump.Create(view, view.SequenceTarget, footer))
        {
            while (true)
            {
                pump.BeginTurn(view, view.SequenceTarget, footer, view.Viewer.Color);
                TerminalRenderer.RenderFullScreen(view, view.SequenceTarget, footer, pump.Hover, pump.HoverColor, pump.HoveredHand);
                footer = null;

                if (view.Status == GameStatus.Won)
                {
                    TerminalRenderer.RenderWinner(view);
                    return;
                }

                if (view.ViewerId != view.CurrentPlayerId)
                {
                    // Opponent's turn: nothing to prompt for, just wait for the next update.
                    (view, footer) = ReceiveNext(messages, view);
                    continue;
                }

                // Your turn. While prompting, the pump prints any pending notice (for example
                // the opponent quitting) straight onto the open line, instead of waiting until
                // you finish typing and the next frame is rendered.
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
                case GameStartedMessage started:
                    return (started.View, footer);

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

                    if (message is PlayerDisconnectedMessage notice && _reportNoticesInline)
                    {
                        lock (_printGate)
                        {
                            Console.WriteLine();
                            AnsiConsole.MarkupLine($"[red]{Markup.Escape(DisconnectText(notice))}[/]");
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