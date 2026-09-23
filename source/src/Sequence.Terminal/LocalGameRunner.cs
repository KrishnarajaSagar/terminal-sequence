using System.Text;
using Sequence.Domain;
using Sequence.Server;
using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>
/// The local hot-seat loop. It behaves like two in-process terminal clients connected to
/// a <see cref="GameServer"/>: each turn it gathers input via <see cref="ActionBuilder"/>,
/// submits the resulting action to the server, and renders the server-provided
/// <see cref="PlayerView"/>. The server owns all game state; this class never constructs
/// or mutates an engine state.
/// </summary>
public static class LocalGameRunner
{
    public static void Run(GameSetup setup)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AnsiConsole.Profile.Capabilities.Ansi = !Console.IsOutputRedirected;

        var server = new GameServer(setup);
        const string hostClient = "local:player1";
        const string guestClient = "local:player2";
        PlayerId hostSeat = server.ConnectPlayer(hostClient);
        PlayerId guestSeat = server.ConnectPlayer(guestClient);

        string? footer = null;

        using (ITurnPump pump = TurnPump.Create())
        {
            while (true)
            {
                PlayerId current = server.CurrentPlayerId;
                PlayerView view = server.GetPlayerView(current);
                TerminalRenderer.RenderFullScreen(view, view.SequenceTarget, footer);
                footer = null;

                if (view.Status == GameStatus.Won)
                {
                    TerminalRenderer.RenderWinner(view);
                    return;
                }

                string clientId = current == hostSeat ? hostClient : guestClient;

                TurnCommand command = ActionBuilder.BuildTurn(view, pump);
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
                        footer = Submit(server, clientId, command.Message!.ToAction(view.CurrentPlayerId));
                        continue;
                }
            }
        }
    }

    private static string? Submit(GameServer server, string clientId, PlayerAction action)
    {
        ActionResult result = server.SubmitAction(clientId, action);
        return result.IsSuccess ? null : result.Error!.ToString();
    }
}