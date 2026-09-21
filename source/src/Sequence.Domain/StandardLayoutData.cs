namespace Sequence.Domain;

/// <summary>
/// The fixed cell data of the standard Sequence board, row by row.
/// "FREE" marks the four corner spaces; every other cell holds a non-Jack card code.
/// This is pure configuration data and is validated by <see cref="BoardLayout"/>.
/// </summary>
public static class StandardLayoutData
{
    public static IReadOnlyList<string> Cells { get; } = new[]
    {
        "FREE", "10S", "QS", "KS", "AS", "2D", "3D", "4D", "5D", "FREE",
        "9S", "10H", "9H", "8H", "7H", "6H", "5H", "4H", "3H", "6D",
        "8S", "10C", "2H", "5C", "6C", "7C", "8C", "9C", "AH", "7D",
        "7S", "KC", "KH", "QC", "QH", "10C", "2H", "5C", "6C", "8D",
        "6S", "7C", "8C", "9C", "AH", "KC", "KH", "QC", "QH", "9D",
        "5S", "10D", "10S", "2C", "2D", "3C", "3D", "4C", "4D", "10D",
        "4S", "5D", "5S", "6D", "6S", "7D", "7S", "8D", "8S", "QD",
        "3S", "9D", "9S", "AC", "AD", "KD", "KS", "QD", "QS", "KD",
        "2S", "10H", "9H", "8H", "7H", "6H", "5H", "4H", "3H", "AD",
        "FREE", "4C", "3C", "2C", "AC", "AS", "2S", "3S", "4S", "FREE",
    };
}