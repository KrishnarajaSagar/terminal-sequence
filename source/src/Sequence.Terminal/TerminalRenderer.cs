using System.Text;
using Sequence.Domain;
using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>
/// Full-screen renderer. Draws the whole frame every turn: header with statuses, the
/// 10x10 board on the left with card faces kept visible under player chip indicators,
/// and (on wide terminals) the player's hand plus commands in a right-hand panel.
/// Renders only from a <see cref="PlayerView"/>; it never mutates game state.
/// </summary>
public static class TerminalRenderer
{
    private const int BoardRows = 10;
    private const int HandTileWidth = 5;

    public static void RenderFullScreen(
        PlayerView view,
        int sequenceTarget,
        string? footer = null,
        BoardPosition? hovered = null,
        PlayerColor hoverColor = default,
        int? hoveredHand = null)
    {
        (int width, int height) = WindowSize();

        Clear();
        RenderHeader(view, sequenceTarget);
        BlankLine();

        BoardGeometry geometry = BoardGeometry.Compute(width, height);
        if (geometry.IsStacked)
        {
            RenderStacked(view, geometry, footer, hovered, hoverColor, hoveredHand);
            return;
        }

        // Two-pane layout: board on the left, hand/commands on the right.
        const int gutter = 2;
        int cellWidth = geometry.CellWidth;
        int cellRows = geometry.CellRows;
        int boardWidth = geometry.BoardWidth;
        int rightPanelWidth = geometry.PanelWidth;

        var left = new List<string>();
        BuildBoard(left, view, cellWidth, cellRows, hovered, hoverColor);

        var right = new List<string>();
        BuildRightPanel(right, view, sequenceTarget);
        int shift = view.HasExchangedDeadCardThisTurn ? 1 : 0;

        int totalRows = Math.Max(left.Count, right.Count);
        for (int i = 0; i < totalRows; i++)
        {
            string leftLine = i < left.Count ? left[i] : string.Empty;
            string rightLine = i < right.Count ? TruncatePlain(right[i], rightPanelWidth) : string.Empty;

            // Tint the hand card the pointer is over (hand tiles start at panel index 6+shift).
            if (hoveredHand is int hand && rightLine.Length > 0 && i - 6 - shift == hand)
            {
                rightLine = $"[on {ColorName(hoverColor)}]{rightLine}[/]";
            }

            MarkupLine(PadPlain(leftLine, boardWidth + gutter) + rightLine);
        }

        BlankLine();
        RenderPrompt(view, footer);
    }

    private static void RenderStacked(
        PlayerView view,
        BoardGeometry geometry,
        string? footer,
        BoardPosition? hovered,
        PlayerColor hoverColor,
        int? hoveredHand)
    {
        // Cramped terminal: fall back to the stacked layout (board, then hand).
        var boardLines = new List<string>();
        BuildBoard(boardLines, view, geometry.CellWidth, geometry.CellRows, hovered, hoverColor);
        foreach (string line in boardLines)
        {
            MarkupLine(line);
        }

        BlankLine();
        BuildHandBelow(view, geometry, hoveredHand, hoverColor);
        BlankLine();
        RenderPrompt(view, footer);
    }

    // ------------------------------------------------------------------ header

    private static void RenderHeader(PlayerView view, int sequenceTarget)
    {
        PlayerSummary you = view.Viewer;
        PlayerSummary opponent = view.Players.Single(p => p.Id != view.ViewerId);

        string turn = view.IsYourTurn ? "YOU" : opponent.Name;
        string status = view.Status == GameStatus.Won ? "Game Over" : "Playing";

        // Line 1: game label and identity.
        MarkupLine($"[bold]SEQUENCE[/]");
        MarkupLine($"Sequences: {you.SequenceCount} / {sequenceTarget}   [bold]{ColoredPlayer(you)}[/]  |  " +
                   $"Opponent: {Markup.Escape(opponent.Name)}  |  Turn: {Markup.Escape(turn)}  |  Status: {status}");
        MarkupLine($"[dim]Draw pile: {view.CardsRemainingInDrawPile}  |  Discard pile: {view.DiscardPile.Count}" +
                   $"{(view.HasExchangedDeadCardThisTurn ? "  |  (already exchanged this turn)" : string.Empty)}[/]");
    }

    // ------------------------------------------------------------------ board

    private static void BuildBoard(List<string> lines, PlayerView view, int cellWidth, int cellRows, BoardPosition? hovered, PlayerColor hoverColor)
    {
        string gutter = new(' ', 2);
        var colLabel = new StringBuilder();
        colLabel.Append(gutter);
        for (int col = 0; col < BoardRows; col++)
        {
            colLabel.Append(Center((col + 1).ToString(), cellWidth + 1));
        }

        colLabel.Append(' ');
        lines.Add(Markup.Escape(colLabel.ToString()));

        lines.Add(Markup.Escape(HorizontalBorder(cellWidth, '┌', '┬', '┐')));

        for (int row = 0; row < BoardRows; row++)
        {
            string[] rowLines = new string[cellRows];

            for (int col = 0; col < BoardRows; col++)
            {
                string[] block = BoardCell(view, row, col, cellWidth, cellRows, hovered, hoverColor);
                string gap = col < BoardRows - 1 ? " " : string.Empty;
                for (int line = 0; line < cellRows; line++)
                {
                    rowLines[line] += block[line] + gap;
                }
            }

            for (int line = 0; line < cellRows; line++)
            {
                string prefix = line == cellRows - 1 ? $"{(row + 1):D2}" : "  ";
                lines.Add(Markup.Escape(prefix) + "│" + rowLines[line] + "│");
            }

            if (row < BoardRows - 1)
            {
                lines.Add(Markup.Escape(HorizontalBorder(cellWidth, '├', '┼', '┤')));
            }
        }

        lines.Add(Markup.Escape(HorizontalBorder(cellWidth, '└', '┴', '┘')));
    }

    private static string[] BoardCell(PlayerView view, int row, int col, int cellWidth, int cellRows, BoardPosition? hovered, PlayerColor hoverColor)
    {
        var pos = new BoardPosition(row, col);
        bool locked = view.Board.IsLocked(pos);
        Card? card = BoardLayout.Standard.GetCardAt(pos);
        PlayerId? owner = view.Board.ChipsAt(pos);
        bool hasChip = owner is not null;
        bool isHovered = hovered is { } hoverPosition && hoverPosition == pos;
        string background = locked ? "[on gold1]" : isHovered ? $"[on {ColorName(hoverColor)}]" : string.Empty;
        string reset = background.Length == 0 ? string.Empty : "[/]";

        string[] lines = new string[cellRows];
        for (int i = 0; i < cellRows; i++)
        {
            lines[i] = new string(' ', cellWidth);
        }

        int contentRow = (cellRows - 1) / 2;

        if (BoardLayout.Standard.IsFreeCorner(pos))
        {
            lines[contentRow] = CenterText("[gold1 bold]★[/]", cellWidth, cellWidth);
            return Wrap(lines, background, reset);
        }

        string faceColor = SuitColorMarkup(card);

        if (cellRows >= 2)
        {
            string faceText = Markup.Escape(CardFace(card));
            if (!hasChip)
            {
                lines[contentRow] = CenterText($"{faceColor}{faceText}[/]", cellWidth, cellWidth);
                return Wrap(lines, background, reset);
            }

            string group = $"{faceColor}{faceText}[/]  {ChipMarkup(view, owner!.Value)}";
            if (Markup.Remove(group).Length <= cellWidth)
            {
                lines[contentRow] = CenterText(group, cellWidth, cellWidth);
                return Wrap(lines, background, reset);
            }

            // Too wide (small cells): keep the face and chip packed side-by-side.
            int faceBudget = Math.Max(2, cellWidth - 3);
            string compact = PadPlain($"{faceColor}{PackLeft(CardFace(card), faceBudget)}[/]", faceBudget) + "  " + ChipMarkup(view, owner!.Value);
            lines[contentRow] = compact;
            return Wrap(lines, background, reset);
        }

        // Narrow fallback: card face left, chip marker right on the same line.
        int codeWidth = Math.Max(3, cellWidth - 2);
        string facePart = $"{faceColor}{PackLeft(CardFace(card), codeWidth)}[/]";
        string chipPart = hasChip ? PadPlain(ChipMarkup(view, owner!.Value), cellWidth - codeWidth) : new string(' ', cellWidth - codeWidth);
        lines[0] = facePart + chipPart;
        return Wrap(lines, background, reset);
    }

    private static string PadPlain(string markup, int width)
    {
        int pad = width - Markup.Remove(markup).Length;
        return markup + new string(' ', Math.Max(0, pad));
    }

    private static string TruncatePlain(string markup, int width)
    {
        var sb = new StringBuilder();
        var tags = new Stack<string>();
        int plain = 0;
        int i = 0;
        while (i < markup.Length && plain < width)
        {
            if (markup[i] == '[')
            {
                int close = markup.IndexOf(']', i);
                if (close < 0)
                {
                    sb.Append(markup[i]);
                    i++;
                    continue;
                }

                string inner = markup.Substring(i, close - i + 1);
                string tag = markup.Substring(i + 1, close - i - 1).Trim();
                if (tag == "/")
                {
                    if (tags.Count > 0)
                    {
                        tags.Pop();
                    }

                    sb.Append(inner);
                }
                else if (tag.Length > 0 && char.IsLetter(tag[0]))
                {
                    tags.Push(tag);
                    sb.Append(inner);
                }
                else
                {
                    sb.Append(inner);
                }

                i = close + 1;
                continue;
            }

            sb.Append(markup[i]);
            plain++;
            i++;
        }

        string result = sb.ToString();
        int lastOpen = result.LastIndexOf('[');
        if (lastOpen >= 0 && result.LastIndexOf(']') < lastOpen)
        {
            result = result[..lastOpen];
        }

        while (tags.Count > 0)
        {
            _ = tags.Pop();
            result += "[/]";
        }

        return result;
    }

    private static string[] Wrap(string[] lines, string background, string reset) =>
        background.Length == 0 ? lines : lines.Select(l => $"{background}{l}{reset}").ToArray();

    private static string PackLeft(string text, int width)
    {
        string inner = Markup.Escape(text);
        return inner.Length >= width ? inner[..width] : inner + new string(' ', width - inner.Length);
    }

    private static string CenterText(string content, int textWidth, int cellWidth)
    {
        string plain = Markup.Remove(content);
        int pad = cellWidth - plain.Length;
        int left = pad / 2;
        int right = pad - left;
        return new string(' ', left) + content + new string(' ', right);
    }

    // ------------------------------------------------------------------ right panel

    private static void BuildRightPanel(List<string> lines, PlayerView view, int sequenceTarget)
    {
        PlayerSummary you = view.Viewer;
        PlayerSummary opponent = view.Players.Single(p => p.Id != view.ViewerId);

        lines.Add("[bold]STATUS[/]");
        lines.Add($"  {ColoredPlayer(you)}  {you.SequenceCount}/{sequenceTarget} seq");
        lines.Add($"  {ColoredPlayer(opponent)}  {opponent.SequenceCount}/{sequenceTarget} seq");
        lines.Add($"  Turn: {Markup.Escape(view.IsYourTurn ? "YOU" : opponent.Name)}");
        lines.Add($"  Draw {view.CardsRemainingInDrawPile}  |  Disc {view.DiscardPile.Count}");
        if (view.HasExchangedDeadCardThisTurn)
        {
            lines.Add("[dim]  exchanged this turn[/]");
        }

        lines.Add(string.Empty);
        lines.Add("[bold]YOUR HAND[/]");
        if (view.Hand.Count == 0)
        {
            lines.Add("[dim]  (no cards)[/]");
        }
        else
        {
            for (int i = 0; i < view.Hand.Count; i++)
            {
                lines.Add(HandCardLine(view, i + 1, view.Hand[i]));
            }
        }

        lines.Add(string.Empty);
        lines.Add("[dim]COMMANDS[/]");
        lines.Add("  [dim]1-7 pick card[/]");
        lines.Add("  [dim]exchange [[n]][/]");
        lines.Add("  [dim]help  |  quit[/]");
    }

    private static string HandCardLine(PlayerView view, int index, Card card)
    {
        string label = Markup.Escape($"[{index}] ");
        string face = $"{SuitColorMarkup(card)}{Markup.Escape(CardFace(card)).PadRight(3)}[/]";

        if (card.IsTwoEyedJack || card.IsOneEyedJack)
        {
            string kind = card.IsTwoEyedJack ? "WILD2  (any space)" : "WILD1  (remove chip)";
            return $"{label}{face}  [gold1]{kind}[/]";
        }

        var positions = BoardLayout.Standard.GetPositionsForCard(card);
        bool anyFree = false;
        var posText = new StringBuilder();
        foreach (BoardPosition position in positions)
        {
            string cell = $"{position.Row + 1},{position.Col + 1}";
            if (view.Board.IsOccupied(position) || view.Board.IsLocked(position))
            {
                posText.Append($"[dim]{cell}[/] ");
            }
            else
            {
                anyFree = true;
                posText.Append($"{cell} ");
            }
        }

        string tag = anyFree ? "[green]PLAY[/]" : "[red]DEAD[/]";
        return $"{label}{face}  {posText}{tag}";
    }

    // ------------------------------------------------------------------ stacked hand (cramped terminals)

    private static void BuildHandBelow(PlayerView view, BoardGeometry geometry, int? hoveredHand, PlayerColor hoverColor)
    {
        MarkupLine("[bold]YOUR HAND[/]");
        if (view.Hand.Count == 0)
        {
            MarkupLine("[dim](no cards)[/]");
            return;
        }

        var lines = new[]
        {
            new StringBuilder(), // labels   [1] [2] ...
            new StringBuilder(), // tops    ┌─────┐
            new StringBuilder(), // middles │ 7♥  │
            new StringBuilder(), // bottoms └─────┘
            new StringBuilder(), // hints   PLAY/DEAD
        };

        for (int i = 0; i < view.Hand.Count; i++)
        {
            string[] block = HandTileBlock(view, i + 1, view.Hand[i], hoveredHand == i + 1, hoverColor);
            for (int line = 0; line < lines.Length; line++)
            {
                lines[line].Append(block[line]);
                lines[line].Append(' ');
            }
        }

        foreach (StringBuilder line in lines)
        {
            MarkupLine(line.ToString().TrimEnd());
        }
    }

    /// <summary>The five text rows that draw one hand tile (label, top, middle, bottom, hint).</summary>
    private static string[] HandTileBlock(PlayerView view, int handIndex, Card card, bool hovered, PlayerColor hoverColor)
    {
        int tile = HandTileWidth;
        int faceWidth = tile - 2;
        (string borderColor, string hint, string hintColor) = HandCardStyle(view, card);

        string[] rows =
        {
            Center(Markup.Escape($"[{handIndex}]"), tile),
            $"{borderColor}┌{Repeat('─', tile - 2)}┐[/]",
            $"{SuitColorMarkup(card)}│{Markup.Escape(CardFace(card)).PadRight(faceWidth)}│[/]",
            $"{borderColor}└{Repeat('─', tile - 2)}┘[/]",
            $"{hintColor}{Center(Markup.Escape(hint), tile)}[/]",
        };

        if (hovered)
        {
            string tint = $"[on {ColorName(hoverColor)}]";
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = tint + rows[i] + "[/]";
            }
        }

        return rows;
    }

    private static (string BorderColor, string Hint, string HintColor) HandCardStyle(PlayerView view, Card card)
    {
        if (card.IsTwoEyedJack || card.IsOneEyedJack)
        {
            return ("[gold1]", card.IsTwoEyedJack ? "WILD2" : "WILD1", "[gold1]");
        }

        bool dead = IsDeadCard(view, card);
        return dead ? ("[red dim]", "DEAD", "[red]") : ("[green]", "PLAY", "[green]");
    }

    // ------------------------------------------------------------------ mouse patches

    /// <summary>
    /// Repaints a single board cell in place (with or without the hover highlight).
    /// Used by the input pump so the pointer can light cells without redrawing the
    /// whole frame or disturbing the line the player is typing on.
    /// </summary>
    public static void PatchCell(PlayerView view, BoardPosition position, bool hovered, PlayerColor hoverColor, BoardGeometry geometry)
    {
        (int Left, int Top)? caret = SaveCaret();
        try
        {
            string[] block = BoardCell(
                view,
                position.Row,
                position.Col,
                geometry.CellWidth,
                geometry.CellRows,
                hovered ? position : null,
                hoverColor);

            int left = geometry.OriginCol + position.Col * (geometry.CellWidth + 1);
            int top = geometry.OriginRow + position.Row * (geometry.CellRows + 1);
            WriteBlock(block, left, top);
        }
        finally
        {
            RestoreCaret(caret);
        }
    }

    /// <summary>Repaints a single hand card (panel line or stacked tile) with the pointer state.</summary>
    public static void PatchHandCard(PlayerView view, int cardIndex, bool hovered, PlayerColor hoverColor, BoardGeometry geometry)
    {
        if (cardIndex < 1 || cardIndex > view.Hand.Count)
        {
            return;
        }

        (int Left, int Top)? caret = SaveCaret();
        try
        {
            if (geometry.IsStacked)
            {
                string[] block = HandTileBlock(view, cardIndex, view.Hand[cardIndex - 1], hovered, hoverColor);
                WriteBlock(block, (cardIndex - 1) * 6, geometry.HandTileFirstRow);
            }
            else
            {
                string line = HandCardLine(view, cardIndex, view.Hand[cardIndex - 1]);
                if (hovered)
                {
                    line = $"[on {ColorName(hoverColor)}]{line}[/]";
                }

                Console.SetCursorPosition(geometry.PanelCol, geometry.HandRow(cardIndex, view.HasExchangedDeadCardThisTurn));
                AnsiConsole.Markup(PadPlain(line, geometry.PanelWidth));
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
        {
            // terminal vanished or the cell scrolled off; the next full frame fixes it
        }
        finally
        {
            RestoreCaret(caret);
        }
    }

    private static void WriteBlock(string[] lines, int left, int top)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            try
            {
                Console.SetCursorPosition(left, top + i);
                AnsiConsole.Markup(lines[i]);
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
            {
                return;
            }
        }
    }

    private static (int Left, int Top)? SaveCaret()
    {
        try
        {
            (int left, int top) = Console.GetCursorPosition();
            return (left, top);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void RestoreCaret((int Left, int Top)? caret)
    {
        if (caret is not (int left, int top))
        {
            return;
        }

        try
        {
            Console.SetCursorPosition(left, top);
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
        {
            // the terminal closed mid-frame; nothing sensible to do
        }
    }

    // ------------------------------------------------------------------ prompt

    private static void RenderPrompt(PlayerView view, string? footer)
    {
        if (view.IsYourTurn)
        {
            MarkupLine("[bold]Choose an action:[/]");
            MarkupLine("  [dim]pick a card 1-7  |  exchange [[n]]  |  help  |  quit[/]");
        }
        else if (view.Status != GameStatus.Won)
        {
            PlayerSummary opponent = view.Players.Single(p => p.Id != view.ViewerId);
            MarkupLine($"[dim]Waiting for {ColoredPlayer(opponent)} to move...[/]");
        }

        if (footer is not null)
        {
            foreach (string line in footer.Split('\n'))
            {
                MarkupLine($"[red]{Markup.Escape(line)}[/]");
            }
        }

        Console.Write("> ");
    }

    public static void RenderWinner(PlayerView view)
    {
        PlayerSummary winner = view.Players.Single(p => p.Id == view.Winner);
        Clear();

        var panel = new Panel(Align.Center(new Markup($"[bold]{ColoredPlayer(winner)}[/] wins!  " +
                                                      $"{Markup.Escape(winner.Name)} controls {winner.SequenceCount} sequence(s).")))
            .Header("GAME OVER")
            .BorderColor(Color.Gold1)
            .Border(BoxBorder.Heavy);
        AnsiConsole.Write(panel);
        Console.WriteLine();
        Console.WriteLine("Thanks for playing.");
    }

    // ------------------------------------------------------------------ shared helpers

    /// <summary>Spectre color name for a card's suit, for composing markup like "[bold red]".</summary>
    public static string SuitColorName(Card card) =>
        card.Suit is Suit.Hearts or Suit.Diamonds ? "red" : "white";

    /// <summary>Display face of a card, e.g. "6♣".</summary>
    public static string Face(Card card) => CardFace(card);

    private static string HorizontalBorder(int cellWidth, char left, char mid, char right)
    {
        var sb = new StringBuilder();
        sb.Append(new string(' ', 2)); // row-label gutter
        sb.Append(left);
        for (int i = 0; i < BoardRows; i++)
        {
            sb.Append(Repeat('─', cellWidth));
            if (i < BoardRows - 1)
            {
                sb.Append(mid);
            }
        }

        sb.Append(right);
        return sb.ToString();
    }

    private static string ColoredPlayer(PlayerSummary p) =>
        $"[{SpectreColor(p.Color)}]●[/] {Markup.Escape(p.Name)}";

    private static string ChipMarkup(PlayerView view, PlayerId owner)
    {
        PlayerColor color = view.Players.Single(p => p.Id == owner).Color;
        return $"[{SpectreColor(color)} bold]●[/]";
    }

    /// <summary>A card is dead when its two matching board spaces are both occupied.</summary>
    private static bool IsDeadCard(PlayerView view, Card card) =>
        !card.IsJack
        && view.Hand.Contains(card)
        && BoardLayout.Standard.GetPositionsForCard(card).All(position => view.Board.IsOccupied(position));

    private static string SuitColorMarkup(Card? card)
    {
        if (card is null)
        {
            return "[gold1]";
        }

        return card.Value.Suit is Suit.Hearts or Suit.Diamonds ? "[red]" : "[white]";
    }

    private static string CardFace(Card? card)
    {
        if (card is null)
        {
            return "  ";
        }

        return CardFace(card.Value);
    }

    private static string CardFace(Card card) => $"{RankText(card.Rank)}{SuitGlyph(card.Suit)}";

    private static string RankText(Rank rank) => rank switch
    {
        Rank.Ace => "A",
        Rank.Ten => "10",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        var r => ((int)r).ToString(),
    };

    private static char SuitGlyph(Suit suit) => suit switch
    {
        Suit.Clubs => '♣',
        Suit.Diamonds => '♦',
        Suit.Hearts => '♥',
        Suit.Spades => '♠',
        _ => '?',
    };

    private static string SpectreColor(PlayerColor color) => ColorName(color);

    /// <summary>Spectre.Console color name used to render a player's chip and name.</summary>
    public static string ColorName(PlayerColor color) => color switch
    {
        PlayerColor.Red => "red",
        PlayerColor.Green => "lime",
        PlayerColor.Blue => "deepskyblue1",
        PlayerColor.Yellow => "yellow",
        PlayerColor.Magenta => "magenta1",
        PlayerColor.Cyan => "aqua",
        _ => "white",
    };

    private static string Center(string text, int width)
    {
        if (text.Length >= width)
        {
            return text;
        }

        int left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - text.Length - left);
    }

    private static string Repeat(char c, int count) => new(c, count);

    private static void Clear()
    {
        if (!Console.IsOutputRedirected)
        {
            try
            {
                Console.Clear();
            }
            catch (IOException)
            {
                // Non-interactive console; continue.
            }
        }
    }

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

    private static void MarkupLine(string text) => AnsiConsole.MarkupLine(text);

    private static void BlankLine() => AnsiConsole.MarkupLine("");
}