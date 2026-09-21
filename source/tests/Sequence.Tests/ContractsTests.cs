using System.Text.Json;
using Sequence.Contracts;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

public class ContractsTests
{
    private static string Serialize<T>(T message) where T : notnull =>
        JsonSerializer.Serialize<T>(message, ProtocolJson.Options);

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, ProtocolJson.Options)!;

    private static PlayerView AnyView()
    {
        GameState state = H.Start(123);
        return StateProjection.GetPlayerView(state, H.P0);
    }

    [Theory]
    [InlineData("7H", 4, 1)]
    [InlineData("AS", 9, 4)]
    public void PlayCard_Message_Round_Trips(string code, int row, int col)
    {
        var message = new PlayCardMessage(Card.Parse(code), new BoardPosition(row, col));
        PlayerId seat = new(1);

        ClientMessage round = Deserialize<ClientMessage>(Serialize<ClientMessage>(message));

        var parsed = Assert.IsType<PlayCardMessage>(round);
        Assert.Equal(message.Card, parsed.Card);
        Assert.Equal(message.Target, parsed.Target);

        PlayerAction action = message.ToAction(seat);
        var play = Assert.IsType<PlayCardAction>(action);
        Assert.Equal(seat, play.PlayerId);
        Assert.Equal(message.Card, play.Card);
        Assert.Equal(message.Target, play.Target);
    }

    [Fact]
    public void Two_Eyed_Jack_Message_Round_Trips_And_Maps()
    {
        var message = new PlayTwoEyedJackMessage(Card.Parse("JC"), new BoardPosition(2, 3));
        PlayerId seat = new(0);

        ClientMessage round = Deserialize<ClientMessage>(Serialize<ClientMessage>(message));
        Assert.Equal(message, round);

        Assert.IsType<PlayTwoEyedJackAction>(message.ToAction(seat));
    }

    [Fact]
    public void One_Eyed_Jack_Message_Round_Trips()
    {
        var message = new PlayOneEyedJackMessage(Card.Parse("JH"), new BoardPosition(5, 6));

        ClientMessage round = Deserialize<ClientMessage>(Serialize<ClientMessage>(message));
        Assert.Equal(message, round);
    }

    [Fact]
    public void Exchange_Dead_Card_Message_Round_Trips()
    {
        var message = new ExchangeDeadCardMessage(Card.Parse("9H"));

        ClientMessage round = Deserialize<ClientMessage>(Serialize<ClientMessage>(message));
        Assert.Equal(message, round);
    }

    [Fact]
    public void Game_Started_Message_Round_Trips()
    {
        var message = new GameStartedMessage(AnyView());

        ServerMessage round = Deserialize<ServerMessage>(Serialize<ServerMessage>(message));

        var parsed = Assert.IsType<GameStartedMessage>(round);
        Assert.Equal(message.View.ViewerId, parsed.View.ViewerId);
        Assert.Equal(message.View.Hand, parsed.View.Hand);
        Assert.Equal(message.View.CardsRemainingInDrawPile, parsed.View.CardsRemainingInDrawPile);
    }

    [Fact]
    public void Game_State_Updated_Message_Round_Trips_With_Events()
    {
        var view = AnyView();
        var message = new GameStateUpdatedMessage(
            view,
            new GameEvent[]
            {
                new CardPlayedEvent(H.P0, Card.Parse("7H")),
                new ChipPlacedEvent(H.P0, new BoardPosition(4, 1)),
                new TurnChangedEvent(H.P1, 2),
            });

        ServerMessage round = Deserialize<ServerMessage>(Serialize<ServerMessage>(message));

        var parsed = Assert.IsType<GameStateUpdatedMessage>(round);
        Assert.Equal(message.View.Hand, parsed.View.Hand);
        Assert.Equal(3, parsed.Events.Count);
        Assert.IsType<CardPlayedEvent>(parsed.Events[0]);
        Assert.IsType<ChipPlacedEvent>(parsed.Events[1]);
        Assert.IsType<TurnChangedEvent>(parsed.Events[2]);
    }

    [Fact]
    public void Action_Rejected_Message_Round_Trips()
    {
        var message = new ActionRejectedMessage(new ActionError(ActionErrorCode.NotYourTurn, "It is not this player's turn.", H.P1));

        ServerMessage round = Deserialize<ServerMessage>(Serialize<ServerMessage>(message));

        var parsed = Assert.IsType<ActionRejectedMessage>(round);
        Assert.Equal(ActionErrorCode.NotYourTurn, parsed.Error.Code);
        Assert.Equal(H.P1, parsed.Error.PlayerId);
    }

    [Fact]
    public void Board_With_Ownerless_Positions_Survives_Wire_Delivery()
    {
        // A view holding chips and locked positions must rebuild identically after JSON.
        GameState state = H.With(
            H.Start(123),
            H.P0,
            p0Hand: H.Hand(H.CardAt(2, 6).Code, "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState scored = GameEngine.ApplyAction(
            H.WithBoard(state, H.BoardWith(
                (H.Pos(2, 2), H.P0),
                (H.Pos(2, 3), H.P0),
                (H.Pos(2, 4), H.P0),
                (H.Pos(2, 5), H.P0)), H.P0),
            new PlayCardAction(H.P0, H.CardAt(2, 6), new BoardPosition(2, 6))).NewState!;

        PlayerView view = StateProjection.GetPlayerView(scored, H.P0);
        BoardState round = JsonSerializer.Deserialize<BoardState>(JsonSerializer.Serialize(view.Board, ProtocolJson.Options), ProtocolJson.Options)!;

        Assert.All(view.Board.Chips, pair => Assert.Equal(pair.Value, round.ChipsAt(pair.Key)));
        Assert.All(view.Board.LockedPositions, pos => Assert.True(round.IsLocked(pos)));
    }
}