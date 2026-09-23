using System.Net;
using Sequence.Server;
using Spectre.Console;

namespace Sequence.Terminal;

/// <summary>Everything a "Host Game" menu needs to hand to the server runner.</summary>
public readonly record struct HostSettings(int Port, IPAddress Bind, int Seed, string SaveName);

/// <summary>
/// The "Host Game" screen. Every field starts prefilled with the same defaults the
/// --server flags use, so pressing Enter through the form starts a server with the
/// established behaviour (port 5000, all interfaces, random deck seed, game-001 save).
/// </summary>
public static class HostForm
{
    public static HostSettings? Show(IMenuInput input, IAnsiConsole console)
    {
        var fields = new[]
        {
            new MutableField { Label = "Port", Value = Args.DefaultPort.ToString(), Placeholder = "1-65535", Numeric = true },
            new MutableField { Label = "Bind", Value = "any", Placeholder = "any / loopback / 127.0.0.1" },
            new MutableField { Label = "Seed", Value = string.Empty, Placeholder = "random" },
            new MutableField { Label = "Save", Value = GameStateStore.DefaultSaveName, Placeholder = GameStateStore.DefaultSaveName },
        };

        IReadOnlyList<MutableField>? result = FormScreen.Show(
            input, console, "Host Game", "start a server other players join", fields, "Start Hosting", Validate);

        if (result is null)
        {
            return null;
        }

        int port = int.Parse(result[0].Value);
        IPAddress bind = Args.ParseBindAddress(result[1].Value.Trim());
        int seed = result[2].Value.Length == 0 ? Random.Shared.Next() : int.Parse(result[2].Value);
        string saveName = result[3].Value.Trim().Length == 0
            ? GameStateStore.DefaultSaveName
            : result[3].Value.Trim();

        return new HostSettings(port, bind, seed, saveName);
    }

    internal static string? Validate(IReadOnlyList<MutableField> fields)
    {
        if (!int.TryParse(fields[0].Value, out int port) || port is < 1 or > 65535)
        {
            return $"Invalid port '{fields[0].Value}' - must be an integer between 1 and 65535.";
        }

        try
        {
            _ = Args.ParseBindAddress(fields[1].Value.Trim());
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        if (fields[2].Value.Length > 0 && !int.TryParse(fields[2].Value, out _))
        {
            return $"Invalid seed '{fields[2].Value}' - must be a whole number or left empty for a random deck.";
        }

        return null;
    }
}