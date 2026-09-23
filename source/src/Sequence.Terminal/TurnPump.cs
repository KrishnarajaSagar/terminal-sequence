namespace Sequence.Terminal;

/// <summary>
/// Source of the line input the prompting flow consumes. Keyboard-only: each call to
/// <see cref="Next"/> prints a prompt (when one is supplied) and blocks until a line
/// is submitted.
/// </summary>
public interface ITurnPump : IDisposable
{
    /// <summary>
    /// Blocks for the next line of input. A non-null <paramref name="prompt"/> is
    /// printed before waiting; pass null for the phases that reuse the prompt already
    /// drawn by the renderer. Returns null on end-of-input.
    /// </summary>
    string? Next(string? prompt);
}

/// <summary>Creates the keyboard pump. Mouse input is not supported.</summary>
public static class TurnPump
{
    public static ITurnPump Create() => LineTurnPump.Instance;
}

/// <summary>
/// The keyboard path: blocks on <see cref="Console.ReadLine"/>. Used for both
/// interactive consoles and redirected/piped input (tests, smokes, CI).
/// </summary>
public sealed class LineTurnPump : ITurnPump
{
    public static readonly LineTurnPump Instance = new();

    private LineTurnPump()
    {
    }

    public string? Next(string? prompt)
    {
        if (prompt is not null)
        {
            Console.Write(prompt);
        }

        return Console.ReadLine();
    }

    public void Dispose()
    {
    }
}