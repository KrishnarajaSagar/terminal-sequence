using System.Collections.Immutable;

namespace Sequence.Domain;

/// <summary>
/// A single client's projection of the game. Derived from the authoritative
/// <see cref="GameState"/> by the engine: the viewer's own hand, the public board
/// state, the visible common discard pile, and all completed sequences are included;
/// the opponent's hand and the draw pile contents are never exposed.
/// </summary>
public sealed record PlayerView(
    PlayerId ViewerId,
    string ViewerName,
    BoardState Board,
    ImmutableList<Card> Hand,
    ImmutableList<Card> DiscardPile,
    int CardsRemainingInDrawPile,
    int SequenceTarget,
    PlayerId CurrentPlayerId,
    int TurnNumber,
    GameStatus Status,
    PlayerId? Winner,
    ImmutableList<CompletedSequence> CompletedSequences,
    ImmutableList<PlayerSummary> Players,
    bool IsYourTurn,
    bool HasExchangedDeadCardThisTurn)
{
    public PlayerSummary Viewer => Players.Single(p => p.Id == ViewerId);

    public bool IsOver => Status == GameStatus.Won;
}