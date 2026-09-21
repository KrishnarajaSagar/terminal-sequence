using System.Collections.Immutable;
using System.Linq;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

public class MoveTests
{
    private static readonly Card SevenHearts = Card.Parse("7H");

    [Fact]
    public void Legal_Placement_On_First_Matching_Position()
    {
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(SevenHearts)[0];
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, SevenHearts, target));

        Assert.True(result.IsSuccess);
        Assert.Equal(H.P0, result.NewState!.Board.Chips[target]);
        Assert.DoesNotContain(SevenHearts, result.NewState.GetPlayer(H.P0).Hand);
        Assert.Contains(SevenHearts, result.NewState.DiscardPile);
        Assert.Equal(7, result.NewState.GetPlayer(H.P0).Hand.Count); // replacement drawn
    }

    [Fact]
    public void Legal_Placement_On_Second_Matching_Position()
    {
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(SevenHearts)[1];
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, SevenHearts, target));

        Assert.True(result.IsSuccess);
        Assert.Equal(H.P0, result.NewState!.Board.Chips[target]);
    }

    [Fact]
    public void Occupied_Alternative_Does_Not_Block_The_Free_One()
    {
        BoardPosition free = BoardLayout.Standard.GetPositionsForCard(SevenHearts)[0];
        GameState board = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(board, H.BoardWith((BoardLayout.Standard.GetPositionsForCard(SevenHearts)[1], H.P1)), H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, SevenHearts, free));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Occupied_Position_Rejected()
    {
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(SevenHearts)[0];
        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, H.BoardWith((target, H.P1)), H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, SevenHearts, target));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.TargetOccupied, result.Error!.Code);
    }

    [Fact]
    public void Position_Not_Matching_The_Card_Rejected()
    {
        BoardPosition elsewhere = new(2, 3);
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, SevenHearts, elsewhere));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.TargetDoesNotMatchCard, result.Error!.Code);
    }

    [Fact]
    public void Card_Not_In_Hand_Rejected()
    {
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(SevenHearts)[0];
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("AS", "2C", "3D", "4S", "5D", "6S", "8H"));

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, SevenHearts, target));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.CardNotInHand, result.Error!.Code);
    }

    [Fact]
    public void Opponent_Cannot_Act_On_Someone_Elses_Turn()
    {
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(SevenHearts)[0];
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P1, SevenHearts, target));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.NotYourTurn, result.Error!.Code);
    }

    [Fact]
    public void Jack_Played_Through_Normal_Action_Rejected()
    {
        BoardPosition anyEmpty = new(3, 3);
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("JC", "AS", "2C", "3D", "4S", "5D", "6S"));

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, Card.Parse("JC"), anyEmpty));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.WrongActionForCard, result.Error!.Code);
    }

    [Fact]
    public void Draw_Happens_After_Placement()
    {
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        int before = state.CardsRemainingInDrawPile;
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(SevenHearts)[0];

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, SevenHearts, target));

        Assert.True(result.IsSuccess);
        Assert.Equal(before - 1, result.NewState!.CardsRemainingInDrawPile);
        Assert.Contains(result.Events, e => e is CardDrawnEvent drawn && drawn.PlayerId == H.P0);
    }
}