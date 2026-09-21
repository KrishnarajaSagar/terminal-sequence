using System.Collections.Immutable;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

/// <summary>
/// End-to-end games driven through nothing but legal actions and player views,
/// mimicking a real client session.
/// </summary>
public class E2EGameTests
{
    private static GameState PlayCard(GameState state, PlayerId player, int row, int col)
    {
        Card card = H.CardAt(row, col);
        GameState staged = player == H.P0
            ? H.With(state, H.P0, p0Hand: H.Hand(card.Code))
            : H.With(state, H.P1, p1Hand: H.Hand(card.Code));

        ActionResult result = GameEngine.ApplyAction(staged, new PlayCardAction(player, card, new BoardPosition(row, col)));
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.NewState!;
    }

    [Fact]
    public void Two_Player_Game_Reaches_A_Winner_Through_Alternating_Real_Moves()
    {
        // P0 builds a horizontal five on row 4; P1 scatters chips far away so the
        // target of 1 gives P0 the win on the fifth P0 move.
        GameState state = H.Start(target: 1);

        foreach (int col in new[] { 4, 5, 6, 7, 8 })
        {
            state = PlayCard(state, H.P0, 4, col);
            if (col == 8)
            {
                break; // game ends on the winning move
            }

            Assert.Equal(TurnPhase.Playing, state.Phase);

            BoardPosition scatter = new[] { H.Pos(9, 2), H.Pos(2, 8), H.Pos(7, 9), H.Pos(1, 3) }[col - 4];
            state = PlayCard(state, H.P1, scatter.Row, scatter.Col);
            Assert.Equal(TurnPhase.Playing, state.Phase);
        }

        Assert.Equal(GameStatus.Won, state.Status);
        Assert.Equal(H.P0, state.Winner);
        Assert.Equal(1, state.SequenceCount(H.P0));
        Assert.Equal(TurnPhase.GameOver, state.Phase);

        for (int col = 4; col <= 8; col++)
        {
            Assert.True(state.Board.IsLocked(H.Pos(4, col)));
        }
    }

    [Fact]
    public void Views_Stay_Consistent_Across_The_Whole_Game()
    {
        GameState state = H.Start(target: 1);
        var log = new List<(PlayerId Player, string EventName)>();

        foreach (int col in new[] { 4, 5, 6, 7, 8 })
        {
            Card card = H.CardAt(4, col);
            GameState staged = H.With(state, H.P0, p0Hand: H.Hand(card.Code));
            ActionResult result = GameEngine.ApplyAction(staged, new PlayCardAction(H.P0, card, new BoardPosition(4, col)));
            Assert.True(result.IsSuccess, result.Error?.Message);
            log.AddRange(result.Events.Select(e => (H.P0, e.GetType().Name)));
            state = result.NewState!;

            if (col < 8)
            {
                BoardPosition scatter = new[] { H.Pos(9, 2), H.Pos(2, 8), H.Pos(7, 9), H.Pos(1, 3) }[col - 4];
                Card scattered = H.CardAt(scatter.Row, scatter.Col);
                GameState p1Staged = H.With(state, H.P1, p1Hand: H.Hand(scattered.Code));
                ActionResult p1 = GameEngine.ApplyAction(p1Staged, new PlayCardAction(H.P1, scattered, scatter));
                Assert.True(p1.IsSuccess, p1.Error?.Message);
                log.AddRange(p1.Events.Select(e => (H.P1, e.GetType().Name)));
                state = p1.NewState!;
            }
        }

        // The winning move must be the last event produced.
        Assert.Equal(GameStatus.Won, state.Status);
        Assert.Equal("GameWonEvent", log[^1].EventName);

        // A player view built from the final state is faithful and consistent.
        PlayerView p0View = StateProjection.GetPlayerView(state, H.P0);
        Assert.Equal(GameStatus.Won, p0View.Status);
        Assert.Equal(H.P0, p0View.Winner);
        Assert.Single(p0View.CompletedSequences);
        Assert.Equal(state.Board, p0View.Board);
        Assert.Contains(p0View.Players, p => p.Id == H.P0 && p.SequenceCount == 1);
        Assert.Contains(p0View.Players, p => p.Id == H.P1 && p.SequenceCount == 0);

        // Undo-style replay: every event contains enough to have rendered the outcome.
        Assert.Contains(log, e => e.EventName == "SequenceCompletedEvent");
        Assert.Contains(log, e => e.EventName == "ChipPlacedEvent");
    }
}