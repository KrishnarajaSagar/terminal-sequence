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

    private static PlayerView FirstSeatView() =>
        new GameServer(new GameSetup("Alice", "Bob", 4242, 2)).GetPlayerView(new PlayerId(0));

    [Fact]
    public void Host_Lobby_Shows_Listening_Details()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(
            console, "0.0.0.0", 5001, Array.Empty<HostRosterEntry>(), "192.168.1.50");

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

        LobbyScreen.RenderHost(console, "0.0.0.0", 5000, TestSeats(connected: 0), string.Empty);

        string text = writer.ToString();
        Assert.Contains("Waiting for the first player", text);
        Assert.Contains("waiting...", text);
        Assert.DoesNotContain("connected", text);
    }

    [Fact]
    public void Host_Lobby_Waits_For_The_Second_Player_When_One_Is_Connected()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(console, "0.0.0.0", 5000, TestSeats(connected: 1), string.Empty);

        string text = writer.ToString();
        Assert.Contains("Waiting for the second player", text);
        Assert.Contains("Alice", text);
        Assert.Contains("connected", text);
    }

    [Fact]
    public void Host_Lobby_Announces_The_Game_When_Both_Players_Are_Connected()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderHost(console, "0.0.0.0", 5000, TestSeats(connected: 2), string.Empty);

        string text = writer.ToString();
        Assert.Contains("Both players connected", text);
        Assert.Contains("game has started", text);
    }

    [Fact]
    public void Client_Lobby_Shows_Connection_Status_And_Waiting_Message()
    {
        IAnsiConsole console = Spool(out StringWriter writer);

        LobbyScreen.RenderClient(console, "127.0.0.1", 5000, FirstSeatView());

        string text = writer.ToString();
        Assert.Contains("Connected to", text);
        Assert.Contains("127.0.0.1:5000", text);
        Assert.Contains("Alice", text);
        Assert.Contains("Player 1", text);
        Assert.Contains("Waiting for the other player", text);
        Assert.Contains("JOINED", text);
    }

    private static HostRosterEntry[] TestSeats(int connected) =>
        new[]
        {
            Seat(0, "Alice", PlayerColor.Green, connected >= 1),
            Seat(1, "Bob", PlayerColor.Blue, connected >= 2),
        };
}