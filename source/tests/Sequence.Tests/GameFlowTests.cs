using System.Collections.Immutable;
using System.Linq;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

/// <summary>Integration-style tests driven purely through legal moves.</summary>
public class GameFlowTests
{
    [Fact]
    public void Turns_Alternate_And_Reset_Exchange_Flag()
    {
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(Card.Parse("7H"))[0];
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, Card.Parse("7H"), target));

        Assert.True(result.IsSuccess);
        GameState after = result.NewState!;
        Assert.Equal(H.P1, after.CurrentPlayerId);
        Assert.Equal(2, after.TurnNumber);
        Assert.False(after.CurrentPlayerHasExchangedDeadCard);
        Assert.Equal(TurnPhase.Playing, after.Phase);
        Assert.Contains(result.Events, e => e is TurnChangedEvent { NewPlayerId: var id } && id == H.P1);
    }

    [Fact]
    public void Six_Chips_In_A_Row_Score_Two_Sequences_Max()
    {
        // Target 3 so the game does not end when the line of six reaches 2 Sequences,
        // allowing us to verify that a seventh chip adds nothing further.
        GameState state = H.With(H.Start(target: 3), H.P0);
        int cumulative = 0;

        foreach (int col in new[] { 3, 4, 5, 6, 7, 8 })
        {
            Card card = H.CardAt(3, col);
            GameState handReset = H.With(state, H.P0, p0Hand: H.Hand(card.Code));

            ActionResult result = GameEngine.ApplyAction(handReset, new PlayCardAction(H.P0, card, new BoardPosition(3, col)));
            Assert.True(result.IsSuccess, result.Error?.Message);

            state = result.NewState!;
            cumulative += result.Events.Count(e => e is SequenceCompletedEvent);

            Assert.Equal(cumulative, state.SequenceCount(H.P0));
        }

        Assert.Equal(2, cumulative);

        // A seventh chip in the same line adds no further Sequence.
        Card seventh = H.CardAt(3, 9);
        GameState beforeSeventh = H.With(state, H.P0, p0Hand: H.Hand(seventh.Code));
        ActionResult extra = GameEngine.ApplyAction(beforeSeventh, new PlayCardAction(H.P0, seventh, new BoardPosition(3, 9)));

        Assert.True(extra.IsSuccess);
        Assert.Equal(2, extra.NewState!.SequenceCount(H.P0));
        Assert.Empty(extra.Events.OfType<SequenceCompletedEvent>());
    }

    [Fact]
    public void Completed_Sequences_Are_Locked_On_The_Board()
    {
        BoardState pre = H.BoardWith((H.Pos(2, 2), H.P0), (H.Pos(2, 3), H.P0), (H.Pos(2, 4), H.P0), (H.Pos(2, 5), H.P0));
        BoardPosition final = new(2, 6);
        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand(H.CardAt(2, 6).Code, "AS", "2C", "3D", "4S", "5D", "6S"));
        Card played = H.CardAt(2, 6);
        GameState state = H.WithBoard(baseState, pre, H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, played, final));

        Assert.True(result.IsSuccess);
        for (int col = 2; col <= 6; col++)
        {
            Assert.True(result.NewState!.Board.IsLocked(H.Pos(2, col)));
        }
    }

    [Fact]
    public void Win_On_Second_Sequence_Ends_The_Game_Immediately()
    {
        // Two crossing sequences completed by a single chip → the winner reaches 2.
        BoardState pre = H.BoardWith(
            (H.Pos(2, 2), H.P0), (H.Pos(2, 3), H.P0), (H.Pos(2, 4), H.P0), (H.Pos(2, 5), H.P0),
            (H.Pos(3, 6), H.P0), (H.Pos(4, 6), H.P0), (H.Pos(5, 6), H.P0), (H.Pos(6, 6), H.P0));
        BoardPosition final = new(2, 6);
        Card played = H.CardAt(2, 6);
        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand(played.Code, "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, pre, H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, played, final));

        Assert.True(result.IsSuccess);
        GameState after = result.NewState!;
        Assert.Equal(GameStatus.Won, after.Status);
        Assert.Equal(H.P0, after.Winner);
        Assert.Equal(TurnPhase.GameOver, after.Phase);
        Assert.Equal(2, after.SequenceCount(H.P0));
        Assert.Contains(result.Events, e => e is GameWonEvent { WinnerPlayerId: var id } && id == H.P0);
        Assert.DoesNotContain(result.Events, e => e is TurnChangedEvent);
    }

    [Fact]
    public void Single_Sequence_Wins_When_Target_Is_One()
    {
        BoardState pre = H.BoardWith((H.Pos(4, 4), H.P0), (H.Pos(4, 5), H.P0), (H.Pos(4, 6), H.P0), (H.Pos(4, 7), H.P0));
        BoardPosition final = new(4, 8);
        Card played = H.CardAt(4, 8);
        GameState baseState = H.With(H.Start(target: 1), H.P0, p0Hand: H.Hand(played.Code, "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, pre, H.P0);

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, played, final));

        Assert.True(result.IsSuccess);
        Assert.Equal(H.P0, result.NewState!.Winner);
        Assert.Equal(GameStatus.Won, result.NewState.Status);
    }

    [Fact]
    public void No_Actions_Are_Accepted_After_The_Game_Ends()
    {
        GameState won = WinState();
        GameState notYour = H.With(won, H.P1, p1Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(Card.Parse("7H"))[0];

        ActionResult result = GameEngine.ApplyAction(notYour, new PlayCardAction(H.P1, Card.Parse("7H"), target));

        Assert.False(result.IsSuccess);
        Assert.Equal(ActionErrorCode.GameOver, result.Error!.Code);

        static GameState WinState()
        {
            GameSetup setup = new("Alice", "Bob", 12345, 1);
            BoardState board = H.BoardWith((H.Pos(4, 4), H.P0), (H.Pos(4, 5), H.P0), (H.Pos(4, 6), H.P0), (H.Pos(4, 7), H.P0));

            ImmutableDictionary<PlayerId, int> counts = ImmutableDictionary<PlayerId, int>.Empty
                .Add(H.P0, 1).Add(H.P1, 0);

            return new GameState(
                setup,
                board,
                ImmutableList.Create(
                    new PlayerInfo(H.P0, "Alice", PlayerColor.Red, ImmutableList<Card>.Empty),
                    new PlayerInfo(H.P1, "Bob", PlayerColor.Green, H.Hand("7H"))),
                ImmutableQueue<Card>.Empty,
                ImmutableList<Card>.Empty,
                H.P0,
                5,
                false,
                ImmutableList<CompletedSequence>.Empty,
                counts,
                0,
                TurnPhase.GameOver);
        }
    }

    [Fact]
    public void Discard_And_Draw_Pile_Are_Maintained_Across_Moves()
    {
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(Card.Parse("7H"))[0];
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        int drawBefore = state.CardsRemainingInDrawPile;

        ActionResult first = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, Card.Parse("7H"), target));
        GameState afterFirst = first.NewState!;
        Assert.Equal(drawBefore - 1, afterFirst.CardsRemainingInDrawPile);
        Assert.Single(afterFirst.DiscardPile);

        ActionResult second = GameEngine.ApplyAction(
            H.With(afterFirst, H.P1, p1Hand: H.Hand("8H", "AS", "2C", "3D", "4S", "5D", "6S")),
            new PlayCardAction(H.P1, Card.Parse("8H"), BoardLayout.Standard.GetPositionsForCard(Card.Parse("8H"))[1]));
        Assert.True(second.IsSuccess);
        Assert.Equal(2, second.NewState!.DiscardPile.Count);
    }
}