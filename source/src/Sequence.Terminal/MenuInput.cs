namespace Sequence.Terminal;

/// <summary>
/// Source of the single keystrokes the menu screens consume. Kept behind an interface so
/// the keyboard-driven navigation can be unit-tested with scripted keys instead of a live
/// console. The game screen keeps using <see cref="ITurnPump"/> (line-based input).
/// </summary>
public interface IMenuInput
{
    /// <summary>Reads one key without echoing it to the console.</summary>
    ConsoleKeyInfo ReadKey();
}

/// <summary>Creates the production key reader for interactive consoles.</summary>
public static class MenuInput
{
    public static IMenuInput Create() => new ConsoleMenuInput();
}

/// <summary>
/// Interactive key reader backed by <see cref="Console.ReadKey"/>. Only used when the
/// input stream is not redirected (piped input keeps the scriptable flag/hot-seat path).
/// </summary>
internal sealed class ConsoleMenuInput : IMenuInput
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);
}