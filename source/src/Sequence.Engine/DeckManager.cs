using System.Collections.Immutable;
using Sequence.Domain;

namespace Sequence.Engine;

/// <summary>
/// Owns everything about the draw pile: drawing a card, and recycling the discard pile
/// into a fresh draw pile (seeded, round-based) when the draw pile runs empty. Board
/// logic never mixes with deck management.
/// </summary>
public static class DeckManager
{
    /// <summary>One draw with its bookkeeping.</summary>
    public sealed record DrawOutcome(
        Card Drawn,
        bool WasReshuffled,
        int ReshuffledCardCount,
        int NextShuffleRound,
        ImmutableQueue<Card> DrawPile,
        ImmutableList<Card> DiscardPile)
    {
        public static DrawOutcome FromTop(Card drawn, ImmutableQueue<Card> drawPile, ImmutableList<Card> discards, int shuffleRound) =>
            new(drawn, false, 0, shuffleRound, drawPile, discards);

        public static DrawOutcome FromRecycling(
            Card drawn,
            int reshuffledCount,
            int shuffleRound,
            ImmutableQueue<Card> drawPile,
            ImmutableList<Card> discards) =>
            new(drawn, true, reshuffledCount, shuffleRound, drawPile, discards);
    }

    /// <summary>
    /// Peeks-and-dequeues the top card. When the pile is empty, every discarded card is
    /// shuffled (seeded by <paramref name="baseSeed"/> and the next round number) into a
    /// new draw pile, the discard pile clears, and the first card of the new pile is drawn.
    /// </summary>
    public static DrawOutcome Draw(
        ImmutableQueue<Card> drawPile,
        ImmutableList<Card> discards,
        int shuffleRound,
        int baseSeed)
    {
        if (!drawPile.IsEmpty)
        {
            return DrawOutcome.FromTop(drawn: drawPile.Peek(), drawPile: drawPile.Dequeue(), discards: discards, shuffleRound);
        }

        if (discards.IsEmpty)
        {
            throw new InvalidOperationException("Cannot draw: the draw pile is empty and there are no discarded cards to recycle.");
        }

        int nextRound = shuffleRound + 1;
        Card[] ordered = DeckShuffler.Shuffle(discards.ToArray(), new SeededRandom(RandomSeeds.Derive(baseSeed, nextRound)));

        ImmutableQueue<Card> recycled = ImmutableQueue<Card>.Empty;
        foreach (Card card in ordered)
        {
            recycled = recycled.Enqueue(card);
        }

        return DrawOutcome.FromRecycling(
            drawn: recycled.Peek(),
            reshuffledCount: ordered.Length,
            shuffleRound: nextRound,
            drawPile: recycled.Dequeue(),
            discards: ImmutableList<Card>.Empty);
    }
}