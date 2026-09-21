using System.Net;

namespace Sequence.Terminal;

/// <summary>
/// Command-line configuration parsing for the terminal client and server. Pure and
/// dependency-free so the validation rules (port range, bind address, host shape) can be
/// unit-tested without touching a console or a socket.
/// </summary>
internal static class Args
{
    public const int DefaultPort = 5000;
    public const int DefaultConnectTimeoutSeconds = 10;

    /// <summary>Whether the exact flag (case-insensitive) appears anywhere in the arguments.</summary>
    public static bool HasFlag(string[] args, string flag) =>
        Array.Exists(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>The integer after <paramref name="flag"/>, or <paramref name="fallback"/> if absent or unparsable.</summary>
    public static int ArgInt(string[] args, string flag, int fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int value))
            {
                return value;
            }
        }

        return fallback;
    }

    /// <summary>The non-empty string after <paramref name="flag"/>, or <c>null</c> if absent or empty.</summary>
    public static string? ArgString(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(args[i + 1]))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// The <c>--port</c> value with range validation (1-65535). A present-but-invalid value
    /// is a hard configuration error instead of silently falling back to the default.
    /// </summary>
    public static int ReadPort(string[] args, int fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(args[i + 1], out int value) && value is >= 1 and <= 65535)
            {
                return value;
            }

            throw new ArgumentException($"Invalid port '{args[i + 1]}' - must be an integer between 1 and 65535.");
        }

        return fallback;
    }

    /// <summary>
    /// The <c>--bind</c> interface: <c>any</c>/<c>0.0.0.0</c> (default, all interfaces,
    /// needed for clients outside the local network), <c>loopback</c>/<c>localhost</c> for
    /// same-PC play only, or a specific adapter IP.
    /// </summary>
    public static IPAddress ReadBindAddress(string[] args)
    {
        string? value = ArgString(args, "--bind");
        if (value is null)
        {
            return IPAddress.Any;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "any" or "0.0.0.0" or "*" => IPAddress.Any,
            "loopback" or "localhost" or "127.0.0.1" => IPAddress.Loopback,
            "ipv6any" or "::" => IPAddress.IPv6Any,
            "ipv6loopback" or "::1" => IPAddress.IPv6Loopback,
            _ when IPAddress.TryParse(value, out IPAddress? ip) => ip!,
            _ => throw new ArgumentException(
                $"Invalid bind address '{value}' - use 'any'/'0.0.0.0' (all interfaces), 'loopback'/'127.0.0.1' (this PC only), or a specific adapter IP."),
        };
    }

    /// <summary>Rejects scheme/port-bearing or obviously malformed address strings up front.</summary>
    public static bool IsPlausibleHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.IndexOfAny([' ', '\t', '/']) >= 0)
        {
            return false;
        }

        return IPAddress.TryParse(host, out _) || !host.Contains(':');
    }
}