using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>What the player chose on the opening screen.</summary>
public enum MainMenuChoice
{
    Host,
    Join,
    Exit,
}

/// <summary>
/// The opening screen. Keyboard-driven: ↑/↓ (or 1-3) picks an option, Enter confirms.
/// No typed commands are required. Rendering goes through the injected console so tests
/// can capture it; input goes through <see cref="IMenuInput"/> so tests can script keys.
/// </summary>
public static class MainMenu
{
    private static readonly (string Label, MainMenuChoice Choice)[] Options =
    {
        ("Host Game", MainMenuChoice.Host),
        ("Join Game", MainMenuChoice.Join),
        ("Exit", MainMenuChoice.Exit),
    };

    public static MainMenuChoice Show(IMenuInput input, IAnsiConsole console)
    {
        int selected = 0;

        while (true)
        {
            Render(console, selected);

            ConsoleKeyInfo key = input.ReadKey();
            if (MenuNavigation.Move(key, Options.Length, ref selected))
            {
                continue;
            }

            if (MenuNavigation.SelectNumber(key, Options.Length, out int index))
            {
                selected = index;
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                return Options[selected].Choice;
            }
        }
    }

    private static void Render(IAnsiConsole console, int selected)
    {
        console.Clear();
        MenuUi.RenderTitle(console, "SEQUENCE", "a two-player card game");
        console.MarkupLine("");

        for (int i = 0; i < Options.Length; i++)
        {
            bool active = i == selected;
            string marker = active ? "►" : " ";
            string label = $"{i + 1}. {Options[i].Label}";

            if (active)
            {
                console.MarkupLine($"{marker} [white on blue] {Markup.Escape(label)} [/]");
            }
            else
            {
                console.MarkupLine($"{marker}   {Markup.Escape(label)}");
            }
        }

        console.MarkupLine("");
        console.MarkupLine("[dim]↑/↓ or 1-3 select · Enter confirm[/]");
    }
}