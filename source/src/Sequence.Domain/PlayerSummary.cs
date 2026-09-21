namespace Sequence.Domain;

/// <summary>Public, no-hand information about a participant, safe to broadcast to everyone.</summary>
public sealed record PlayerSummary(
    PlayerId Id,
    string Name,
    PlayerColor Color,
    int SequenceCount,
    bool IsCurrentPlayer);