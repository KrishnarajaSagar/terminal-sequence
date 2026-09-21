using Sequence.Domain;

namespace Sequence.Engine;

/// <summary>
/// Deterministic Fisher-Yates shuffle. For a fixed input and a fixed
/// <see cref="IRandom"/> seed the result is always identical.
/// </summary>
public static class DeckShuffler
{
    public static Card[] Shuffle(IReadOnlyList<Card> source, IRandom random)
    {
        Card[] cards = source.ToArray();

        for (int i = cards.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return cards;
    }
}