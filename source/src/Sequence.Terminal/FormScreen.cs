using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>
/// One editable row on a form screen. Text rows accept typed characters (digits only when
/// <see cref="Numeric"/>); choice rows cycle through <see cref="Choices"/> with ↑/↓ or
/// digits.
/// </summary>
internal sealed class MutableField
{
    public required string Label { get; init; }

    public required string Value { get; set; }

    /// <summary>Dim hint shown while the field is empty.</summary>
    public required string Placeholder { get; init; }

    /// <summary>Restricts typing to digits (ports, seeds).</summary>
    public bool Numeric { get; init; }

    /// <summary>When set, the field is a pick-list instead of free text.</summary>
    public IReadOnlyList<string>? Choices { get; init; }
}

/// <summary>Pure string-editing rules for <see cref="MutableField"/> values.</summary>
internal static class FormEdit
{
    /// <summary>Appends a typed character; numeric fields ignore anything that is not a digit.</summary>
    public static string Append(string value, char c, bool numericOnly)
    {
        if (numericOnly && !char.IsAsciiDigit(c))
        {
            return value;
        }

        return value + c;
    }

    /// <summary>Removes the last character.</summary>
    public static string Backspace(string value) =>
        value.Length <= 1 ? string.Empty : value[..^1];
}

/// <summary>
/// Generic keyboard-driven form: a list of labelled fields followed by a confirm row.
/// ↑/↓ (or Tab) moves between rows, typing edits the active field, Enter confirms (moving
/// through the form or submitting when on the confirm row), Esc cancels. A validator runs
/// on submit and its message is shown in red until the form is fixed.
/// </summary>
internal static class FormScreen
{
    /// <summary>Returns the confirmed fields, or null when the player pressed Esc.</summary>
    public static IReadOnlyList<MutableField>? Show(
        IMenuInput input,
        IAnsiConsole console,
        string title,
        string? subtitle,
        IReadOnlyList<MutableField> fields,
        string confirmLabel,
        Func<IReadOnlyList<MutableField>, string?>? validate = null)
    {
        int selected = 0; // 0..fields.Count, where fields.Count is the confirm row.
        string? error = null;

        while (true)
        {
            Render(console, title, subtitle, fields, selected, confirmLabel, error);

            ConsoleKeyInfo key = input.ReadKey();
            if (key.Key == ConsoleKey.Escape)
            {
                return null;
            }

            if (selected == fields.Count)
            {
                if (key.Key == ConsoleKey.Enter)
                {
                    error = validate?.Invoke(fields);
                    if (error is null)
                    {
                        return fields;
                    }

                    continue;
                }

                MenuNavigation.Move(key, fields.Count + 1, ref selected);
                continue;
            }

            MutableField field = fields[selected];

            if (field.Choices is not null)
            {
                if (MenuNavigation.SelectNumber(key, field.Choices.Count, out int pick))
                {
                    field.Value = field.Choices[pick];
                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    selected++;
                    continue;
                }

                if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
                {
                    field.Value = Cycle(field.Choices, field.Value, forward: key.Key == ConsoleKey.DownArrow);
                    continue;
                }

                MenuNavigation.Move(key, fields.Count + 1, ref selected);
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                selected++;
                continue;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                field.Value = FormEdit.Backspace(field.Value);
                continue;
            }

            if (MenuNavigation.Move(key, fields.Count + 1, ref selected))
            {
                continue;
            }

            if (char.IsControl(key.KeyChar))
            {
                continue;
            }

            field.Value = FormEdit.Append(field.Value, key.KeyChar, field.Numeric);
        }
    }

    /// <summary>Previous/next entry in a choice list, wrapping at both ends.</summary>
    internal static string Cycle(IReadOnlyList<string> choices, string current, bool forward)
    {
        int index = -1;
        for (int i = 0; i < choices.Count; i++)
        {
            if (string.Equals(choices[i], current, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = forward ? -1 : choices.Count;
        }

        int next = index + (forward ? 1 : -1);
        if (next >= choices.Count)
        {
            next = 0;
        }
        else if (next < 0)
        {
            next = choices.Count - 1;
        }

        return choices[next];
    }

    private static void Render(
        IAnsiConsole console,
        string title,
        string? subtitle,
        IReadOnlyList<MutableField> fields,
        int selected,
        string confirmLabel,
        string? error)
    {
        console.Clear();
        MenuUi.RenderTitle(console, title, subtitle);
        console.MarkupLine("");

        for (int i = 0; i < fields.Count; i++)
        {
            RenderField(console, fields[i], i == selected);
        }

        bool confirmActive = selected == fields.Count;
        string marker = confirmActive ? "►" : " ";
        string confirm = confirmActive
            ? $"[black on green] {Markup.Escape(confirmLabel)} [/]"
            : $"[dim]{Markup.Escape(confirmLabel)}[/]";
        console.MarkupLine($"{marker} {confirm}");

        console.MarkupLine("");
        console.MarkupLine("[dim]↑/↓ move · type to edit · Enter confirm · Esc back[/]");

        if (error is not null)
        {
            foreach (string line in error.Split('\n'))
            {
                console.MarkupLine($"[red]{Markup.Escape(line)}[/]");
            }
        }
    }

    private static void RenderField(IAnsiConsole console, MutableField field, bool active)
    {
        string display = field.Value.Length == 0 ? field.Placeholder : field.Value;
        string label = Markup.Escape(field.Label);
        string value = active
            ? $"[white on blue] {Markup.Escape(display)} [/]"
            : $"[dim]{Markup.Escape(display)}[/]";
        string marker = active ? "►" : " ";

        console.MarkupLine(active
            ? $"{marker} [bold]{label}[/]  {value}"
            : $"{marker} {label}  {value}");
    }
}