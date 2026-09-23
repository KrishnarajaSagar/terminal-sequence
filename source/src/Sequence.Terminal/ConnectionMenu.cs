using Sequence.Domain;
using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>Everything a "Join Game" menu needs to hand to the client runner.</summary>
public readonly record struct ConnectionSettings(
    string Host,
    int Port,
    string PlayerId,
    PlayerColor? PreferredColor,
    int TimeoutSeconds);

/// <summary>
/// The "Join Game" screen. Defaults mirror the --host/--port/--player/--color flags
/// (localhost + the default port; player id and color editable, colour defaults to Auto
/// so the server assigns a free chip colour). Validate reuses the existing
/// <see cref="Args"/> and <see cref="Args.IsPlausibleHost"/> rules.
/// </summary>
public static class ConnectionMenu
{
    private static readonly string[] ColorChoices =
    {
        "Auto", "Green", "Blue", "Yellow", "Magenta", "Cyan",
    };

    public static ConnectionSettings? Show(IMenuInput input, IAnsiConsole console)
    {
        var fields = new[]
        {
            new MutableField { Label = "Server address", Value = "127.0.0.1", Placeholder = "host name or IP" },
            new MutableField { Label = "Port", Value = Args.DefaultPort.ToString(), Placeholder = "1-65535", Numeric = true },
            new MutableField { Label = "Player id", Value = string.Empty, Placeholder = "your id, shown to opponent" },
            new MutableField { Label = "Chip color", Value = "Auto", Placeholder = string.Empty, Choices = ColorChoices },
        };

        IReadOnlyList<MutableField>? result = FormScreen.Show(
            input, console, "Join Game", "connect to a running server", fields, "Connect", Validate);

        if (result is null)
        {
            return null;
        }

        string host = result[0].Value.Trim();
        int port = int.Parse(result[1].Value);
        string playerId = result[2].Value.Trim();
        string colorText = result[3].Value;

        PlayerColor? preferredColor = colorText == "Auto"
            ? null
            : Enum.Parse<PlayerColor>(colorText, ignoreCase: true);

        return new ConnectionSettings(
            host,
            port,
            playerId,
            preferredColor,
            Args.DefaultConnectTimeoutSeconds);
    }

    internal static string? Validate(IReadOnlyList<MutableField> fields)
    {
        string host = fields[0].Value.Trim();
        if (!Args.IsPlausibleHost(host))
        {
            return $"Invalid server host '{host}' - use an IP address or host name without a port or scheme.";
        }

        if (!int.TryParse(fields[1].Value, out int port) || port is < 1 or > 65535)
        {
            return $"Invalid port '{fields[1].Value}' - must be an integer between 1 and 65535.";
        }

        if (fields[2].Value.Trim().Length == 0)
        {
            return "Player id cannot be empty.";
        }

        return null;
    }
}