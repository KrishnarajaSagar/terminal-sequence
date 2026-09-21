namespace Sequence.Domain;

public enum ActionErrorCode
{
    /// <summary>The game is already over; no further actions are accepted.</summary>
    GameOver,

    /// <summary>A player other than the current player attempted to act.</summary>
    NotYourTurn,

    /// <summary>The card was not present in the acting player's hand.</summary>
    CardNotInHand,

    /// <summary>The action type does not fit the card (e.g. a Jack played as a normal card).</summary>
    WrongActionForCard,

    /// <summary>The target space does not match the played card.</summary>
    TargetDoesNotMatchCard,

    /// <summary>The target space is already occupied (placement refused).</summary>
    TargetOccupied,

    /// <summary>The target space holds no chip (a one-eyed Jack must hit an opponent chip).</summary>
    TargetNotOccupied,

    /// <summary>FREE corners cannot receive chips.</summary>
    TargetIsFreeCorner,

    /// <summary>Target coordinates lie outside the board.</summary>
    TargetOutsideBoard,

    /// <summary>A one-eyed Jack may never remove the acting player's own chip.</summary>
    CannotRemoveOwnChip,

    /// <summary>A one-eyed Jack may not remove a chip of a completed Sequence.</summary>
    CannotRemoveLockedChip,

    /// <summary>The card is not dead; it still has a legal matching space and may not be exchanged.</summary>
    NotADeadCard,

    /// <summary>The player has already exchanged one dead card this turn.</summary>
    AlreadyExchangedThisTurn,

    /// <summary>The submitting client is not connected to any seat.</summary>
    ClientNotConnected,

    /// <summary>A client attempted to act for a seat it is not bound to.</summary>
    WrongPlayerForClient,

    /// <summary>The server could not parse a message sent by a client.</summary>
    MalformedMessage,
}

/// <summary>Rejection of an action. Ordinary user mistakes return this instead of throwing.</summary>
public sealed record ActionError(ActionErrorCode Code, string Message, PlayerId? PlayerId = null)
{
    public override string ToString() => Message;
}