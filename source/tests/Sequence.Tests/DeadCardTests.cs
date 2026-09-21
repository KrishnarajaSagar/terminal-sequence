using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

public class DeadCardTests
{
    /// <summary>Makes a state in which both matching spaces of "7H" are occupied.</summary>
    private static GameState StateWithDeadSevenHearts(PlayerId current)
    {
        (BoardPosition A, BoardPosition B) = (
            BoardLayout.Standard.GetPositionsForCard(Card.Parse("7H"))[0],
            BoardLayout.Standard.GetPositionsForCard(Card.Parse("7H"))[1]);

        GameState baseState = H.With(H.Start(), current, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        return H.WithBoard(baseState, H.BoardWith((A, H.P1), (B, H.P1)), current);
    }

    [Fact]
    public void Dead_Card_Is_Identified_When_Both_Spaces_Occupied()
    {
        GameState state = StateWithDeadSevenHearts(H.P0);

        Assert.True(GameEngine.IsDeadCard(state, H.P0, Card.Parse("7H")));
        Assert.True(GameEngine.CanExchangeDeadCard(state, H.P0, Card.Parse("7H")));
    }

    [Fact]
    public void Playable_Card_Is_Not_Dead()
    {
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));

        Assert.False(GameEngine.IsDeadCard(state, H.P0, Card.Parse("7H")));
        Assert.False(GameEngine.CanExchangeDeadCard(state, H.P0, Card.Parse("7H")));
    }

    [Fact]
    public void Jack_Can_Never_Be_Exchanged()
    {
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("JC", "AS", "2C", "3D", "4S", "5D", "6S"));

        Assert.False(GameEngine.IsDeadCard(state, H.P0, Card.Parse("JC")));
    }

    [Fact]
    public void Exchange_Of_Playable_Card_Rejected()
    {
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));

        ActionResult result = GameEngine.ApplyAction(state, new ExchangeDeadCardAction(H.P0, Card.Parse("7H")));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.NotADeadCard, result.Error!.Code);
    }

    [Fact]
    public void Exchange_Rejects_After_One_Exchange_This_Turn()
    {
        GameState state = StateWithDeadSevenHearts(H.P0);

        ActionResult first = GameEngine.ApplyAction(state, new ExchangeDeadCardAction(H.P0, Card.Parse("7H")));
        Assert.True(first.IsSuccess);
        Assert.True(first.NewState!.CurrentPlayerHasExchangedDeadCard);

        // Same player, same ongoing turn: they now hold a second dead card and try to exchange it too.
        (BoardPosition a, BoardPosition b) = (
            BoardLayout.Standard.GetPositionsForCard(Card.Parse("8H"))[0],
            BoardLayout.Standard.GetPositionsForCard(Card.Parse("8H"))[1]);
        GameState continued = H.WithBoard(
            first.NewState.WithPlayerHand(H.P0, H.Hand("8H", "AS", "2C", "3D", "4S", "5D", "6S")),
            first.NewState.Board.PlaceChip(a, H.P1).PlaceChip(b, H.P1),
            H.P0);

        ActionResult second = GameEngine.ApplyAction(continued, new ExchangeDeadCardAction(H.P0, Card.Parse("8H")));

        Assert.False(second.IsSuccess);
        Assert.Equal(ActionErrorCode.AlreadyExchangedThisTurn, second.Error!.Code);
    }

    [Fact]
    public void Exchange_Draws_Replacement_And_Does_Not_End_The_Turn()
    {
        GameState state = StateWithDeadSevenHearts(H.P0);
        int drawBefore = state.CardsRemainingInDrawPile;

        ActionResult result = GameEngine.ApplyAction(state, new ExchangeDeadCardAction(H.P0, Card.Parse("7H")));

        Assert.True(result.IsSuccess);
        GameState after = result.NewState!;

        Assert.Equal(H.P0, after.CurrentPlayerId);
        Assert.Equal(state.TurnNumber, after.TurnNumber);
        Assert.True(after.CurrentPlayerHasExchangedDeadCard);
        Assert.Equal(7, after.GetPlayer(H.P0).Hand.Count);
        Assert.Contains(Card.Parse("7H"), after.DiscardPile);
        Assert.DoesNotContain(Card.Parse("7H"), after.GetPlayer(H.P0).Hand);
        Assert.Equal(drawBefore - 1, after.CardsRemainingInDrawPile);
        Assert.Contains(result.Events, e => e is DeadCardExchangedEvent);
        Assert.Contains(result.Events, e => e is CardDrawnEvent);
        Assert.DoesNotContain(result.Events, e => e is TurnChangedEvent);
    }

    [Fact]
    public void Player_Must_Still_Play_After_An_Exchange()
    {
        GameState state = StateWithDeadSevenHearts(H.P0);

        ActionResult exchange = GameEngine.ApplyAction(state, new ExchangeDeadCardAction(H.P0, Card.Parse("7H")));
        GameState afterExchange = exchange.NewState!;

        // The turn is still the same player's; they must now play a card (or exchange is done).
        Assert.Equal(H.P0, afterExchange.CurrentPlayerId);
        Assert.Equal(TurnPhase.Playing, afterExchange.Phase);
    }
}