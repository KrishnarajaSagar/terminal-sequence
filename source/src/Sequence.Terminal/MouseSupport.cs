namespace Sequence.Terminal;

/// <summary>
/// Turns the console's xterm mouse tracking on and off. The sequences below ask for
/// SGR (extended) coordinates (<c>ESC[?1006h</c>) and for both button-event and
/// any-motion tracking (<c>ESC[?1002h</c>/<c>ESC[?1003h</c>) so the cursor can light up
/// cells while it moves and register clicks. Terminals that ignore 1003 still honour
/// 1002, and ones that ignore 1002 fall back to 1000.
/// </summary>
public static class MouseSupport
{
    // Want raw bytes written straight to the terminal: Spectre's markup layer must not
    // mangle these, so they bypass AnsiConsole entirely.
    private const string EnableSequence = "\x1b[?1000h\x1b[?1002h\x1b[?1003h\x1b[?1006h";
    private const string DisableSequence = "\x1b[?1000l\x1b[?1002l\x1b[?1003l\x1b[?1006l";

    private static bool _enabled;

    /// <summary>
    /// Mouse tracking is pointless (and can garble output) when the process reads from
    /// a pipe. Setting <c>SEQUENCE_NO_MOUSE=1</c> forces the classic keyboard flow on
    /// any terminal.
    /// </summary>
    public static bool IsAvailable =>
        !Console.IsInputRedirected
        && !Console.IsOutputRedirected
        && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEQUENCE_NO_MOUSE"));

    /// <summary>Enables mouse tracking if the console can support it (idempotent).</summary>
    public static void Enable()
    {
        if (!IsAvailable || _enabled)
        {
            return;
        }

        Console.Write(EnableSequence);
        _enabled = true;
    }

    /// <summary>Disables mouse tracking, restoring normal terminal behaviour.</summary>
    public static void Disable()
    {
        if (!_enabled)
        {
            return;
        }

        Console.Write(DisableSequence);
        _enabled = false;
    }
}