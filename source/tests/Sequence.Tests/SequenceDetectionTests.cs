using System.Collections.Immutable;
using System.Linq;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

/// <summary>Unit tests for the pure sequence detector (a BoardState deliberately
/// crafted without going through moves).</summary>
public class SequenceDetectionTests
{
    private static IReadOnlyList<ImmutableArray<BoardPosition>> Detect(BoardState board, BoardPosition placed, PlayerId player) =>
        SequenceDetector.FindNewlyCompleted(board, BoardLayout.Standard, placed, player);

    private static void AssertSequence(IReadOnlyList<ImmutableArray<BoardPosition>> detected, IEnumerable<BoardPosition> expected)
    {
        ImmutableArray<BoardPosition> canonical = CompletedSequence.Order(expected);
        Assert.Contains(detected, window => window.SequenceEqual(canonical));
    }

    [Fact]
    public void Horizontal_Five_Detected()
    {
        BoardState board = H.BoardWith((H.Pos(2, 2), H.P0), (H.Pos(2, 3), H.P0), (H.Pos(2, 4), H.P0), (H.Pos(2, 5), H.P0));

        var windows = Detect(board, new BoardPosition(2, 6), H.P0);

        Assert.Single(windows);
        AssertSequence(windows, new[] { H.Pos(2, 2), H.Pos(2, 3), H.Pos(2, 4), H.Pos(2, 5), H.Pos(2, 6) });
    }

    [Fact]
    public void Vertical_Five_Detected()
    {
        BoardState board = H.BoardWith((H.Pos(3, 3), H.P0), (H.Pos(4, 3), H.P0), (H.Pos(5, 3), H.P0), (H.Pos(6, 3), H.P0));

        var windows = Detect(board, new BoardPosition(7, 3), H.P0);

        Assert.Single(windows);
        AssertSequence(windows, new[] { H.Pos(3, 3), H.Pos(4, 3), H.Pos(5, 3), H.Pos(6, 3), H.Pos(7, 3) });
    }

    [Fact]
    public void Diagonal_Five_Detected()
    {
        BoardState board = H.BoardWith((H.Pos(2, 2), H.P0), (H.Pos(3, 3), H.P0), (H.Pos(4, 4), H.P0), (H.Pos(5, 5), H.P0));

        var windows = Detect(board, new BoardPosition(6, 6), H.P0);

        Assert.Single(windows);
        AssertSequence(windows, new[] { H.Pos(2, 2), H.Pos(3, 3), H.Pos(4, 4), H.Pos(5, 5), H.Pos(6, 6) });
    }

    [Fact]
    public void Line_Of_Six_Adds_The_Second_Anchored_Window()
    {
        // A 5-line already on the board…
        BoardState board = H.BoardWith((H.Pos(3, 3), H.P0), (H.Pos(3, 4), H.P0), (H.Pos(3, 5), H.P0), (H.Pos(3, 6), H.P0), (H.Pos(3, 7), H.P0));

        // …completed to 6 by the sixth chip.
        var windows = Detect(board, new BoardPosition(3, 8), H.P0);

        Assert.Single(windows);
        AssertSequence(windows, new[] { H.Pos(3, 4), H.Pos(3, 5), H.Pos(3, 6), H.Pos(3, 7), H.Pos(3, 8) });
        Assert.DoesNotContain(windows, w => w.Contains(H.Pos(3, 3)));
    }

    [Fact]
    public void Seven_Line_Still_Yields_No_Third_Window()
    {
        BoardState board = H.BoardWith(
            (H.Pos(3, 3), H.P0), (H.Pos(3, 4), H.P0), (H.Pos(3, 5), H.P0),
            (H.Pos(3, 6), H.P0), (H.Pos(3, 7), H.P0), (H.Pos(3, 8), H.P0));

        var windows = Detect(board, new BoardPosition(3, 9), H.P0);

        Assert.Empty(windows);
    }

    [Fact]
    public void Free_Corner_Counts_As_Every_Players_Wildcard()
    {
        BoardState board = H.BoardWith((H.Pos(1, 1), H.P0), (H.Pos(2, 2), H.P0), (H.Pos(3, 3), H.P0));

        var windows = Detect(board, new BoardPosition(4, 4), H.P0);

        Assert.Single(windows);
        AssertSequence(windows, new[] { BoardPosition.FreeCornerTopLeft, H.Pos(1, 1), H.Pos(2, 2), H.Pos(3, 3), H.Pos(4, 4) });
    }

    [Fact]
    public void One_Move_Can_Complete_Two_Sequences_That_Overlap_By_One_Space()
    {
        BoardState board = H.BoardWith(
            (H.Pos(2, 2), H.P0), (H.Pos(2, 3), H.P0), (H.Pos(2, 4), H.P0), (H.Pos(2, 5), H.P0),
            (H.Pos(3, 6), H.P0), (H.Pos(4, 6), H.P0), (H.Pos(5, 6), H.P0), (H.Pos(6, 6), H.P0));

        var windows = Detect(board, new BoardPosition(2, 6), H.P0);

        Assert.Equal(2, windows.Count);
        AssertSequence(windows, new[] { H.Pos(2, 2), H.Pos(2, 3), H.Pos(2, 4), H.Pos(2, 5), H.Pos(2, 6) });
        AssertSequence(windows, new[] { H.Pos(2, 6), H.Pos(3, 6), H.Pos(4, 6), H.Pos(5, 6), H.Pos(6, 6) });
    }

    [Fact]
    public void Sequences_Of_Opponent_Are_Not_Counted()
    {
        BoardState board = H.BoardWith(
            (H.Pos(2, 2), H.P1), (H.Pos(2, 3), H.P1), (H.Pos(2, 4), H.P1), (H.Pos(2, 5), H.P1),
            (H.Pos(2, 6), H.P0));

        var windows = Detect(board, new BoardPosition(2, 6), H.P0);

        Assert.Empty(windows);
    }
}