namespace Sequence.Terminal;

public enum UserIntent
{
    Play,
    Exchange,
    Help,
    Quit,
}

public enum StepIntent
{
    SelectCard,
    ExchangeCard,
    Help,
    Quit,
}

/// <summary>Result of the first ("pick a card") prompt. Card numbers are 1-based.</summary>
public sealed record Step(StepIntent Intent, int CardIndex);

/// <summary>Result of the second ("choose a space") prompt. Coordinates are 1-based.</summary>
public sealed record TargetChoice(bool IsBack, int Row, int Col)
{
    public static TargetChoice Back { get; } = new(true, 0, 0);
}

/// <summary>Result of a numbered-choice menu. Index is 1-based.</summary>
public sealed record OptionChoice(bool IsBack, int Index)
{
    public static OptionChoice Back { get; } = new(true, 0);
}

public static class TerminalInput
{
    private const int BoardSize = 10;
    private const int MinBoardCoordinate = 1;

    /// <summary>Parses the card-selection step: "1..7", "exchange [n]", "help", "quit".</summary>
    public static bool TryParseStep(string? line, int handSize, out Step step, out string error)
    {
        step = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "Type a card number 1-7, or 'exchange', 'help', 'quit'.";
            return false;
        }

        string[] tokens = line.Trim().Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        string head = tokens[0].ToLowerInvariant();

        switch (head)
        {
            case "quit":
            case "q":
            case "exit":
                step = new Step(StepIntent.Quit, 0);
                return true;

            case "help":
            case "h":
            case "?":
                step = new Step(StepIntent.Help, 0);
                return true;

            case "exchange":
            case "ex":
            {
                if (tokens.Length < 2 || !int.TryParse(tokens[1], out int cardIndex))
                {
                    error = "Usage: exchange [n]     e.g. 'exchange 3'";
                    return false;
                }

                return Validity.Of(cardIndex, handSize, new Step(StepIntent.ExchangeCard, cardIndex), out step, out error);
            }

            default:
            {
                if (!int.TryParse(head, out int cardIndex))
                {
                    error = $"Unknown command '{line.Trim()}'. Type 'help' for the list of commands.";
                    return false;
                }

                return Validity.Of(cardIndex, handSize, new Step(StepIntent.SelectCard, cardIndex), out step, out error);
            }
        }
    }

    /// <summary>Parses the space-selection step: "row,col" (1-10) or "back".</summary>
    public static bool TryParseTarget(string? line, out TargetChoice choice, out string error)
    {
        choice = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "Enter row,column, e.g. '4,7', or 'back'.";
            return false;
        }

        string[] tokens = line.Trim().Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1 && tokens[0].Equals("back", StringComparison.OrdinalIgnoreCase) ||
            tokens.Length == 1 && tokens[0].Equals("b", StringComparison.OrdinalIgnoreCase))
        {
            choice = TargetChoice.Back;
            return true;
        }

        if (tokens.Length < 2 ||
            !int.TryParse(tokens[0], out int row) ||
            !int.TryParse(tokens[1], out int col))
        {
            error = "Usage: <row>,<col>     e.g. '4,7'";
            return false;
        }

        if (row is < MinBoardCoordinate or > BoardSize || col is < MinBoardCoordinate or > BoardSize)
        {
            error = $"Row and column must be between {MinBoardCoordinate} and {BoardSize}.";
            return false;
        }

        choice = new TargetChoice(false, row, col);
        return true;
    }

    /// <summary>Parses a numbered-position choice: "1..n" or "back".</summary>
    public static bool TryParseOptionPick(string? line, int optionCount, out OptionChoice choice, out string error)
    {
        choice = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = $"Pick 1-{optionCount}, or 'back'.";
            return false;
        }

        string trimmed = line.Trim();
        if (trimmed.Equals("back", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("b", StringComparison.OrdinalIgnoreCase))
        {
            choice = OptionChoice.Back;
            return true;
        }

        if (!int.TryParse(trimmed, out int pick))
        {
            error = $"Pick 1-{optionCount}, or 'back'.";
            return false;
        }

        if (pick < 1 || pick > optionCount)
        {
            error = $"That choice is out of range (1-{optionCount}).";
            return false;
        }

        choice = new OptionChoice(false, pick);
        return true;
    }

    private static class Validity
    {
        public static bool Of(int cardIndex, int handSize, Step built, out Step step, out string error)
        {
            step = built;
            error = string.Empty;

            if (cardIndex < 1)
            {
                error = "Card numbers start at 1.";
                return false;
            }

            if (cardIndex > handSize)
            {
                error = $"You only have {handSize} cards in hand.";
                return false;
            }

            return true;
        }
    }
}