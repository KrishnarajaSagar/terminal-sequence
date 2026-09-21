using Sequence.Domain;

namespace Sequence.Terminal;

/// <summary>
/// The screen layout of the terminal board, computed from the current window size.
/// Layout rules mirror <see cref="TerminalRenderer"/> exactly: a wide terminal gets a
/// two-pane frame (board left, hand/commands right), a narrow one falls back to the
/// stacked frame. This geometry is shared by the renderer (drawing) and the mouse
/// hit-testing code, so a pointer position maps to the same board cell the renderer
/// drew there.
/// </summary>
public sealed record BoardGeometry(
    bool IsStacked,
    int CellWidth,
    int CellRows,
    int OriginRow,
    int OriginCol,
    int BoardWidth,
    int PanelCol,
    int PanelWidth,
    int HandFirstRow,
    int HandTileFirstRow,
    int Width,
    int Height)
{
    private const int BoardRows = 10;

    /// <summary>
    /// Geometry for a <c>width</c>x<c>height</c> console, using the fallback size the
    /// renderer uses when the console reports none.
    /// </summary>
    public static BoardGeometry Compute(int width, int height)
    {
        int cellWidth = Math.Clamp((width - 48) / BoardRows, 3, 8);
        int boardWidth = 13 + BoardRows * cellWidth;
        bool stacked = width < 74 || width - boardWidth - 2 < 24;

        int cellRows;
        if (stacked)
        {
            cellWidth = width >= 94 ? 7 : width >= 80 ? 5 : 3;
            cellRows = height >= 55 ? 3 : height >= 45 ? 2 : 1;
        }
        else
        {
            int idealRows = Math.Max(1, (cellWidth + 1) / 2);
            cellRows = Math.Clamp((height - 19) / BoardRows, 1, idealRows);
        }

        // The frame starts with the 3-line header and one blank line, then either the
        // column labels / top border. Cell (0,0)'s first content line therefore sits on
        // 0-based screen row 6 and its first content character on 0-based column 3.
        const int originRow = 6;
        const int originCol = 3;

        int finalBoardWidth = 13 + BoardRows * cellWidth;
        return new BoardGeometry(
            IsStacked: stacked,
            CellWidth: cellWidth,
            CellRows: cellRows,
            OriginRow: originRow,
            OriginCol: originCol,
            BoardWidth: finalBoardWidth,
            PanelCol: finalBoardWidth + 2,
            PanelWidth: width - finalBoardWidth - 2,
            HandFirstRow: 11,        // 0-based screen row of panel card [1] in the two-pane frame
            HandTileFirstRow: 17 + BoardRows * cellRows, // 0-based row of the stacked hand label
            Width: width,
            Height: height);
    }

    /// <summary>Geometry for the live console window (falling back if it cannot be read).</summary>
    public static BoardGeometry ComputeCurrent()
    {
        (int width, int height) = WindowSize();
        return Compute(width, height);
    }

    /// <summary>
    /// Maps a 0-based screen position onto a board cell. Returns false when the pointer
    /// is over a border, a column gap, or outside the board entirely.
    /// </summary>
    public bool TryHitTestBoard(int row, int col, out BoardPosition position)
    {
        position = default;
        int relativeRow = row - OriginRow;
        if (relativeRow < 0)
        {
            return false;
        }

        int boardRow = relativeRow / (CellRows + 1);
        int lineInCell = relativeRow % (CellRows + 1);
        if (boardRow >= BoardRows || lineInCell >= CellRows)
        {
            return false;
        }

        int relativeCol = col - OriginCol;
        if (relativeCol < 0)
        {
            return false;
        }

        int boardCol = relativeCol / (CellWidth + 1);
        int colInCell = relativeCol % (CellWidth + 1);
        if (boardCol >= BoardRows || colInCell >= CellWidth)
        {
            return false;
        }

        position = new BoardPosition(boardRow, boardCol);
        return true;
    }

    /// <summary>
    /// Maps a 0-based screen position onto a hand card. The two-pane frame treats the
    /// whole panel card row as clickable; the stacked frame uses the 5-line tile box.
    /// Hand indices are 1-based.
    /// </summary>
    public bool TryHitTestHand(int row, int col, int handCount, bool hasExchanged, out int handIndex)
    {
        handIndex = 0;

        if (IsStacked)
        {
            int groupRow = row - HandTileFirstRow;
            if (groupRow < 0 || groupRow >= 5 || row >= Height)
            {
                return false;
            }

            int tileCol = col % 6;
            int tileIndex = col / 6;
            if (tileIndex >= handCount || tileCol >= 5)
            {
                return false;
            }

            handIndex = tileIndex + 1;
            return true;
        }

        int shift = hasExchanged ? 1 : 0;
        int cardRow = HandFirstRow + shift;
        int local = row - cardRow;
        if (row >= Height || local < 0 || local >= handCount)
        {
            return false;
        }

        if (col < PanelCol || col >= PanelCol + PanelWidth)
        {
            return false;
        }

        handIndex = local + 1;
        return true;
    }

    /// <summary>The 0-based screen row of a given hand card in the two-pane frame.</summary>
    public int HandRow(int cardIndex, bool hasExchanged) =>
        HandFirstRow + (hasExchanged ? 1 : 0) + cardIndex - 1;

    private static (int Width, int Height) WindowSize()
    {
        try
        {
            return (Console.WindowWidth, Console.WindowHeight);
        }
        catch (IOException)
        {
            return (100, 36);
        }
    }
}