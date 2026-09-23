using System.Collections.Generic;
using System.Linq;
using Sequence.Domain;
using Sequence.Server;
using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>
/// The lobby frames shown before a game starts. Both sides are purely presentational:
/// the host screen mirrors <see cref="GameHost.Roster"/> and the client screen renders the
/// <see cref="PlayerView"/> the server just sent on connect. Neither touches the engine or
/// the wire protocol.
/// </summary>
public static class LobbyScreen
{
    /// <summary>
    /// Renders the host's status frame: where it listens, which seats are filled, and a
    /// waiting or start message. Until the host begins the game, players on the other side
    /// of the wire sit in their connected lobbies waiting for the host's go.
    /// </summary>
    public static void RenderHost(
        IAnsiConsole console,
        string bindText,
        int port,
        IReadOnlyList<HostRosterEntry> roster,
        string localAddresses,
        bool started)
    {
        console.Clear();
        MenuUi.RenderTitle(console, "HOSTING", "run a server other players join");

        console.MarkupLine("");
        console.MarkupLine($"[bold]Listening on[/] [white]{Markup.Escape(bindText)}:{port}[/]");
        if (localAddresses.Length > 0)
        {
            console.MarkupLine($"[dim]Same-network address for joiners: {Markup.Escape(localAddresses)}[/]");
            console.MarkupLine("[dim]Remote joiners need that port forwarded on the router and an inbound firewall rule.[/]");
        }

        console.MarkupLine("");
        foreach (HostRosterEntry entry in roster)
        {
            RenderSeat(console, entry);
        }

        console.MarkupLine("");
        int connected = roster.Count(e => e.Connected);
        if (started)
        {
            console.MarkupLine("[green]The game has already started. Players are playing now.[/]");
        }
        else
        {
            switch (connected)
            {
                case 0:
                    console.MarkupLine("[bold yellow]Waiting for the first player to join...[/]");
                    break;

                case 1:
                    console.MarkupLine("[bold yellow]Waiting for the second player to join...[/]");
                    break;

                default:
                    console.MarkupLine("[bold green]Both players connected. Press Enter to start the game.[/]");
                    break;
            }
        }

        console.MarkupLine("");
        console.MarkupLine("[dim]Players join with the 'Join Game' menu using the address above.[/]");
        console.MarkupLine("[dim]Esc stops the server and returns to the main menu · Ctrl+C stops.[/]");
    }

    /// <summary>
    /// Renders the client's connected frame: where it reached, its own seat and chip
    /// colour, and the waiting message until <paramref name="opponentJoined"/> and the
    /// host begins the game.
    /// </summary>
    public static void RenderClient(
        IAnsiConsole console,
        string host,
        int port,
        PlayerView view,
        bool opponentJoined = false)
    {
        console.Clear();
        MenuUi.RenderTitle(console, "JOINED", "connected; the game has not started yet");

        PlayerSummary viewer = view.Viewer;
        PlayerSummary opponent = view.Players.Single(p => p.Id != viewer.Id);
        string viewerDot = $"[{TerminalRenderer.ColorName(viewer.Color)}]●[/]";

        console.MarkupLine("");
        console.MarkupLine($"[bold]Connected to[/] [white]{Markup.Escape(host)}:{port}[/]");
        console.MarkupLine($"  Player {viewer.Id.Value + 1}: {Markup.Escape(viewer.Name)}  {viewerDot} {viewer.Color}");
        if (opponentJoined)
        {
            string opponentDot = $"[{TerminalRenderer.ColorName(opponent.Color)}]●[/]";
            console.MarkupLine($"  Player {opponent.Id.Value + 1}: {Markup.Escape(opponent.Name)}  {opponentDot} [green]connected, waiting for the host to start[/]");
        }
        else
        {
            console.MarkupLine($"  Player {opponent.Id.Value + 1}: {Markup.Escape(opponent.Name)}  [red]waiting to connect...[/]");
        }

        console.MarkupLine("");
        console.MarkupLine(opponentJoined
            ? "[bold green]Both players connected. Waiting for the host to start the game...[/]"
            : "[bold yellow]Waiting for the other player to connect...[/]");
        console.MarkupLine("[dim]The host starts the game once everyone is connected.[/]");
        console.MarkupLine("[dim]Esc disconnects and returns to the main menu.[/]");
    }

    private static void RenderSeat(IAnsiConsole console, HostRosterEntry entry)
    {
        string chip = $"[{TerminalRenderer.ColorName(entry.Color)}]●[/]";
        string status = entry.Connected
            ? $"[green]connected[/] as {Markup.Escape(entry.ClientId!)}"
            : "[red]waiting...[/]";

        console.MarkupLine($"  Player {entry.Seat.Value + 1}  {chip}  [bold]{Markup.Escape(entry.PlayerName)}[/]  {status}");
    }
}