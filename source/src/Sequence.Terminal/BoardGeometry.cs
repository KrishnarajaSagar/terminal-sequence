namespace Sequence.Terminal;

/// <summary>
/// The screen layout of the terminal board, computed from the current window size.
/// Layout rules mirror <see cref="TerminalRenderer"/> exactly: a wide terminal gets a
/// two-pane frame (board left, hand/commands right), a narrow one falls back to the
/// stacked frame.
/// </summary>
public sealed record BoardGeometry(
    bool IsStacked,
    int CellWidth,
    int CellRows,
    int BoardWidth,
    int PanelWidth,
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

        int finalBoardWidth = 13 + BoardRows * cellWidth;
        return new BoardGeometry(
            IsStacked: stacked,
            CellWidth: cellWidth,
            CellRows: cellRows,
            BoardWidth: finalBoardWidth,
            PanelWidth: width - finalBoardWidth - 2,
            Width: width,
            Height: height);
    }
}