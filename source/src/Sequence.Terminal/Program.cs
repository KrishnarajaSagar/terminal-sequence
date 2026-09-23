using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Sequence.Domain;
using Sequence.Server;
using Spectre.Console;

namespace Sequence.Terminal;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            // Command-line dispatch is kept so tests, scripts, and remote sessions can
            // still drive the game non-interactively. The menus are the interactive path.
            if (Args.HasFlag(args, "--server"))
            {
                RunServer(args);
                return;
            }

            if (Args.HasFlag(args, "--client") || Args.HasFlag(args, "--net"))
            {
                RunClient(args);
                return;
            }

            if (Console.IsInputRedirected)
            {
                // Piped input has no arrow keys: keep the classic scriptable hot-seat path.
                RunLocalHotSeat(args);
                return;
            }

            RunMenuLoop(args);
        }
        catch (ArgumentException ex)
        {
            // Bad command-line configuration (port range, bind address, host name...).
            Console.Error.WriteLine(ex.Message);
        }
    }

    /// <summary>Local hot-seat game with into-line name/color prompts (piped input).</summary>
    private static void RunLocalHotSeat(string[] args)
    {
        // The first numeric argument is the deck seed.
        int seed = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : Random.Shared.Next();

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold yellow]S E Q U E N C E[/]");

        string firstName = PromptName(1);
        PlayerColor firstColor = PromptColor(1, null);

        string secondName = PromptName(2);
        PlayerColor secondColor = PromptColor(2, firstColor);

        LocalGameRunner.Run(new GameSetup(
            firstName,
            secondName,
            seed,
            FirstPlayerColor: firstColor,
            SecondPlayerColor: secondColor));
    }

    /// <summary>
    /// Interactive menu loop: main menu, then host/join forms that run the server or the
    /// client. Each game ending (or failing) returns here so the player can start another.
    /// </summary>
    private static void RunMenuLoop(string[] args)
    {
        _ = args; // reserved: future menu-bypass flags.

        AnsiConsole.Profile.Capabilities.Ansi = !Console.IsOutputRedirected;
        IAnsiConsole console = AnsiConsole.Console;
        IMenuInput input = MenuInput.Create();

        while (true)
        {
            switch (MainMenu.Show(input, console))
            {
                case MainMenuChoice.Exit:
                    return;

                case MainMenuChoice.Host:
                    HostSettings? host = HostForm.Show(input, console);
                    if (host is null)
                    {
                        continue;
                    }

                    RunServerCore(host.Value);
                    break;

                case MainMenuChoice.Join:
                    ConnectionSettings? join = ConnectionMenu.Show(input, console);
                    if (join is null)
                    {
                        continue;
                    }

                    RunClientCore(join.Value);
                    break;
            }
        }
    }

    private static void RunServer(string[] args)
    {
        int seed = Args.ArgInt(args, "--seed", Random.Shared.Next());
        int port = Args.ReadPort(args, Args.DefaultPort);
        string? resume = Args.ArgString(args, "--resume");
        string? save = Args.ArgString(args, "--save");
        IPAddress bind = Args.ReadBindAddress(args);

        RunServerCore(port, bind, seed, save ?? GameStateStore.DefaultSaveName, resume);
    }

    private static void RunServerCore(HostSettings settings) =>
        RunServerCore(settings.Port, settings.Bind, settings.Seed, settings.SaveName, resume: null);

    private static void RunServerCore(int port, IPAddress bind, int seed, string saveName, string? resume)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold yellow]S E Q U E N C E[/]");
        Console.WriteLine("Server mode. Two clients join this process:");
        Console.WriteLine($"  1. Start a client with '--client --host <this-pc-ip|public-ip> --port {port} --player your-id'.");
        Console.WriteLine($"  2. Bind interface: {DisplayAddress(bind)}.");
        Console.WriteLine($"Deck seed {seed}; each client requests its chip color on join (default: first green, second blue).");

        GameServer server;
        if (!string.IsNullOrWhiteSpace(resume))
        {
            GameState restored = GameStateStore.Load(resume);
            server = new GameServer(restored);
            Console.WriteLine($"Resumed '{GameStateStore.ResolvePath(resume)}' at turn {restored.TurnNumber}.");
        }
        else
        {
            server = new GameServer(new GameSetup(
                "Player 1",
                "Player 2",
                seed,
                FirstPlayerColor: PlayerColor.Green,
                SecondPlayerColor: PlayerColor.Blue));
            Console.WriteLine($"New game; will auto-save to '{GameStateStore.ResolvePath(saveName)}'.");
        }

        // Snapshot now and after every accepted action so a crash is always resumable.
        GameStateStore.Save(server.CurrentState, saveName);
        var host = new GameHost(server, port, bind, updated => GameStateStore.Save(updated, saveName));
        host.Start();

        PrintListeningInfo(host);

        if (Console.IsInputRedirected)
        {
            // Scripted/CI servers have no live keyboard: keep the classic Ctrl+C stop.
            Console.WriteLine("Press Ctrl+C to stop.");
            WaitForCtrlC();
        }
        else
        {
            RunHostLobby(host);
        }

        host.Stop();
        Console.WriteLine("Server stopped.");
    }

    /// <summary>
    /// Interactive host lobby: renders who is connected, polls the roster so a joined
    /// player appears without any new server events, and lets the host press Enter to
    /// begin the game once both seats are filled. Esc or Ctrl+C stops the server so the
    /// menu can take the host back to the main menu.
    /// </summary>
    private static void RunHostLobby(GameHost host)
    {
        var wait = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            wait.Set();
        };

        string bindText = DisplayAddress(host.BindAddress);
        string localAddresses = string.Join(", ", LocalIPv4Addresses());
        bool started = false;

        while (!wait.IsSet)
        {
            LobbyScreen.RenderHost(AnsiConsole.Console, bindText, host.Port, host.Roster, localAddresses, started);

            if (!started && host.Roster.Count(e => e.Connected) == 2 && ConsoleKeys.EnterPressed())
            {
                host.StartGame();
                started = true;
                continue;
            }

            if (ConsoleKeys.EscapePressed())
            {
                wait.Set();
                break;
            }

            wait.Wait(TimeSpan.FromMilliseconds(250));
        }
    }

    private static void WaitForCtrlC()
    {
        var wait = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            wait.Set();
        };

        wait.Wait();
    }

    private static void PrintListeningInfo(GameHost host)
    {
        if (host.BindAddress.Equals(IPAddress.Any) || host.BindAddress.Equals(IPAddress.IPv6Any))
        {
            Console.WriteLine($"Listening on {DisplayAddress(host.BindAddress)}:{host.Port} on all interfaces.");
            string local = string.Join(", ", LocalIPv4Addresses());
            Console.WriteLine(local.Length == 0
                ? "  (no IPv4 address found; check the network adapter)"
                : $"  Local address for same-network clients: {local}:{host.Port}");
            Console.WriteLine($"  Remote clients need port {host.Port} forwarded on the router to this PC, an inbound firewall rule,");
            Console.WriteLine("  and connect with '--client --host <public-ip> --port {host.Port}'.");
        }
        else if (host.BindAddress.Equals(IPAddress.Loopback) || host.BindAddress.Equals(IPAddress.IPv6Loopback))
        {
            Console.WriteLine($"Listening on {DisplayAddress(host.BindAddress)}:{host.Port} (loopback only; no other machine can connect).");
        }
        else
        {
            Console.WriteLine($"Listening on {DisplayAddress(host.BindAddress)}:{host.Port} (that single adapter only).");
        }
    }

    private static IEnumerable<string> LocalIPv4Addresses()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    yield return address.Address.ToString();
                }
            }
        }
    }

    private static void RunClient(string[] args)
    {
        string host = Args.ArgString(args, "--host") ?? "127.0.0.1";
        if (!Args.IsPlausibleHost(host))
        {
            throw new ArgumentException(
                $"Invalid server host '{host}' - use an IP address or host name without a port or scheme (e.g. --host 55.66.77.88).");
        }

        int port = Args.ReadPort(args, Args.DefaultPort);
        int timeoutSeconds = Args.ArgInt(args, "--timeout", Args.DefaultConnectTimeoutSeconds);
        if (timeoutSeconds <= 0)
        {
            throw new ArgumentException("Invalid --timeout value - the connect timeout must be a positive number of seconds.");
        }

        string playerId = Args.ArgString(args, "--player") ?? PromptPlayerId();
        PlayerColor? preferredColor = ResolveClientColor(args);

        RunClientCore(new ConnectionSettings(host, port, playerId, preferredColor, timeoutSeconds));
    }

    private static void RunClientCore(ConnectionSettings settings)
    {
        try
        {
            NetworkGameRunner.Run(
                settings.Host,
                settings.Port,
                settings.PlayerId,
                settings.PreferredColor,
                TimeSpan.FromSeconds(settings.TimeoutSeconds));
        }
        catch (Exception ex)
        {
            foreach (string line in ConnectFailure.Describe(settings.Host, settings.Port, settings.TimeoutSeconds, ex))
            {
                Console.WriteLine(line);
            }
        }
    }

    /// <summary>
    /// The chip color a client asks for when it joins: an explicit <c>--color</c> flag
    /// wins, otherwise the user is offered a picker (like the hot-seat game). Piped input
    /// yields no preference, so the server auto-assigns.
    /// </summary>
    private static PlayerColor? ResolveClientColor(string[] args)
    {
        string? raw = Args.ArgString(args, "--color");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (!Enum.TryParse<PlayerColor>(raw, ignoreCase: true, out PlayerColor color) || color == PlayerColor.Red)
            {
                throw new ArgumentException($"Invalid --color '{raw}' - use one of green, blue, yellow, magenta, cyan.");
            }

            return color;
        }

        if (Console.IsInputRedirected)
        {
            return null;
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<PlayerColor>()
                .Title("Choose your chip color:")
                .PageSize(6)
                .AddChoices(Enum.GetValues<PlayerColor>().Where(c => c != PlayerColor.Red).ToArray())
                .UseConverter(SpectreColorName));
    }

    private static string DisplayAddress(IPAddress address) =>
        address.Equals(IPAddress.Any) ? "0.0.0.0" : address.ToString();

    private static string PromptPlayerId()
    {
        if (Console.IsInputRedirected)
        {
            return "player-" + Random.Shared.Next(1000, 9999);
        }

        string? id = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Your player id:[/] (used to reconnect, shown to the opponent)")
                .PromptStyle("white")
                .Validate(i => string.IsNullOrWhiteSpace(i) ? ValidationResult.Error("Player id cannot be empty.") : ValidationResult.Success()));

        return id.Trim();
    }

    private static string PromptName(int playerIndex)
    {
        string label = playerIndex == 1 ? "Player 1" : "Player 2";

        if (Console.IsInputRedirected)
        {
            Console.Write($"{label} name: ");
            string? line = Console.ReadLine();
            return string.IsNullOrWhiteSpace(line) ? label : line.Trim();
        }

        string? name = AnsiConsole.Prompt(
            new TextPrompt<string>($"[bold]{label} name:[/]")
                .PromptStyle("white")
                .Validate(n => string.IsNullOrWhiteSpace(n) ? ValidationResult.Error("Name cannot be empty.") : ValidationResult.Success()));

        return name.Trim();
    }

    private static PlayerColor PromptColor(int playerIndex, PlayerColor? taken)
    {
        if (Console.IsInputRedirected)
        {
            // Non-interactive (piped) input: pick the lowest free color so scripts work.
            return Enum.GetValues<PlayerColor>().First(c => c != taken && c != PlayerColor.Red);
        }

        var choices = Enum.GetValues<PlayerColor>().Where(c => c != taken && c != PlayerColor.Red).ToArray();

        PlayerColor pick = AnsiConsole.Prompt(
            new SelectionPrompt<PlayerColor>()
                .Title($"Choose {(playerIndex == 1 ? "Player 1" : "Player 2")}'s chip color:")
                .PageSize(6)
                .AddChoices(choices)
                .UseConverter(SpectreColorName));

        return pick;
    }

    private static string SpectreColorName(PlayerColor color)
    {
        var dot = new Markup($"[{TerminalRenderer.ColorName(color)}]●[/]");
        return $"{dot} {color}";
    }
}