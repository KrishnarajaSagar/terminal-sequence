using Sequence.Domain;

namespace Sequence.Tests;

public class DeckTests
{
    [Fact]
    public void CreateStandard_Has_TwoCopies_Of_All_52_Cards()
    {
        var cards = Deck.CreateStandard();

        Assert.Equal(Deck.TotalCards, cards.Count);

        var groups = cards.GroupBy(c => c).OrderBy(g => g.Key.Rank).ThenBy(g => g.Key.Suit).ToList();
        Assert.Equal(52, groups.Count);
        Assert.All(groups, g => Assert.Equal(2, g.Count()));
    }

    [Fact]
    public void CreateStandard_Deterministic()
    {
        Assert.Equal(Deck.CreateStandard(), Deck.CreateStandard());
    }
}