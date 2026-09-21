using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

public class StartGameTests
{
    private static GameSetup Setup(string seedSuffix = "", int seed = 20240921, int target = 2) =>
        new(
            FirstPlayerName: $"Player One {seedSuffix}",
            SecondPlayerName: $"Player Two {seedSuffix}",
            DeckSeed: seed,
            SequenceTarget: target);

    private static bool CompareForTest(Card a, Card b) => a.Rank != b.Rank ? a.Rank > b.Rank : a.Suit > b.Suit;

    [Fact]
    public void Deals_Seven_Cards_To_Each_Player()
    {
        GameState state = GameEngine.StartGame(Setup());

        Assert.Equal(2, state.Players.Count);
        Assert.All(state.Players, p => Assert.Equal(7, p.Hand.Count));

        var combined = state.Players.SelectMany(p => p.Hand).ToList();
        Assert.Equal(14, combined.Distinct().Count());
    }

    [Fact]
    public void Leaves_90_Cards_In_The_Draw_Pile_And_None_Discarded()
    {
        GameState state = GameEngine.StartGame(Setup());

        Assert.Equal(Deck.TotalCards - 14, state.CardsRemainingInDrawPile);
        Assert.Empty(state.DiscardPile);
    }

    [Fact]
    public void First_Player_Is_The_Player_Who_Received_The_Higher_Opening_Card()
    {
        var setup = Setup();
        Card[] shuffled = DeckShuffler.Shuffle(Deck.CreateStandard(), new SeededRandom(setup.DeckSeed));
        Card higher = CompareForTest(shuffled[0], shuffled[1]) ? shuffled[0] : shuffled[1];

        GameState state = GameEngine.StartGame(setup);
        PlayerInfo starter = state.GetPlayer(state.CurrentPlayerId);
        PlayerInfo other = state.GetPlayer(state.CurrentPlayerId == new PlayerId(0) ? new PlayerId(1) : new PlayerId(0));

        // The starter keeps the higher opening card and receives the first card of the deal.
        Assert.Equal(higher, starter.Hand[0]);
        Assert.NotEqual(higher, other.Hand[0]);

        // The opener is the higher card; equal ranks fall back to suit order.
        bool firstWins = starter.Hand[0].Rank != other.Hand[0].Rank
            ? starter.Hand[0].Rank > other.Hand[0].Rank
            : starter.Hand[0].Suit > other.Hand[0].Suit;
        Assert.True(firstWins);
    }

    [Fact]
    public void First_Player_Turns_On_Turn_One()
    {
        GameState state = GameEngine.StartGame(Setup());

        Assert.Equal(GameStatus.InProgress, state.Status);
        Assert.Equal(1, state.TurnNumber);
        Assert.False(state.CurrentPlayerHasExchangedDeadCard);
        Assert.Null(state.Winner);
        Assert.Empty(state.Board.Chips);
        Assert.Empty(state.Board.LockedPositions);
    }

    [Fact]
    public void Same_Seed_Produces_Identical_Game_State()
    {
        GameState a = GameEngine.StartGame(Setup());
        GameState b = GameEngine.StartGame(Setup());

        Assert.Equal(a.CurrentPlayerId, b.CurrentPlayerId);
        Assert.Equal(a.Players[0].Hand, b.Players[0].Hand);
        Assert.Equal(a.Players[1].Hand, b.Players[1].Hand);
        Assert.Equal(a.DrawPile.ToArray(), b.DrawPile.ToArray());
        Assert.Equal(a.TurnNumber, b.TurnNumber);
    }

    [Fact]
    public void Different_Seeds_Produce_Different_Hands()
    {
        GameState a = GameEngine.StartGame(Setup(seed: 1));
        GameState b = GameEngine.StartGame(Setup(seed: 2));

        Assert.NotEqual(a.Players[0].Hand, b.Players[0].Hand);
    }

    [Fact]
    public void Invalid_Setup_Throws()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.StartGame(Setup() with { FirstPlayerName = "  " }));
        Assert.Throws<ArgumentException>(() => GameEngine.StartGame(Setup() with { SecondPlayerName = "" }));
        Assert.Throws<ArgumentException>(() => GameEngine.StartGame(Setup() with { SequenceTarget = 0 }));
    }
}