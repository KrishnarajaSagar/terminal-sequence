using Sequence.Domain;
using Sequence.Engine;
using Sequence.Server;

namespace Sequence.Tests;

public class GameServerTests
{
    private static GameServer NewServer(int target = 2) =>
        new(new GameSetup("Alice", "Bob", 4242, target, PlayerColor.Green, PlayerColor.Blue));

    [Fact]
    public void Connect_Assigns_Seats_In_Order()
    {
        var server = NewServer();
        PlayerId seatA = server.ConnectPlayer("a");
        PlayerId seatB = server.ConnectPlayer("b");

        Assert.Equal(new PlayerId(0), seatA);
        Assert.Equal(new PlayerId(1), seatB);
        Assert.Equal(seatA, server.SeatForClient("a"));
        Assert.Equal(seatB, server.SeatForClient("b"));
    }

    [Fact]
    public void Reconnecting_Client_Keeps_Its_Seat()
    {
        var server = NewServer();
        PlayerId seat = server.ConnectPlayer("a");

        Assert.Equal(seat, server.ConnectPlayer("a"));
        Assert.Equal(seat, server.SeatForClient("a"));
    }

    [Fact]
    public void Third_Connection_Is_Rejected()
    {
        var server = NewServer();
        _ = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");

        Assert.Throws<InvalidOperationException>(() => server.ConnectPlayer("c"));
    }

    [Fact]
    public void Disconnect_Frees_The_Seat()
    {
        var server = NewServer();
        _ = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");

        server.DisconnectPlayer("a");
        Assert.Null(server.SeatForClient("a"));
        Assert.Equal(new PlayerId(0), server.ConnectPlayer("a"));
    }

    [Fact]
    public void Disconnected_Client_Reconnects_To_Its_Old_Seat()
    {
        var server = NewServer();
        PlayerId seatA = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");

        server.DisconnectPlayer("a");
        server.DisconnectPlayer("b");
        Assert.Equal(seatA, server.ConnectPlayer("a"));
        Assert.Equal(new PlayerId(1), server.ConnectPlayer("b"));
    }

    [Fact]
    public void Unconnected_Client_Cannot_Act()
    {
        var server = NewServer();
        _ = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");

        ActionResult result = server.SubmitAction("nobody", new PlayCardAction(new PlayerId(0), Card.Parse("7H"), new BoardPosition(4, 1)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.ClientNotConnected, result.Error!.Code);
    }

    [Fact]
    public void Client_Cannot_Act_For_Another_Seat()
    {
        var server = NewServer();
        _ = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");
        PlayerId other = server.SeatForClient("a") == H.P0 ? H.P1 : H.P0;

        ActionResult result = server.SubmitAction("a", new PlayCardAction(other, Card.Parse("7H"), new BoardPosition(4, 1)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.WrongPlayerForClient, result.Error!.Code);
    }

    [Fact]
    public void Successful_Action_Advances_The_Turn()
    {
        var server = NewServer();
        _ = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");

        PlayerId start = server.CurrentPlayerId;
        PlayerView view = server.GetPlayerView(start);
        string acting = server.SeatForClient("a") == start ? "a" : "b";
        Card playable = view.Hand.First(c => !c.IsJack);
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(playable)[0];

        ActionResult result = server.SubmitAction(acting, new PlayCardAction(start, playable, target));

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.NotEqual(start, result.NewState!.CurrentPlayerId);
        Assert.NotEqual(start, server.CurrentPlayerId);
        Assert.Contains(result.Events, e => e is TurnChangedEvent);
    }

    [Fact]
    public void Acting_Out_Of_Turn_Is_Rejected()
    {
        var server = NewServer();
        _ = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");

        PlayerId start = server.CurrentPlayerId;
        PlayerView view = server.GetPlayerView(start);
        string acting = server.SeatForClient("a") == start ? "a" : "b";
        Card playable = view.Hand.First(c => !c.IsJack);
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(playable)[0];

        ActionResult ok = server.SubmitAction(acting, new PlayCardAction(start, playable, target));
        Assert.True(ok.IsSuccess, ok.Error?.ToString());

        ActionResult again = server.SubmitAction(acting, new PlayCardAction(start, playable, target));

        Assert.False(again.IsSuccess);
        Assert.Equal(ActionErrorCode.NotYourTurn, again.Error!.Code);
        Assert.Null(again.NewState);
    }

    [Fact]
    public void Server_View_Never_Exposes_Private_Information()
    {
        var server = NewServer();
        PlayerId seat = server.ConnectPlayer("a");
        _ = server.ConnectPlayer("b");

        PlayerView view = server.GetPlayerView(seat);

        Assert.Equal(seat, view.ViewerId);
        Assert.Equal(7, view.Hand.Count);
        Assert.All(view.Players, p => Assert.DoesNotContain(p.GetType().GetProperties(), prop => prop.Name == "Hand"));
        Assert.DoesNotContain(typeof(PlayerView).GetProperties(), prop => prop.Name == "DrawPile");
    }
}