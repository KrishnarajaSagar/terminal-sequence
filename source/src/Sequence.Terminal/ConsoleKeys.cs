using System.IO;

namespace Sequence.Terminal;

/// <summary>
/// Non-blocking key checks used by the non-prompting parts of the UI (the host lobby and
/// the connected lobby) so those loops can watch for a single Escape while they poll.
/// Mouse input is not supported anywhere.
/// </summary>
internal static class ConsoleKeys
{
    /// <summary>
    /// True when an Escape key is waiting on the console. Consumes exactly one console key
    /// when one is available; other keys are ignored. Always false for piped/redirected
    /// input, where there is no live keyboard (tests, smokes, CI keep the flag paths).
    /// </summary>
    public static bool EscapePressed()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            return false;
        }

        try
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            return false;
        }

        return Console.ReadKey(intercept: true).Key == ConsoleKey.Escape;
    }

    /// <summary>
    /// True when an Enter key is waiting on the console. Same non-blocking, input-redirected
    /// safe semantics as <see cref="EscapePressed"/>.
    /// </summary>
    public static bool EnterPressed()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            return false;
        }

        try
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            return false;
        }

        ConsoleKey key = Console.ReadKey(intercept: true).Key;
        return key == ConsoleKey.Enter;
    }
}