using System.Collections.Immutable;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

public class PlayerViewTests
{
    private static (GameState State, GameState Paused) ProgressedGame()
    {
        // Play one move so there is meaningful shared state, then start a fresh state
        // for the isolation tests below.
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(Card.Parse("7H"))[0];
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState after = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, Card.Parse("7H"), target)).NewState!;
        return (after, after);
    }

    [Fact]
    public void Viewer_Sees_Only_Their_Own_Hand()
    {
        (GameState state, _) = ProgressedGame();
        PlayerView p0 = StateProjection.GetPlayerView(state, H.P0);

        Assert.Equal(state.GetPlayer(H.P0).Hand, p0.Hand);
        Assert.Equal(7, p0.Hand.Count);
    }

    [Fact]
    public void Player_Summaries_Never_Contain_A_Hand()
    {
        (GameState state, _) = ProgressedGame();
        PlayerView p0 = StateProjection.GetPlayerView(state, H.P0);

        Assert.Equal(2, p0.Players.Count);
        Assert.All(p0.Players, p =>
        {
            Assert.NotNull(p.Name);
            Assert.DoesNotContain(p.GetType().GetProperties(), prop => prop.Name == "Hand"); // PlayerSummary carries no hand
        });
        Assert.Contains(p0.Players, p => p.IsCurrentPlayer);
    }

    [Fact]
    public void Draw_Pile_Contents_Are_Never_Exposed()
    {
        GameState state = H.Start();
        PlayerView view = StateProjection.GetPlayerView(state, H.P0);

        Assert.Equal(state.CardsRemainingInDrawPile, view.CardsRemainingInDrawPile);
    }

    [Fact]
    public void Discard_Pile_Is_Visible()
    {
        (GameState state, _) = ProgressedGame();
        PlayerView p0 = StateProjection.GetPlayerView(state, H.P0);

        Assert.Equal(state.DiscardPile, p0.DiscardPile);
        Assert.Contains(Card.Parse("7H"), p0.DiscardPile);
    }

    [Fact]
    public void Board_And_Sequences_Are_Visible_To_Both()
    {
        BoardState pre = H.BoardWith((H.Pos(2, 2), H.P0), (H.Pos(2, 3), H.P0), (H.Pos(2, 4), H.P0), (H.Pos(2, 5), H.P0));
        GameState baseState = H.With(H.Start(), H.P0, p0Hand: H.Hand(H.CardAt(2, 6).Code, "AS", "2C", "3D", "4S", "5D", "6S"));
        GameState state = H.WithBoard(baseState, pre, H.P0);
        GameState after = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, H.CardAt(2, 6), new BoardPosition(2, 6))).NewState!;

        PlayerView p0 = StateProjection.GetPlayerView(after, H.P0);
        PlayerView p1 = StateProjection.GetPlayerView(after, H.P1);

        Assert.Single(p0.CompletedSequences);
        Assert.Single(p1.CompletedSequences);
        Assert.Equal(p0.CompletedSequences[0], p1.CompletedSequences[0]);
        Assert.All(p0.CompletedSequences[0].Positions, pos => Assert.True(p0.Board.IsLocked(pos)));
    }

    [Fact]
    public void IsYourTurn_Is_Truthful_For_Every_Viewer()
    {
        (GameState state, _) = ProgressedGame();

        PlayerView p0 = StateProjection.GetPlayerView(state, H.P0);
        PlayerView p1 = StateProjection.GetPlayerView(state, H.P1);

        Assert.Equal(state.CurrentPlayerId == H.P0, p0.IsYourTurn);
        Assert.Equal(state.CurrentPlayerId == H.P1, p1.IsYourTurn);
        Assert.NotEqual(p0.IsYourTurn, p1.IsYourTurn);
    }

    [Fact]
    public void Unknown_Player_Is_Rejected()
    {
        GameState state = H.Start();
        Assert.Throws<InvalidOperationException>(() => StateProjection.GetPlayerView(state, new PlayerId(99)));
    }

    [Fact]
    public void Winner_And_Status_Are_Visible_To_All()
    {
        GameState won = WinState();
        PlayerView p0 = StateProjection.GetPlayerView(won, H.P0);
        PlayerView p1 = StateProjection.GetPlayerView(won, H.P1);

        Assert.Equal(GameStatus.Won, p0.Status);
        Assert.Equal(GameStatus.Won, p1.Status);
        Assert.Equal(H.P0, p0.Winner);
        Assert.Equal(H.P0, p1.Winner);

        static GameState WinState()
        {
            GameSetup setup = new("Alice", "Bob", 12345, 1);
            GameState s = H.Start(target: 1);
            return s with { SequenceCounts = s.SequenceCounts.SetItem(H.P0, 1) };
        }
    }
}