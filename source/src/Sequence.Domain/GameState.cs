using System.Collections.Immutable;

namespace Sequence.Domain;

/// <summary>
/// The complete, immutable state of a game. Every transition produces a new
/// <see cref="GameState"/>; the current one is always the latest member of that chain.
/// </summary>
public sealed record GameState(
    GameSetup Setup,
    BoardState Board,
    ImmutableList<PlayerInfo> Players,
    ImmutableQueue<Card> DrawPile,
    ImmutableList<Card> DiscardPile,
    PlayerId CurrentPlayerId,
    int TurnNumber,
    bool CurrentPlayerHasExchangedDeadCard,
    ImmutableList<CompletedSequence> CompletedSequences,
    ImmutableDictionary<PlayerId, int> SequenceCounts,
    int ShuffleRound,
    TurnPhase Phase)
{
    public static GameState New(GameSetup setup)
    {
        return new GameState(
            Setup: setup,
            Board: BoardState.Empty,
            Players: ImmutableList<PlayerInfo>.Empty,
            DrawPile: ImmutableQueue<Card>.Empty,
            DiscardPile: ImmutableList<Card>.Empty,
            CurrentPlayerId: new PlayerId(0),
            TurnNumber: 1,
            CurrentPlayerHasExchangedDeadCard: false,
            CompletedSequences: ImmutableList<CompletedSequence>.Empty,
            SequenceCounts: ImmutableDictionary<PlayerId, int>.Empty,
            ShuffleRound: 0,
            Phase: TurnPhase.Playing);
    }

    public bool IsOver => Status == GameStatus.Won;

    public PlayerInfo CurrentPlayer => Players.Single(p => p.Id == CurrentPlayerId);

    public PlayerInfo GetPlayer(PlayerId playerId) =>
        Players.Single(p => p.Id == playerId);

    public int SequenceCount(PlayerId player) =>
        SequenceCounts.TryGetValue(player, out int count) ? count : 0;

    public bool HasReachedSequenceTarget(PlayerId player) =>
        SequenceCount(player) >= Setup.SequenceTarget;

    public GameStatus Status => Winner is null ? GameStatus.InProgress : GameStatus.Won;

    public PlayerId? Winner
    {
        get
        {
            foreach ((PlayerId id, int count) in SequenceCounts)
            {
                if (count >= Setup.SequenceTarget)
                {
                    return id;
                }
            }

            return null;
        }
    }

    public int CardsRemainingInDrawPile => DrawPile.Count();

    public GameState WithPlayerHand(PlayerId playerId, ImmutableList<Card> hand)
    {
        int index = Players.FindIndex(p => p.Id == playerId);
        return this with { Players = Players.SetItem(index, Players[index] with { Hand = hand }) };
    }

    public GameState WithPlayerName(PlayerId playerId, string name)
    {
        int index = Players.FindIndex(p => p.Id == playerId);
        return this with { Players = Players.SetItem(index, Players[index] with { Name = name }) };
    }

    public GameState WithBoard(BoardState board) => this with { Board = board };

    public GameState WithDrawPile(ImmutableQueue<Card> drawPile) => this with { DrawPile = drawPile };

    public GameState WithDiscardPile(ImmutableList<Card> discards) => this with { DiscardPile = discards };
}