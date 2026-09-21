namespace Sequence.Domain;

/// <summary>
/// Zero-based board coordinate. The terminal UI may display it as (row + 1, col + 1).
/// </summary>
public readonly record struct BoardPosition(int Row, int Col)
{
    public const int BoardSize = 10;

    public bool IsInsideBoard => Row is >= 0 and < BoardSize && Col is >= 0 and < BoardSize;

    public bool IsCorner => IsInsideBoard && (Row is 0 or (BoardSize - 1)) && (Col is 0 or (BoardSize - 1));

    public override string ToString() => $"{Row + 1},{Col + 1}";

    public static BoardPosition FreeCornerTopLeft => new(0, 0);

    public static BoardPosition FreeCornerTopRight => new(0, BoardSize - 1);

    public static BoardPosition FreeCornerBottomLeft => new(BoardSize - 1, 0);

    public static BoardPosition FreeCornerBottomRight => new(BoardSize - 1, BoardSize - 1);
}

public readonly record struct BoardCell(bool IsFree, Card? Card)
{
    public static BoardCell Free => new(true, null);

    public static BoardCell OccupiedBy(Card card) => new(false, card);

    public override string ToString() => IsFree ? " " : (Card?.ToString() ?? "?");
}