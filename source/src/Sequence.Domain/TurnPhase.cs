namespace Sequence.Domain;

/// <summary>Stage of the turn-model that the engine is currently in. Drawing and turn
/// advancement are engine-internal consequences of a completed play, so the only
/// client decision points are: play a card, or exchange one dead card.</summary>
public enum TurnPhase
{
    /// <summary>It is the current player's turn and they owe a card play.</summary>
    Playing = 0,

    /// <summary>The game has ended; no further actions are legal.</summary>
    GameOver = 1,
}