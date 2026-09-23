using System.Collections.Immutable;
using System.IO;
using Sequence.Domain;
using Sequence.Server;
using Sequence.Terminal;
using Spectre.Console;

namespace Sequence.Tests;

/// <summary>
/// Locks down the lobby frames. Both renders are pure presentation over server data
/// (<see cref="HostRosterEntry"/> / <see cref="PlayerView"/>), captured through a
/// StringWriter-backed <see cref="IAnsiConsole"/> so no live console is touched.
/// </summary>
public class LobbyScreenTests
{
    private static IAnsiConsole Spool(out StringWriter writer)
    {
        writer = new StringWriter();
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            Out = new AnsiConsoleOutput(writer),
        });
    }

    private static HostRosterEntry Seat(int value, string name, PlayerColor color, bool connected) =>
        new(new PlayerId(value), name, color, connected, connected ? name.ToLowerInvariant() : null);

    private static HostRosterEntry[] TestSeats(int connected) =>
        new[]
        {
            Seat(0, "Alice", PlayerColor.Green, connected >= 1),
            Seat(1, "Bob", PlayerColor.Blue, connected >= 2),
        };

    private static PlayerView FirstSeatView() =>
        new GameServer(new GameSetup("Alice", "Bob", 4242, 2)).GetPlayerView(new PlayerId(0));

    [Fact]
    public void Host_Lobby_Shows_Listening_Details()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(
            console, "0.0.0.0", 5001, TestSeats(connected: 0), "192.168.1.50", started: false);

        string text = writer.ToString();
        Assert.Contains("Listening on", text);
        Assert.Contains("0.0.0.0:5001", text);
        Assert.Contains("192.168.1.50", text);
        Assert.Contains("HOSTING", text);
    }

    [Fact]
    public void Host_Lobby_Waits_For_The_First_Player_When_None_Are_Connected()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(
            console, "0.0.0.0", 5000, TestSeats(connected: 0), string.Empty, started: false);

        string text = writer.ToString();
        Assert.Contains("Waiting for the first player", text);
        Assert.Contains("waiting...", text);
        Assert.DoesNotContain("connected", text);
    }

    [Fact]
    public void Host_Lobby_Waits_For_The_Second_Player_When_One_Is_Connected()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(
            console, "0.0.0.0", 5000, TestSeats(connected: 1), string.Empty, started: false);

        string text = writer.ToString();
        Assert.Contains("Waiting for the second player", text);
        Assert.Contains("Alice", text);
        Assert.Contains("connected", text);
    }

    [Fact]
    public void Host_Lobby_Asks_The_Host_To_Start_When_Both_Players_Are_Connected()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(
            console, "0.0.0.0", 5000, TestSeats(connected: 2), string.Empty, started: false);

        string text = writer.ToString();
        Assert.Contains("Both players connected", text);
        Assert.Contains("Press Enter to start", text);
        Assert.Contains("start", text);
    }

    [Fact]
    public void Host_Lobby_Blocks_A_Second_Start_Once_The_Game_Began()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(
            console, "0.0.0.0", 5000, TestSeats(connected: 2), string.Empty, started: true);

        string text = writer.ToString();
        Assert.Contains("game has already started", text);
        Assert.DoesNotContain("Press Enter to start", text);
    }

    [Fact]
    public void Client_Lobby_Shows_Joined_Seat_And_Waits_For_The_Opponent()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderClient(console, "127.0.0.1", 5000, FirstSeatView());

        string text = writer.ToString();
        Assert.Contains("Connected to", text);
        Assert.Contains("127.0.0.1:5000", text);
        Assert.Contains("Player 1", text);
        Assert.Contains("Alice", text);
        Assert.Contains("Waiting for the other player", text);
        Assert.Contains("connected", text);
    }

    [Fact]
    public void Client_Lobby_Confirms_The_Opponent_Joined_And_Waits_For_The_Host_To_Start()
    {
        IAnsiConsole console = Spool(out StringWriter writer);
        PlayerView view = FirstSeatView();
        PlayerView withOpponent =
            view with
            {
                Players = view.Players.Select(p => p with {
                    IsCurrentPlayer = true }).ToImmutableList(),
            };

        LobbyScreen.RenderClient(console, "127.0.0.1", 5000, withOpponent, opponentJoined: true);

        string text = writer.ToString();
        Assert.Contains("Both players connected", text);
        Assert.Contains("waiting for the host to start", text);
    }
}
