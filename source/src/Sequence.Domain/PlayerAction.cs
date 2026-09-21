namespace Sequence.Domain;

/// <summary>
/// A decision submitted by a client. The engine validates and applies it; clients never
/// mutate game state directly.
/// </summary>
public abstract record PlayerAction(PlayerId PlayerId);

/// <summary>Playing a non-Jack card onto one of its two matching board positions.</summary>
public sealed record PlayCardAction(PlayerId PlayerId, Card Card, BoardPosition Target) : PlayerAction(PlayerId);

/// <summary>
/// Playing an empty-target Jack (clubs or diamonds): places a chip on any empty
/// non-corner space.
/// </summary>
public sealed record PlayTwoEyedJackAction(PlayerId PlayerId, Card Card, BoardPosition Target) : PlayerAction(PlayerId);

/// <summary>
/// Playing a removing Jack (hearts or spades): removes an opponent's unprotected chip.
/// </summary>
public sealed record PlayOneEyedJackAction(PlayerId PlayerId, Card Card, BoardPosition Target) : PlayerAction(PlayerId);

/// <summary>Discarding a dead card (both matching spaces occupied) and drawing a replacement.</summary>
public sealed record ExchangeDeadCardAction(PlayerId PlayerId, Card Card) : PlayerAction(PlayerId);