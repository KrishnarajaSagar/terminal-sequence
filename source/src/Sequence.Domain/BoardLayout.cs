namespace Sequence.Domain;

/// <summary>
/// An immutable, configured representation of the fixed 10x10 Sequence board.
/// The cell contents are pure data (<see cref="StandardLayoutData"/>); the board
/// is never generated or shuffled by the game logic.
/// </summary>
public sealed class BoardLayout
{
    public const int Size = 10;

    public const int CellCount = Size * Size; // 100

    private readonly BoardCell[] _cells; // row-major, index = row * Size + col

    private BoardLayout(IReadOnlyList<BoardCell> cells)
    {
        if (cells.Count != CellCount)
        {
            throw new ArgumentException($"A board layout must contain exactly {CellCount} cells, got {cells.Count}.", nameof(cells));
        }

        _cells = new BoardCell[CellCount];
        for (int i = 0; i < cells.Count; i++)
        {
            _cells[i] = cells[i];
        }

        Validate();
    }

    /// <summary>The standard Sequence board layout, identical for every game.</summary>
    public static BoardLayout Standard { get; } = CreateFromData();

    private static BoardLayout CreateFromData()
    {
        var cells = new List<BoardCell>(CellCount);
        foreach (string code in StandardLayoutData.Cells)
        {
            cells.Add(code == "FREE" ? BoardCell.Free : BoardCell.OccupiedBy(Card.Parse(code)));
        }

        return new BoardLayout(cells);
    }

    public BoardCell GetCell(int row, int col)
    {
        ThrowIfOutOfBounds(row, col);
        return _cells[row * Size + col];
    }

    public BoardCell GetCell(BoardPosition position) => GetCell(position.Row, position.Col);

    /// <summary>Card printed on a board space, or null if the space is a FREE corner.</summary>
    public Card? GetCardAt(BoardPosition position)
    {
        BoardCell cell = GetCell(position);
        return cell.IsFree ? null : cell.Card;
    }

    public bool IsFree(BoardPosition position) => GetCell(position).IsFree;

    public bool IsFreeCorner(BoardPosition position) => position.IsCorner && IsFree(position);

    /// <summary>
    /// Every board coordinate that shows the given card. Each non-Jack appears exactly
    /// twice on the standard board; Jacks never appear.
    /// </summary>
    public IReadOnlyList<BoardPosition> GetPositionsForCard(Card card)
    {
        var positions = new List<BoardPosition>(2);
        for (int row = 0; row < Size; row++)
        {
            for (int col = 0; col < Size; col++)
            {
                BoardCell cell = _cells[row * Size + col];
                if (!cell.IsFree && cell.Card == card)
                {
                    positions.Add(new BoardPosition(row, col));
                }
            }
        }

        return positions;
    }

    private void Validate()
    {
        var cardCounts = new Dictionary<Card, int>();
        int freeCorners = 0;

        for (int row = 0; row < Size; row++)
        {
            for (int col = 0; col < Size; col++)
            {
                BoardCell cell = _cells[row * Size + col];

                if (cell.IsFree)
                {
                    if (!new BoardPosition(row, col).IsCorner)
                    {
                        throw new InvalidDataException($"Board cell ({row},{col}) is FREE but is not a corner.");
                    }

                    freeCorners++;
                    continue;
                }

                if (cell.Card is null)
                {
                    throw new InvalidDataException($"Board cell ({row},{col}) is neither FREE nor holds a card.");
                }

                if (cell.Card.Value.IsJack)
                {
                    throw new InvalidDataException($"Jack {cell.Card.Value} must not appear on the board.");
                }

                cardCounts.TryGetValue(cell.Card.Value, out int current);
                cardCounts[cell.Card.Value] = current + 1;
            }
        }

        if (freeCorners != 4)
        {
            throw new InvalidDataException($"A standard board must have exactly 4 FREE corners, found {freeCorners}.");
        }

        if (cardCounts.Count != 48)
        {
            throw new InvalidDataException($"Exactly 48 non-Jack card identities are expected, found {cardCounts.Count}.");
        }

        foreach ((Card card, int count) in cardCounts)
        {
            if (count != 2)
            {
                throw new InvalidDataException($"Card {card} appears {count} times; each non-Jack card must appear exactly twice.");
            }
        }
    }

    private static void ThrowIfOutOfBounds(int row, int col)
    {
        if (row is < 0 or >= Size || col is < 0 or >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(row), $"Board coordinate ({row},{col}) is outside the {Size}x{Size} board.");
        }
    }
}