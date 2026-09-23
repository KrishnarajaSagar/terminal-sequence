namespace Sequence.Terminal;

/// <summary>
/// The top-level screens the terminal client can be in. The game engine and the server
/// know nothing about these; they exist purely to organize the keyboard-driven flow so
/// a player never has to type commands such as "host" or "join".
/// </summary>
public enum UiState
{
    /// <summary>The opening screen: choose to host, to join, or to exit.</summary>
    MainMenu,

    /// <summary>Configuration form shown before a server is started ("Host Game").</summary>
    HostForm,

    /// <summary>Configuration form shown before connecting ("Join Game").</summary>
    ConnectionMenu,

    /// <summary>Server running and waiting for the second player.</summary>
    HostLobby,

    /// <summary>Connected and waiting for the game to begin.</summary>
    ClientLobby,

    /// <summary>The board is being played (local hot-seat or over the network).</summary>
    Game,

    /// <summary>The application is exiting.</summary>
    Exit,
}