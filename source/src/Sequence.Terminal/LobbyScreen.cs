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
    /// waiting message until both players are connected.
    /// </summary>
    public static void RenderHost(
        IAnsiConsole console,
        string bindText,
        int port,
        IReadOnlyList<HostRosterEntry> roster,
        string localAddresses)
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
        switch (connected)
        {
            case 0:
                console.MarkupLine("[bold yellow]Waiting for the first player to join...[/]");
                break;

            case 1:
                console.MarkupLine("[bold yellow]Waiting for the second player to join...[/]");
                break;

            default:
                console.MarkupLine("[green]Both players connected - the game has started.[/]");
                break;
        }

        console.MarkupLine("");
        console.MarkupLine("[dim]Players join with the 'Join Game' menu using the address above.[/]");
        console.MarkupLine("[dim]Esc stops the server and returns to the main menu · Ctrl+C stops.[/]");
    }

    /// <summary>
    /// Renders the client's connected frame: where it reached, its own seat and chip
    /// colour, and a waiting message until the opponent connects.
    /// </summary>
    public static void RenderClient(IAnsiConsole console, string host, int port, PlayerView view)
    {
        console.Clear();
        MenuUi.RenderTitle(console, "JOINED", "connected; the game has not started yet");

        PlayerSummary viewer = view.Viewer;
        PlayerSummary opponent = view.Players.Single(p => p.Id != viewer.Id);
        string viewerDot = $"[{TerminalRenderer.ColorName(viewer.Color)}]●[/]";

        console.MarkupLine("");
        console.MarkupLine($"[bold]Connected to[/] [white]{Markup.Escape(host)}:{port}[/]");
        console.MarkupLine($"  Player {viewer.Id.Value + 1}: {Markup.Escape(viewer.Name)}  {viewerDot} {viewer.Color}");
        console.MarkupLine($"  Player {opponent.Id.Value + 1}: {Markup.Escape(opponent.Name)}  [red]waiting for the second player...[/]");

        console.MarkupLine("");
        console.MarkupLine("[bold yellow]Waiting for the other player to connect...[/]");
        console.MarkupLine("[dim]The game begins automatically once both players are connected.[/]");
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