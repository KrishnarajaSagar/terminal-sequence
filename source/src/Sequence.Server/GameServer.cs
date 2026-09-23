using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Server;

/// <summary>
/// The authoritative owner of a game. Clients never touch the engine's
/// <see cref="GameState"/> directly: they connect to a seat, submit actions (validated
/// against the seat they are bound to), and receive player-specific <see cref="PlayerView"/>s.
/// Runs fully in-process today; the TCP listener will reuse this same API.
/// </summary>
public sealed class GameServer
{
    private readonly object _gate = new();
    private GameState _state;
    private readonly Dictionary<string, PlayerId> _seatOfClient = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerId> _lastSeatOfClient = new(StringComparer.Ordinal);

    public GameServer(GameSetup setup)
    {
        _state = GameEngine.StartGame(setup);
    }

    /// <summary>
    /// Restores a server from a previously saved authoritative state (e.g. after the
    /// process restarted). Play continues from exactly this state; only the live
    /// connection bindings are new.
    /// </summary>
    public GameServer(GameState restored)
    {
        _state = restored ?? throw new ArgumentNullException(nameof(restored));
    }

    /// <summary>The authoritative state, for snapshots and persistence.</summary>
    public GameState CurrentState
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public PlayerId FirstPlayerId =>
        _state.Players[0].Id;

    public PlayerId SecondPlayerId =>
        _state.Players[1].Id;

    public PlayerId CurrentPlayerId
    {
        get
        {
            lock (_gate)
            {
                return _state.CurrentPlayerId;
            }
        }
    }

    public GameStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _state.Status;
            }
        }
    }

    public PlayerId? Winner
    {
        get
        {
            lock (_gate)
            {
                return _state.Winner;
            }
        }
    }

    /// <summary>
    /// Binds a client identity to a free seat (first seat, then second seat). A known
    /// client keeps its existing seat. Clients that disconnected are remembered, so a
    /// reconnect reclaims the same seat as long as nobody else took it. On a client's
    /// first join its requested <paramref name="preferredColor"/> is applied when that
    /// color is still free; reconnects keep the color the seat already owns. Throws when
    /// both seats are already taken.
    /// </summary>
    public PlayerId ConnectPlayer(string clientId, PlayerColor? preferredColor = null)
    {
        lock (_gate)
        {
            if (_seatOfClient.TryGetValue(clientId, out PlayerId known))
            {
                return known;
            }

            PlayerId seat;
            bool reclaiming = _lastSeatOfClient.TryGetValue(clientId, out PlayerId previous);
            if (reclaiming && !_seatOfClient.ContainsValue(previous))
            {
                seat = previous;
            }
            else if (!_seatOfClient.ContainsValue(FirstPlayerId))
            {
                seat = FirstPlayerId;
            }
            else if (!_seatOfClient.ContainsValue(SecondPlayerId))
            {
                seat = SecondPlayerId;
            }
            else
            {
                throw new InvalidOperationException("The game is full. Both seats are taken.");
            }

            if (!reclaiming && preferredColor is { } color && !SeatInUse(color))
            {
                _state = _state.WithPlayerColor(seat, color);
            }

            _seatOfClient[clientId] = seat;
            _lastSeatOfClient[clientId] = seat;
            return seat;
        }
    }

    private bool SeatInUse(PlayerColor color) =>
        _state.Players.Any(p => p.Color == color);

    /// <summary>Releases a client's seat. Its actions are refused until it reconnects.</summary>
    public void DisconnectPlayer(string clientId)
    {
        lock (_gate)
        {
            // The last-seat mapping is deliberately kept, so a reconnect is recognized.
            _seatOfClient.Remove(clientId);
        }
    }

    /// <summary>Whether the client identity currently holds a live seat.</summary>
    public bool IsClientConnected(string clientId)
    {
        lock (_gate)
        {
            return _seatOfClient.ContainsKey(clientId);
        }
    }

    /// <summary>
    /// Names the seat bound to a client after its announced identity. Purely cosmetic:
    /// every client's view and the disconnect notices then reflect the id (e.g.
    /// <c>--player alice</c>) instead of the setup defaults, so names stay in sync.
    /// </summary>
    public void RenamePlayer(string clientId, string displayName)
    {
        lock (_gate)
        {
            if (_seatOfClient.TryGetValue(clientId, out PlayerId seat))
            {
                _state = _state.WithPlayerName(seat, displayName);
            }
        }
    }

    /// <summary>The seat bound to a client, or <c>null</c> if it is not connected.</summary>
    public PlayerId? SeatForClient(string clientId)
    {
        lock (_gate)
        {
            return _seatOfClient.TryGetValue(clientId, out PlayerId seat) ? seat : null;
        }
    }

    /// <summary>The player-specific view for a seat. Nothing private ever leaves the server.</summary>
    public PlayerView GetPlayerView(PlayerId playerId)
    {
        lock (_gate)
        {
            return StateProjection.GetPlayerView(_state, playerId);
        }
    }

    /// <summary>
    /// Validates and applies an action on behalf of a connected client. The client may
    /// only act for the seat it is bound to; the authoritative <see cref="GameState"/> is
    /// then updated only when the engine accepts the action.
    /// </summary>
    public ActionResult SubmitAction(string clientId, PlayerAction action)
    {
        lock (_gate)
        {
            if (!_seatOfClient.TryGetValue(clientId, out PlayerId seat))
            {
                return ActionResult.Failure(new ActionError(ActionErrorCode.ClientNotConnected, "This client is not connected to a seat."));
            }

            if (seat != action.PlayerId)
            {
                return ActionResult.Failure(new ActionError(ActionErrorCode.WrongPlayerForClient, "A client may only submit actions for its own seat.", action.PlayerId));
            }

            ActionResult result = GameEngine.ApplyAction(_state, action);
            if (result.IsSuccess)
            {
                _state = result.NewState!;
            }

            return result;
        }
    }
}