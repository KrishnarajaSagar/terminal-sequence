namespace Sequence.Domain;

/// <summary>Parameters fixed for the whole game, before any cards are dealt.</summary>
public sealed record GameSetup(
    string FirstPlayerName,
    string SecondPlayerName,
    int DeckSeed,
    int SequenceTarget = 2,
    PlayerColor FirstPlayerColor = PlayerColor.Red,
    PlayerColor SecondPlayerColor = PlayerColor.Green)
{
    public const int MinPlayers = 2;

    public const int MaxPlayers = 6;

    public const int MinHandSize = 7;
}