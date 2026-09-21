using System.Collections.Immutable;
using Sequence.Domain;

namespace Sequence.Engine;

/// <summary>
/// Deterministic detection of newly completed Sequences. A move can only complete
/// Sequences that run through the space just chipped, so every candidate is built from
/// the four lines (horizontal, vertical, and both diagonals) through that space.
/// FREE corners count as belonging to every player.
/// </summary>
public static class SequenceDetector
{
    private static readonly (int DR, int DC)[] Directions =
    {
        (0, 1), // horizontal
        (1, 0), // vertical
        (1, 1), // diagonal down-right
        (1, -1), // diagonal down-left
    };

    /// <summary>
    /// Returns the Sequences of exactly five positions that <paramref name="placed"/>
    /// just completed for <paramref name="player"/>. At most two windows are ever taken
    /// from one maximal line — the window anchored at the line's first position and the
    /// window anchored at its second position — matching a physical board, where a chain
    /// of six or more still means exactly two Sequences. Windows are deduplicated by
    /// position-set equality.
    /// </summary>
    public static IReadOnlyList<ImmutableArray<BoardPosition>> FindNewlyCompleted(
        BoardState board,
        BoardLayout layout,
        BoardPosition placed,
        PlayerId player)
    {
        var found = new List<ImmutableArray<BoardPosition>>();
        var seen = new HashSet<ImmutableArray<BoardPosition>>();

        foreach ((int dRow, int dCol) in Directions)
        {
            List<BoardPosition> segment = BuildMaximalSegment(board, layout, placed, player, dRow, dCol);

            // A line of 5 scores its first anchored window; a line of 6 or more also
            // scores the second. Windows that do not contain the newly placed chip were
            // completed by earlier moves and are already recorded.
            if (segment.Count >= 5)
            {
                TryRegister(segment.GetRange(0, 5));
            }

            if (segment.Count >= 6)
            {
                TryRegister(segment.GetRange(1, 5));
            }
        }

        return found;

        void TryRegister(List<BoardPosition> window)
        {
            if (!window.Contains(placed))
            {
                return;
            }

            ImmutableArray<BoardPosition> key = CompletedSequence.Order(window);
            if (seen.Add(key))
            {
                found.Add(key);
            }
        }
    }

    /// <summary>
    /// The maximal run through <paramref name="placed"/> along (dRow, dCol) of positions
    /// that the player owns (a matching chip or a FREE corner), ordered start-to-end.
    /// </summary>
    private static List<BoardPosition> BuildMaximalSegment(
        BoardState board,
        BoardLayout layout,
        BoardPosition placed,
        PlayerId player,
        int dRow,
        int dCol)
    {
        var before = new List<BoardPosition>();
        var after = new List<BoardPosition>();

        BoardPosition cursor = new(placed.Row + dRow, placed.Col + dCol);
        while (BelongsTo(cursor))
        {
            after.Add(cursor);
            cursor = new(cursor.Row + dRow, cursor.Col + dCol);
        }

        cursor = new(placed.Row - dRow, placed.Col - dCol);
        while (BelongsTo(cursor))
        {
            before.Add(cursor);
            cursor = new(cursor.Row - dRow, cursor.Col - dCol);
        }

        before.Reverse();

        var segment = new List<BoardPosition>(before.Count + 1 + after.Count);
        segment.AddRange(before);
        segment.Add(placed);
        segment.AddRange(after);
        return segment;

        bool BelongsTo(BoardPosition position)
        {
            if (!position.IsInsideBoard)
            {
                return false;
            }

            if (layout.IsFreeCorner(position))
            {
                return true;
            }

            return board.Chips.TryGetValue(position, out PlayerId owner) && owner == player;
        }
    }
}