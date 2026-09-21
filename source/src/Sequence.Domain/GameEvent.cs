using System.Text.Json.Serialization;

namespace Sequence.Domain;

/// <summary>
/// Structured description of what happened when an action was applied. Events carry
/// enough detail for a client to render the result and never depend on a console or
/// network. The <c>type</c> discriminator lets them be serialized polymorphically for
/// network messages and saved game logs.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CardPlayedEvent), "cardPlayed")]
[JsonDerivedType(typeof(ChipPlacedEvent), "chipPlaced")]
[JsonDerivedType(typeof(ChipRemovedEvent), "chipRemoved")]
[JsonDerivedType(typeof(DeadCardExchangedEvent), "deadCardExchanged")]
[JsonDerivedType(typeof(CardDrawnEvent), "cardDrawn")]
[JsonDerivedType(typeof(DeckReshuffledEvent), "deckReshuffled")]
[JsonDerivedType(typeof(SequenceCompletedEvent), "sequenceCompleted")]
[JsonDerivedType(typeof(TurnChangedEvent), "turnChanged")]
[JsonDerivedType(typeof(GameWonEvent), "gameWon")]
public abstract record GameEvent;

/// <summary>A card was discarded from the current player's hand.</summary>
public sealed record CardPlayedEvent(PlayerId PlayerId, Card Card) : GameEvent;

/// <summary>A chip was placed on the board.</summary>
public sealed record ChipPlacedEvent(PlayerId PlayerId, BoardPosition Position) : GameEvent;

/// <summary>A chip was removed by a one-eyed Jack.</summary>
public sealed record ChipRemovedEvent(PlayerId PlayerId, BoardPosition Position, PlayerId RemovedPlayer) : GameEvent;

/// <summary>The current player exchanged a dead card for a replacement.</summary>
public sealed record DeadCardExchangedEvent(PlayerId PlayerId, Card DiscardedCard, Card ReplacementCard) : GameEvent;

/// <summary>A card was drawn from the draw pile.</summary>
public sealed record CardDrawnEvent(PlayerId PlayerId, Card Card) : GameEvent;

/// <summary>The draw pile ran out and the discard pile was reshuffled into a new draw pile.</summary>
public sealed record DeckReshuffledEvent(int CardsReshuffled) : GameEvent;

/// <summary>A player completed a Sequence of exactly five positions.</summary>
public sealed record SequenceCompletedEvent(PlayerId PlayerId, IReadOnlyList<BoardPosition> Positions) : GameEvent;

/// <summary>Control passed to the next player.</summary>
public sealed record TurnChangedEvent(PlayerId NewPlayerId, int TurnNumber) : GameEvent;

/// <summary>A player reached the target number of Sequences and the game ended.</summary>
public sealed record GameWonEvent(PlayerId WinnerPlayerId) : GameEvent;