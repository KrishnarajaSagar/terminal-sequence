using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sequence.Contracts;
using Sequence.Domain;
using Sequence.Server;

namespace Sequence.Tests;

/// <summary>
/// Exercises the real TCP transport end to end: two clients join a <see cref="GameHost"/>
/// on the loopback interface and exchange length-prefixed JSON messages. This is the
/// "failure conditions" layer from the v3 plan (Step 8).
/// </summary>
public class GameHostTests
{
    /// <summary>Client ids become the in-game display names once each client announces.</summary>
    private const string FirstId = "red";
    private const string SecondId = "blue";

    private static GameHost NewHost(out int port, int seed = 4242, int target = 2)
    {
        var server = new GameServer(new GameSetup("Alice", "Bob", seed, target));
        var host = new GameHost(server, 0);
        host.Start();
        port = host.Port;
        return host;
    }

    private static async Task<ServerMessage> Join(TestClient client, int port)
    {
        ServerMessage started = await client.JoinAsync(port, client.Id);
        return started;
    }

    /// <summary>Connects both clients and returns each one's starting view.</summary>
    private static async Task<(PlayerView First, PlayerView Second)> JoinBoth(GameHost host, TestClient first, TestClient second)
    {
        PlayerView firstView = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId)).View;
        PlayerView secondView = Assert.IsType<GameStartedMessage>(await second.JoinAsync(host.Port, SecondId)).View;
        return (firstView, secondView);
    }

    /// <summary>
    /// Joining the second client makes the host push a fresher roster to the first, so
    /// tests that read further messages from it must consume that refresh first.
    /// </summary>
    private static async Task DrainFirstRosterRefresh(TestClient first)
    {
        Assert.IsType<GameStartedMessage>(await first.AwaitServerMessageAsync());
    }

    private static PlayCardMessage FirstOpenPlay(PlayerView view)
    {
        Card card = view.Hand.First(c =>
            !c.IsJack &&
            BoardLayout.Standard.GetPositionsForCard(c)
                .Any(p => !view.Board.IsOccupied(p) && !view.Board.IsLocked(p)));

        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(card)
            .First(p => !view.Board.IsOccupied(p) && !view.Board.IsLocked(p));
        return new PlayCardMessage(card, target);
    }

    [Fact]
    public async Task Two_Clients_Each_Get_Their_Own_Seat_And_View()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };

        PlayerView firstView = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId)).View;
        PlayerView secondView = Assert.IsType<GameStartedMessage>(await second.JoinAsync(host.Port, SecondId)).View;

        Assert.Equal(new PlayerId(0), firstView.ViewerId);
        Assert.Equal(new PlayerId(1), secondView.ViewerId);
        Assert.NotEqual(firstView.ViewerId, secondView.ViewerId);

        // Exactly one of the two seats is the opening player (the opening draw decides).
        Assert.True(firstView.IsYourTurn ^ secondView.IsYourTurn);

        Assert.Equal(2, firstView.Players.Count);
        Assert.Equal(FirstId, firstView.ViewerName);
        Assert.Equal(SecondId, secondView.ViewerName);
        Assert.Equal(firstView.SequenceTarget, secondView.SequenceTarget);
    }

    [Fact]
    public async Task The_First_Client_Is_Sent_A_Fresh_Roster_When_The_Second_Joins()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };

        PlayerView alone = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId)).View;
        Assert.Equal("Bob", alone.Players.Single(p => p.Id != alone.ViewerId).Name);

        _ = Assert.IsType<GameStartedMessage>(await second.JoinAsync(host.Port, SecondId)).View;

        // The refresh the host pushes to the first client carries the new display name.
        var refresh = Assert.IsType<GameStartedMessage>(await first.AwaitServerMessageAsync());
        Assert.Equal(SecondId, refresh.View.Players.Single(p => p.Id != refresh.View.ViewerId).Name);
    }

    [Fact]
    public async Task A_Clients_Requested_Chip_Color_Is_Used_Over_The_Wire()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };

        PlayerView firstView = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId, PlayerColor.Yellow)).View;
        PlayerView secondView = Assert.IsType<GameStartedMessage>(await second.JoinAsync(host.Port, SecondId, PlayerColor.Magenta)).View;

        Assert.Equal(PlayerColor.Yellow, firstView.Viewer.Color);
        Assert.Equal(PlayerColor.Magenta, secondView.Viewer.Color);
        Assert.NotEqual(firstView.Viewer.Color, secondView.Viewer.Color);
    }

    [Fact]
    public async Task A_Move_From_One_Client_Is_Broadcast_To_Both()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };
        (PlayerView firstView, PlayerView secondView) = await JoinBoth(host, first, second);
        await DrainFirstRosterRefresh(first);

        (TestClient mover, PlayerView moverView, PlayerView waitingView) = firstView.IsYourTurn
            ? (first, firstView, secondView)
            : (second, secondView, firstView);

        PlayCardMessage move = FirstOpenPlay(moverView);
        await mover.SendAsync(move);

        TestClient waiting = mover == first ? second : first;
        var moverUpdate = Assert.IsType<GameStateUpdatedMessage>(await mover.AwaitServerMessageAsync());
        var waitingUpdate = Assert.IsType<GameStateUpdatedMessage>(await waiting.AwaitServerMessageAsync());

        Assert.NotEqual(moverView.CurrentPlayerId, moverUpdate.View.CurrentPlayerId);
        Assert.Equal(moverUpdate.View.CurrentPlayerId, waitingUpdate.View.CurrentPlayerId);
        Assert.DoesNotContain(moverUpdate.View.Hand, c => c == move.Card);
        Assert.Equal(7, waitingView.Hand.Count);
        Assert.All(moverUpdate.View.Board.Chips, pair => Assert.Equal(pair.Value, waitingUpdate.View.Board.ChipsAt(pair.Key)));
        Assert.Equal(moverView.ViewerId, moverUpdate.View.Board.ChipsAt(move.Target));
    }

    [Fact]
    public async Task The_Same_Action_Twice_In_A_Row_Is_Rejected_The_Second_Time()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };
        (PlayerView firstView, PlayerView secondView) = await JoinBoth(host, first, second);
        await DrainFirstRosterRefresh(first);

        (TestClient mover, PlayerView moverView, _) = firstView.IsYourTurn
            ? (first, firstView, secondView)
            : (second, secondView, firstView);

        PlayCardMessage move = FirstOpenPlay(moverView);
        await mover.SendAsync(move);
        await mover.SendAsync(move); // deliberately no read in between: two actions land together

        Assert.IsType<GameStateUpdatedMessage>(await mover.AwaitServerMessageAsync());

        var rejected = Assert.IsType<ActionRejectedMessage>(await mover.AwaitServerMessageAsync());
        Assert.Equal(ActionErrorCode.NotYourTurn, rejected.Error.Code);
    }

    [Fact]
    public async Task Acting_On_The_Opponents_Turn_Is_Rejected_Over_The_Wire()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };
        (PlayerView firstView, PlayerView secondView) = await JoinBoth(host, first, second);
        await DrainFirstRosterRefresh(first);

        // Whoever is not the opening player tries to move during the opening turn.
        (TestClient acting, PlayerView actingView, _) = firstView.IsYourTurn
            ? (second, secondView, firstView)
            : (first, firstView, secondView);

        await acting.SendAsync(FirstOpenPlay(actingView));

        var rejected = Assert.IsType<ActionRejectedMessage>(await acting.AwaitServerMessageAsync());
        Assert.Equal(ActionErrorCode.NotYourTurn, rejected.Error.Code);
    }

    [Fact]
    public async Task Playing_An_Invalid_Card_Is_Rejected_And_State_Does_Not_Change()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };
        (PlayerView firstView, PlayerView secondView) = await JoinBoth(host, first, second);
        await DrainFirstRosterRefresh(first);

        (TestClient mover, PlayerView moverView, _) = firstView.IsYourTurn
            ? (first, firstView, secondView)
            : (second, secondView, firstView);

        Card card = moverView.Hand.First(c => !c.IsJack);
        await mover.SendAsync(new PlayCardMessage(card, new BoardPosition(8, 8))); // not a position of that card

        var rejected = Assert.IsType<ActionRejectedMessage>(await mover.AwaitServerMessageAsync());
        Assert.Contains(rejected.Error.Code, new[] { ActionErrorCode.TargetDoesNotMatchCard, ActionErrorCode.TargetOutsideBoard, ActionErrorCode.TargetOccupied });

        // The rejection left the turn exactly as before: the same client can still act.
        PlayCardMessage move = FirstOpenPlay(moverView);
        await mover.SendAsync(move);
        Assert.IsType<GameStateUpdatedMessage>(await mover.AwaitServerMessageAsync());
    }

    [Fact]
    public async Task A_Third_Connection_Is_Closed_Without_A_Seat()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        await using var second = new TestClient { Id = SecondId };
        await using var third = new TestClient { Id = "mallory" };

        _ = await first.JoinAsync(host.Port, FirstId);
        _ = await second.JoinAsync(host.Port, SecondId);

        // The host refuses a third seat outright: the connection is closed without a reply.
        await Assert.ThrowsAsync<System.IO.IOException>(() => third.ConnectAndSayHelloAsync(host.Port, "mallory"));
        Assert.True(await third.WaitForCloseAsync());
    }

    [Fact]
    public async Task A_Disconnect_Notifies_The_Remaining_Client()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        var second = new TestClient { Id = SecondId };

        PlayerView firstView = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId)).View;
        PlayerView secondView = Assert.IsType<GameStartedMessage>(await second.JoinAsync(host.Port, SecondId)).View;
        await DrainFirstRosterRefresh(first);
        await second.DisposeAsync();

        var notice = Assert.IsType<PlayerDisconnectedMessage>(await first.AwaitServerMessageAsync());
        Assert.Equal(secondView.ViewerId, notice.PlayerId);
        Assert.Equal(SecondId, notice.PlayerName);
    }

    [Fact]
    public async Task A_Client_Disconnects_During_Its_Turn_Then_Reconnects_To_The_Same_Seat()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };
        var second = new TestClient { Id = SecondId };

        PlayerView firstView = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId)).View;
        PlayerView secondView = Assert.IsType<GameStartedMessage>(await second.JoinAsync(host.Port, SecondId)).View;
        await DrainFirstRosterRefresh(first);

        // The opening player acts so the turn moves to the other client.
        (TestClient mover, PlayerView moverView, _) = firstView.IsYourTurn
            ? (first, firstView, secondView)
            : (second, secondView, firstView);
        await mover.SendAsync(FirstOpenPlay(moverView));
        Assert.IsType<GameStateUpdatedMessage>(await mover.AwaitServerMessageAsync());
        Assert.IsType<GameStateUpdatedMessage>(await (mover == first ? second : first).AwaitServerMessageAsync());

        // Now it is the other client's turn; it drops out mid-turn.
        TestClient leaver = mover == first ? second : first;
        PlayerView leaverView = leaver == second ? secondView : firstView;
        string leaverId = leaver == second ? SecondId : FirstId;
        await leaver.DisposeAsync();
        Assert.IsType<PlayerDisconnectedMessage>(await (mover == first ? first : second).AwaitServerMessageAsync());

        // Reconnecting with the same identity must bring back the same seat and nothing lost.
        await using var again = new TestClient { Id = leaverId };
        PlayerView view2 = Assert.IsType<GameStartedMessage>(await again.JoinAsync(host.Port, leaverId)).View;

        Assert.Equal(leaverView.ViewerId, view2.ViewerId);
        Assert.Equal(leaverView.ViewerId, view2.CurrentPlayerId); // still the leaver's turn
        Assert.Equal(7, view2.Hand.Count);
    }

    [Fact]
    public async Task Malformed_Data_Is_Rejected_Without_Dropping_The_Connection()
    {
        await using GameHost host = NewHost(out _);
        await using var first = new TestClient { Id = FirstId };

        _ = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId)).View;

        await first.SendRawAsync(Encoding.UTF8.GetBytes("this is not json"));

        var rejected = Assert.IsType<ActionRejectedMessage>(await first.AwaitServerMessageAsync());
        Assert.Equal(ActionErrorCode.MalformedMessage, rejected.Error.Code);
    }

    [Fact]
    public async Task Every_Accepted_Action_Is_Reported_To_The_Persistence_Observer()
    {
        var server = new GameServer(new GameSetup("Alice", "Bob", 4242, 2));
        GameState? lastSaved = null;
        var host = new GameHost(server, 0, updated => lastSaved = updated);
        host.Start();
        try
        {
            await using var first = new TestClient { Id = FirstId };
            await using var second = new TestClient { Id = SecondId };
            PlayerView firstView = Assert.IsType<GameStartedMessage>(await first.JoinAsync(host.Port, FirstId)).View;

            // No action accepted yet: nothing has been persisted.
            Assert.Null(lastSaved);

            PlayerView secondView = Assert.IsType<GameStartedMessage>(await second.JoinAsync(host.Port, SecondId)).View;
            await DrainFirstRosterRefresh(first);

            (TestClient mover, PlayerView moverView) = firstView.IsYourTurn
                ? (first, firstView)
                : (second, secondView);
            PlayCardMessage move = FirstOpenPlay(moverView);
            await mover.SendAsync(move);
            Assert.IsType<GameStateUpdatedMessage>(await mover.AwaitServerMessageAsync());
            Assert.IsType<GameStateUpdatedMessage>(await (mover == first ? second : first).AwaitServerMessageAsync());

            // The accepted action moved the turn to the other seat and was reported.
            Assert.NotNull(lastSaved);
            Assert.NotEqual(moverView.ViewerId, lastSaved!.CurrentPlayerId);

            // A rejected follow-up must not overwrite the persisted state. The mover
            // resending its own move is rejected as out of turn (the turn has moved on).
            GameState beforeRejection = lastSaved;
            await mover.SendAsync(move);
            Assert.IsType<ActionRejectedMessage>(await mover.AwaitServerMessageAsync());
            Assert.Same(beforeRejection, lastSaved);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private sealed class TestClient : IAsyncDisposable
    {
        private readonly TcpClient _tcp = new();
        private NetworkStream? _stream;

        public string Id { get; init; } = string.Empty;

        /// <summary>Connects and announces the identity; the first server message is returned.</summary>
        public async Task<ServerMessage> JoinAsync(int port, string id, PlayerColor? color = null)
        {
            return await ConnectAndSayHelloAsync(port, id, color);
        }

        public async Task<ServerMessage> ConnectAndSayHelloAsync(int port, string id, PlayerColor? color = null)
        {
            await _tcp.ConnectAsync(IPAddress.Loopback, port);
            _stream = _tcp.GetStream();
            await ProtocolFraming.WriteFrameAsync(_stream, JsonSerializer.SerializeToUtf8Bytes(new HelloMessage(id, color)));
            return await AwaitEofOrMessageAsync() ?? throw new IOException("Server closed the connection before the game started.");
        }

        public async Task SendAsync(ClientMessage message) =>
            await ProtocolFraming.WriteFrameAsync(_stream!, JsonSerializer.SerializeToUtf8Bytes<ClientMessage>(message, ProtocolJson.Options));

        public async Task SendRawAsync(byte[] payload) =>
            await ProtocolFraming.WriteFrameAsync(_stream!, payload);

        public async Task<ServerMessage> AwaitServerMessageAsync(int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            byte[]? frame = await ProtocolFraming.ReadFrameAsync(_stream!, cts.Token);
            Assert.NotNull(frame);
            return JsonSerializer.Deserialize<ServerMessage>(frame, ProtocolJson.Options)!;
        }

        /// <summary>True when the server closes the connection without sending a message.</summary>
        public async Task<bool> WaitForCloseAsync(int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                byte[]? frame = await ProtocolFraming.ReadFrameAsync(_stream!, cts.Token);
                return frame is null;
            }
            catch (IOException)
            {
                return true;
            }
        }

        private async Task<ServerMessage?> AwaitEofOrMessageAsync()
        {
            byte[]? frame = await ProtocolFraming.ReadFrameAsync(_stream!);
            if (frame is null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<ServerMessage>(frame, ProtocolJson.Options)!;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _stream?.Close();
            }
            catch (IOException)
            {
            }

            _tcp.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}