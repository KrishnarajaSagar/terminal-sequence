using Sequence.Domain;

namespace Sequence.Tests;

public class BoardLayoutTests
{
    [Fact]
    public void Standard_Has_Exactly_Four_Free_Corners()
    {
        var free = new List<BoardPosition>();
        for (int row = 0; row < BoardLayout.Size; row++)
        {
            for (int col = 0; col < BoardLayout.Size; col++)
            {
                var position = new BoardPosition(row, col);
                if (BoardLayout.Standard.IsFree(position))
                {
                    free.Add(position);
                }
            }
        }

        Assert.Equal(4, free.Count);
        Assert.All(free, p => Assert.True(p.IsCorner));
    }

    [Fact]
    public void Standard_Every_NonJack_Card_Appears_Exactly_Twice()
    {
        var counts = new Dictionary<Card, int>();
        var jacks = new List<Card>();

        for (int row = 0; row < BoardLayout.Size; row++)
        {
            for (int col = 0; col < BoardLayout.Size; col++)
            {
                Card? card = BoardLayout.Standard.GetCardAt(new BoardPosition(row, col));
                if (card is null)
                {
                    continue;
                }

                if (card.Value.IsJack)
                {
                    jacks.Add(card.Value);
                }

                counts.TryGetValue(card.Value, out int n);
                counts[card.Value] = n + 1;
            }
        }

        Assert.Empty(jacks);
        Assert.Equal(48, counts.Count);
        Assert.All(counts, pair => Assert.Equal(2, pair.Value));
    }

    [Fact]
    public void Standard_Always_Returns_The_Same_Instance()
    {
        Assert.Same(BoardLayout.Standard, BoardLayout.Standard);
    }

    [Fact]
    public void GetPositionsForCard_Returns_Two_Matches()
    {
        foreach (Card card in new[] { Card.Parse("7H"), Card.Parse("QS"), Card.Parse("KD") })
        {
            var positions = BoardLayout.Standard.GetPositionsForCard(card);
            Assert.Equal(2, positions.Count);
            Assert.All(positions, p => Assert.Equal(card, BoardLayout.Standard.GetCardAt(p)));
        }
    }

    [Fact]
    public void GetCell_Outside_Board_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoardLayout.Standard.GetCell(new BoardPosition(10, 3)));
    }

    [Fact]
    public void Corners_Are_Never_Card_Positions()
    {
        foreach (BoardPosition corner in new[]
                 {
                     BoardPosition.FreeCornerTopLeft,
                     BoardPosition.FreeCornerTopRight,
                     BoardPosition.FreeCornerBottomLeft,
                     BoardPosition.FreeCornerBottomRight,
                 })
        {
            Assert.Null(BoardLayout.Standard.GetCardAt(corner));
            Assert.True(BoardLayout.Standard.IsFreeCorner(corner));
        }
    }
}