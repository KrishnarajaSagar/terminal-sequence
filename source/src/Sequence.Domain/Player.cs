namespace Sequence.Domain;

/// <summary>Chip colors a player can be assigned. A two-player game uses two distinct colors.</summary>
public enum PlayerColor
{
    Red = 0,
    Green = 1,
    Blue = 2,
    Yellow = 3,
    Magenta = 4,
    Cyan = 5,
}

/// <summary>Identifies a player seat. Starts at 0 for the first player named in the setup.</summary>
public readonly record struct PlayerId(int Value);

/// <summary>Immutable description of one player in a game, including their current hand.</summary>
public sealed record PlayerInfo(
    PlayerId Id,
    string Name,
    PlayerColor Color,
    System.Collections.Immutable.ImmutableList<Card> Hand);