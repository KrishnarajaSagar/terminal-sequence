using Sequence.Contracts;
using Sequence.Domain;
using Spectre.Console;

namespace Sequence.Terminal;

public enum TurnCommandKind
{
    /// <summary>The player asked to leave the game.</summary>
    Quit,

    /// <summary>The player asked for help; <see cref="TurnCommand.Text"/> holds the help text.</summary>
    Help,

    /// <summary>Nothing was sent; <see cref="TurnCommand.Text"/> is a message to show as the footer.</summary>
    Footer,

    /// <summary>The player chose a move; <see cref="TurnCommand.Message"/> is ready to send.</summary>
    Submit,
}

/// <summary>One result of the interactive "pick a card, pick a space" prompting sequence.</summary>
public sealed record TurnCommand(TurnCommandKind Kind, string? Text, ClientMessage? Message)
{
    public static TurnCommand Quit() => new(TurnCommandKind.Quit, null, null);

    public static TurnCommand Help(string helpText) => new(TurnCommandKind.Help, helpText, null);

    public static TurnCommand Footer(string? footer) => new(TurnCommandKind.Footer, footer, null);

    public static TurnCommand Submit(ClientMessage message) => new(TurnCommandKind.Submit, null, message);
}

/// <summary>
/// Drives the turn-prompting flow shared by the on-device and networked clients. Given
/// the viewer's <see cref="PlayerView"/> it reads line input through an
/// <see cref="ITurnPump"/>, parses it through <see cref="TerminalInput"/>, and produces
/// a <see cref="ClientMessage"/> the caller routes to the authoritative server. Reading
/// prompt order and messages match the original hot-seat behaviour exactly.
/// </summary>
public static class ActionBuilder
{
    public static TurnCommand BuildTurn(PlayerView view, ITurnPump pump)
    {
        string? line = pump.Next(null);
        if (!TerminalInput.TryParseStep(line, view.Hand.Count, out Step step, out string stepError))
        {
            return TurnCommand.Footer(stepError);
        }

        switch (step.Intent)
        {
            case StepIntent.Quit:
                return TurnCommand.Quit();

            case StepIntent.Help:
                return TurnCommand.Help(HelpText());

            case StepIntent.ExchangeCard:
                return TurnCommand.Submit(new ExchangeDeadCardMessage(view.Hand[step.CardIndex - 1]));

            case StepIntent.SelectCard:
                return ResolvePlacement(view, pump, view.Hand[step.CardIndex - 1]);

            default:
                return TurnCommand.Footer("Unrecognised input; type 'help' for the list of commands.");
        }
    }

    private static TurnCommand ResolvePlacement(PlayerView view, ITurnPump pump, Card selected)
    {
        if (selected.IsJack)
        {
            return ResolveJackTarget(pump, selected);
        }

        // Normal card: show the 1-2 open board positions and let the player pick one.
        BoardPosition[] openSpots = BoardLayout.Standard.GetPositionsForCard(selected)
            .Where(p => !view.Board.IsOccupied(p) && !view.Board.IsLocked(p))
            .ToArray();

        if (openSpots.Length == 0)
        {
            int cardIndex = view.Hand.IndexOf(selected) + 1;
            return TurnCommand.Footer($"{selected.Code} is a dead card - both spaces are taken. Use 'exchange {cardIndex}'.");
        }

        AnsiConsole.MarkupLine($"  [bold {TerminalRenderer.SuitColorName(selected)}]{Markup.Escape(TerminalRenderer.Face(selected))}[/] can be played at:");
        for (int i = 0; i < openSpots.Length; i++)
        {
            BoardPosition spot = openSpots[i];
            AnsiConsole.MarkupLine($"    {Markup.Escape($"[{i + 1}]")} ({(spot.Row + 1):D2},{(spot.Col + 1):D2})");
        }

        const string prompt = "  pick a position (or 'back') > ";
        string? pickLine = pump.Next(prompt);
        if (!TerminalInput.TryParseOptionPick(pickLine, openSpots.Length, out OptionChoice pick, out string pickError))
        {
            return TurnCommand.Footer(pickError);
        }

        if (pick.IsBack)
        {
            return TurnCommand.Footer(null);
        }

        return TurnCommand.Submit(new PlayCardMessage(selected, openSpots[pick.Index - 1]));
    }

    private static TurnCommand ResolveJackTarget(ITurnPump pump, Card card)
    {
        string prompt = $"  {TargetPrompt(card)} (or 'back') > ";
        string? line = pump.Next(prompt);
        if (!TerminalInput.TryParseTarget(line, out TargetChoice choice, out string jackError))
        {
            return TurnCommand.Footer(jackError);
        }

        if (choice.IsBack)
        {
            return TurnCommand.Footer(null);
        }

        return SubmitJack(card, new BoardPosition(choice.Row - 1, choice.Col - 1));
    }

    private static TurnCommand SubmitJack(Card card, BoardPosition target) =>
        TurnCommand.Submit(card.IsTwoEyedJack
            ? new PlayTwoEyedJackMessage(card, target)
            : new PlayOneEyedJackMessage(card, target));

    private static string TargetPrompt(Card card)
    {
        if (card.IsTwoEyedJack)
        {
            return $"{card.Code} (two-eyed Jack): place on any empty space";
        }

        return $"{card.Code} (one-eyed Jack): remove a chip at";
    }

    private static string HelpText() =>
        string.Join('\n',
            "Commands:",
            "  1-7  pick a hand card; for a normal card choose one of its shown positions (1 or 2),",
            "       for a Jack type the row,column of the space to place/remove.",
            "  exchange [n]  discard a dead card and draw a new one (once per turn).",
            "  help  this text    |    quit  leave the game.",
            "Two-eyed Jacks place a chip anywhere. One-eyed Jacks remove an opponent chip.");
}