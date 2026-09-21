namespace Sequence.Domain;

/// <summary>
/// The physical deck used by the game: two standard 52-card decks with no jokers,
/// so that every card identity occurs exactly twice.
/// </summary>
public static class Deck
{
    public const int DistinctCards = 52;

    public const int PhysicalCopiesPerCard = 2;

    public const int TotalCards = DistinctCards * PhysicalCopiesPerCard; // 104

    /// <summary>
    /// Builds the standard deck in a fixed, deterministic order: two copies of each
    /// of the 52 cards. Shuffling is the deck manager's responsibility.
    /// </summary>
    public static IReadOnlyList<Card> CreateStandard()
    {
        var cards = new List<Card>(TotalCards);

        for (int copy = 0; copy < PhysicalCopiesPerCard; copy++)
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                foreach (Suit suit in Enum.GetValues<Suit>())
                {
                    cards.Add(new Card(rank, suit));
                }
            }
        }

        return cards;
    }
}