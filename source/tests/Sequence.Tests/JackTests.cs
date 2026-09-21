using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

public class JackTests
{
    private static GameState StateWith(PlayerId current, params string[] hand) =>
        H.With(H.Start(), current, p0Hand: H.Hand(hand));

    [Fact]
    public void TwoEyed_Jack_Places_On_Any_Empty_NonCorner()
    {
        BoardPosition target = new(3, 3);
        GameState state = StateWith(H.P0, "JC", "AS", "2C", "3D", "4S", "5D", "6S");

        ActionResult result = GameEngine.ApplyAction(state, new PlayTwoEyedJackAction(H.P0, Card.Parse("JC"), target));

        Assert.True(result.IsSuccess);
        Assert.Equal(H.P0, result.NewState!.Board.Chips[target]);
    }

    [Fact]
    public void TwoEyed_Jack_Cannot_Target_A_Free_Corner()
    {
        GameState state = StateWith(H.P0, "JC", "AS", "2C", "3D", "4S", "5D", "6S");

        ActionResult result = GameEngine.ApplyAction(state, new PlayTwoEyedJackAction(H.P0, Card.Parse("JC"), BoardPosition.FreeCornerTopLeft));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.TargetIsFreeCorner, result.Error!.Code);
    }

    [Fact]
    public void TwoEyed_Jack_Cannot_Target_Occupied_Space()
    {
        BoardPosition target = new(3, 3);
        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand("JC", "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, H.BoardWith((target, H.P1)), H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayTwoEyedJackAction(H.P0, Card.Parse("JC"), target));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.TargetOccupied, result.Error!.Code);
    }

    [Fact]
    public void TwoEyed_Jack_Cannot_Target_Outside_Board()
    {
        GameState state = StateWith(H.P0, "JC", "AS", "2C", "3D", "4S", "5D", "6S");

        ActionResult result = GameEngine.ApplyAction(state, new PlayTwoEyedJackAction(H.P0, Card.Parse("JC"), new BoardPosition(10, 5)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.TargetOutsideBoard, result.Error!.Code);
    }

    [Fact]
    public void OneEyed_Jack_Removes_Opponent_Chip()
    {
        BoardPosition target = new(3, 3);
        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand("JH", "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, H.BoardWith((target, H.P1)), H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayOneEyedJackAction(H.P0, Card.Parse("JH"), target));

        Assert.True(result.IsSuccess);
        Assert.False(result.NewState!.Board.IsOccupied(target));
        Assert.Contains(result.Events, e => e is ChipRemovedEvent removed && removed.RemovedPlayer == H.P1);
    }

    [Fact]
    public void OneEyed_Jack_Cannot_Remove_Own_Chip()
    {
        BoardPosition target = new(3, 3);
        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand("JH", "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, H.BoardWith((target, H.P0)), H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayOneEyedJackAction(H.P0, Card.Parse("JH"), target));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.CannotRemoveOwnChip, result.Error!.Code);
    }

    [Fact]
    public void OneEyed_Jack_Cannot_Remove_Locked_Chip()
    {
        // A completed Sequence by Player 1 occupies and locks (2..6, 2).
        var sequencePositions = new[] { H.Pos(2, 2), H.Pos(2, 3), H.Pos(2, 4), H.Pos(2, 5), H.Pos(2, 6) };
        BoardState locked = H.BoardWith(sequencePositions.Select(p => (p, H.P1)).ToArray());
        locked = locked.LockPositions(sequencePositions);

        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand("JH", "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, locked, H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayOneEyedJackAction(H.P0, Card.Parse("JH"), H.Pos(2, 4)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.CannotRemoveLockedChip, result.Error!.Code);
    }

    [Fact]
    public void OneEyed_Jack_Needs_A_Chip_To_Remove()
    {
        BoardPosition target = new(3, 3);
        GameState state = StateWith(H.P0, "JH", "AS", "2C", "3D", "4S", "5D", "6S");

        ActionResult result = GameEngine.ApplyAction(state, new PlayOneEyedJackAction(H.P0, Card.Parse("JH"), target));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.TargetNotOccupied, result.Error!.Code);
    }

    [Fact]
    public void Wrong_Jack_Type_Rejected()
    {
        GameState state = StateWith(H.P0, "JC", "AS", "2C", "3D", "4S", "5D", "6S");

        ActionResult result = GameEngine.ApplyAction(state, new PlayOneEyedJackAction(H.P0, Card.Parse("JC"), new BoardPosition(3, 3)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.WrongActionForCard, result.Error!.Code);
    }

    [Fact]
    public void NonJack_Cannot_Be_Played_Through_A_Jack_Action()
    {
        GameState state = StateWith(H.P0, "7H", "AS", "2C", "3D", "4S", "5D", "6S");

        ActionResult result = GameEngine.ApplyAction(state, new PlayTwoEyedJackAction(H.P0, Card.Parse("7H"), new BoardPosition(3, 3)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.WrongActionForCard, result.Error!.Code);
    }

    [Fact]
    public void Jack_Play_Draws_A_Replacement()
    {
        BoardPosition target = new(3, 3);
        GameState state = StateWith(H.P0, "JC", "AS", "2C", "3D", "4S", "5D", "6S");

        ActionResult result = GameEngine.ApplyAction(state, new PlayTwoEyedJackAction(H.P0, Card.Parse("JC"), target));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(Card.Parse("JC"), result.NewState!.GetPlayer(H.P0).Hand);
        Assert.Equal(7, result.NewState.GetPlayer(H.P0).Hand.Count);
    }
}