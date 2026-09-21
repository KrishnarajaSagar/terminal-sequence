namespace Sequence.Domain;

/// <summary>Lifecycle state of a game.</summary>
public enum GameStatus
{
    /// <summary>The game is being played.</summary>
    InProgress = 0,

    /// <summary>One player has completed the target number of sequences.</summary>
    Won = 1,
}