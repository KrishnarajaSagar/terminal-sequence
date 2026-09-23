namespace Sequence.Terminal;

/// <summary>
/// Pure keyboard-navigation rules shared by the menu screens. Extraction of every
/// decision makes the ↑/↓/number-key behaviour unit-testable without a live console.
/// </summary>
internal static class MenuNavigation
{
    /// <summary>
    /// Applies vertical navigation for a key press. Returns true when the key was handled
    /// as navigation (selection changed), false when the caller must handle it itself.
    /// </summary>
    public static bool Move(ConsoleKeyInfo key, int count, ref int selected)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                selected = selected <= 0 ? count - 1 : selected - 1;
                return true;

            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                selected = (selected + 1) % count;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Maps the digit pressed ('1'-'9') to a zero-based option index. Returns true when
    /// the digit names a valid option.
    /// </summary>
    public static bool SelectNumber(ConsoleKeyInfo key, int count, out int index)
    {
        index = -1;
        if (key.KeyChar is >= '1' and <= '9')
        {
            int candidate = key.KeyChar - '1';
            if (candidate < count)
            {
                index = candidate;
                return true;
            }
        }

        return false;
    }
}