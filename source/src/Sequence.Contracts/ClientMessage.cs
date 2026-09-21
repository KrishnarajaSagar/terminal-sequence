using System.Text.Json.Serialization;
using Sequence.Domain;

namespace Sequence.Contracts;

/// <summary>
/// A request sent from a client to the server. The acting seat is decided by the server
/// from the connection, never carried in the message, so a client cannot act for another
/// player. Each message maps onto the corresponding engine action.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PlayCardMessage), "playCard")]
[JsonDerivedType(typeof(PlayTwoEyedJackMessage), "playTwoEyedJack")]
[JsonDerivedType(typeof(PlayOneEyedJackMessage), "playOneEyedJack")]
[JsonDerivedType(typeof(ExchangeDeadCardMessage), "exchangeDeadCard")]
public abstract record ClientMessage
{
    /// <summary>Maps this request onto the corresponding engine action for <paramref name="seat"/>.</summary>
    public abstract PlayerAction ToAction(PlayerId seat);
}

/// <summary>Playing a non-Jack card onto one of its matching board positions.</summary>
public sealed record PlayCardMessage(Card Card, BoardPosition Target) : ClientMessage
{
    public override PlayerAction ToAction(PlayerId seat) => new PlayCardAction(seat, Card, Target);
}

/// <summary>Playing a two-eyed Jack onto any empty non-corner space.</summary>
public sealed record PlayTwoEyedJackMessage(Card Card, BoardPosition Target) : ClientMessage
{
    public override PlayerAction ToAction(PlayerId seat) => new PlayTwoEyedJackAction(seat, Card, Target);
}

/// <summary>Playing a one-eyed Jack to remove an opponent's unprotected chip.</summary>
public sealed record PlayOneEyedJackMessage(Card Card, BoardPosition Target) : ClientMessage
{
    public override PlayerAction ToAction(PlayerId seat) => new PlayOneEyedJackAction(seat, Card, Target);
}

/// <summary>Discarding a dead card (its two spaces are both occupied) and drawing a replacement.</summary>
public sealed record ExchangeDeadCardMessage(Card Card) : ClientMessage
{
    public override PlayerAction ToAction(PlayerId seat) => new ExchangeDeadCardAction(seat, Card);
}