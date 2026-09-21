using System.Collections.Immutable;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

public class DeckManagerTests
{
    private static ImmutableQueue<Card> Queue(params string[] codes)
    {
        ImmutableQueue<Card> q = ImmutableQueue<Card>.Empty;
        foreach (string code in codes)
        {
            q = q.Enqueue(Card.Parse(code));
        }

        return q;
    }

    private static ImmutableList<Card> List(params string[] codes) => ImmutableList.CreateRange(codes.Select(Card.Parse));

    [Fact]
    public void Draw_Takes_The_Top_Card()
    {
        DeckManager.DrawOutcome outcome = DeckManager.Draw(
            Queue("7H", "8C"),
            List("KD"),
            shuffleRound: 0,
            baseSeed: 99);

        Assert.Equal(Card.Parse("7H"), outcome.Drawn);
        Assert.False(outcome.WasReshuffled);
        Assert.Equal(0, outcome.NextShuffleRound);
        Assert.Single(outcome.DrawPile);
        Assert.Single(outcome.DiscardPile);
    }

    [Fact]
    public void Recycled_When_The_Draw_Pile_Is_Empty()
    {
        DeckManager.DrawOutcome outcome = DeckManager.Draw(
            ImmutableQueue<Card>.Empty,
            List("7H", "8C", "KD"),
            shuffleRound: 2,
            baseSeed: 99);

        Assert.True(outcome.WasReshuffled);
        Assert.Equal(3, outcome.ReshuffledCardCount);
        Assert.Equal(3, outcome.NextShuffleRound);
        Assert.Equal(2, outcome.DrawPile.Count()); // one card drawn, two remain
        Assert.Empty(outcome.DiscardPile);
        Assert.Contains(outcome.Drawn, List("7H", "8C", "KD"));
    }

    [Fact]
    public void Recycling_Is_Deterministic_For_The_Same_Seed()
    {
        var args = (ImmutableQueue<Card>.Empty, List("7H", "8C", "KD", "AS"), 0, 42);

        DeckManager.DrawOutcome a = DeckManager.Draw(args.Item1, args.Item2, args.Item3, args.Item4);
        DeckManager.DrawOutcome b = DeckManager.Draw(args.Item1, args.Item2, args.Item3, args.Item4);

        Assert.Equal(a.Drawn, b.Drawn);
        Assert.Equal(a.DrawPile.ToArray(), b.DrawPile.ToArray());
    }

    [Fact]
    public void Fails_When_Nothing_Can_Be_Recycled()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DeckManager.Draw(ImmutableQueue<Card>.Empty, ImmutableList<Card>.Empty, 0, 42));
    }

    [Fact]
    public void Play_That_Sweeps_The_Empty_Draw_Pile_Recycles_Discards()
    {
        // Draw pile is empty, three cards waiting in the discard pile.
        GameState state = H.With(H.Start(), H.P0, p0Hand: H.Hand("7H", "AS", "2C", "3D", "4S", "5D", "6S"));
        state = state
            .WithDrawPile(ImmutableQueue<Card>.Empty)
            .WithDiscardPile(List("3C", "9D", "KS"));
        BoardPosition target = BoardLayout.Standard.GetPositionsForCard(Card.Parse("7H"))[0];

        ActionResult result = GameEngine.ApplyAction(state, new PlayCardAction(H.P0, Card.Parse("7H"), target));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Events, e => e is DeckReshuffledEvent { CardsReshuffled: 3 });
        Assert.Contains(result.Events, e => e is CardDrawnEvent);
        Assert.Equal(1, result.NewState!.ShuffleRound);
        Assert.Equal(2, result.NewState.CardsRemainingInDrawPile); // 3 recycled - 1 drawn
        Assert.Single(result.NewState.DiscardPile); // played card only
    }
}