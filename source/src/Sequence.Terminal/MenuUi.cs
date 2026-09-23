using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>
/// Small pure-rendering helpers shared by the menu screens (title box, centering). Uses
/// only Spectre markup, which is already used by the game renderer.
/// </summary>
internal static class MenuUi
{
    /// <summary>Draws the centered SEQUENCE title in a box-drawing frame.</summary>
    public static void RenderTitle(IAnsiConsole console, string title, string? subtitle = null)
    {
        int content = Math.Max(title.Length, subtitle?.Length ?? 0) + 6;
        string bar = new string('═', content);

        console.MarkupLine($"[bold yellow]  ╔{bar}╗[/]");
        console.MarkupLine($"[bold yellow]  ║{Center(title, content)}║[/]");
        if (subtitle is not null)
        {
            console.MarkupLine($"[dim]  ║{Center(subtitle, content)}║[/]");
        }

        console.MarkupLine($"[bold yellow]  ╚{bar}╝[/]");
    }

    /// <summary>Centres plain text inside a fixed-width content area.</summary>
    public static string Center(string text, int width)
    {
        int left = (width - text.Length) / 2;
        int right = width - text.Length - left;
        return new string(' ', left) + text + new string(' ', right);
    }
}