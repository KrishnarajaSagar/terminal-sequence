using System.Collections.Generic;
using System.Linq;
using Sequence.Domain;

namespace Sequence.Server;

/// <summary>
/// Public snapshot of who is currently sitting on each seat, used by the host's
/// status display. Read-only: the roster is derived from the live <see cref="GameHost"/>
/// placements and never mutates game state.
/// </summary>
public readonly record struct HostRosterEntry(
    PlayerId Seat,
    string PlayerName,
    PlayerColor Color,
    bool Connected,
    string? ClientId);

/// <summary>
/// Read-only status surface for the host's lobby screen. The accept loop and the
/// <see cref="GameServer"/> are untouched; this snapshot just mirrors whichever seats
/// hold a registered connection right now.
/// </summary>
public sealed partial class GameHost
{
    /// <summary>
    /// Current seat occupancy in seat order (first player, then second player). A seat
    /// shows <see cref="HostRosterEntry.Connected"/> false until a client bound to it, so
    /// the host can tell the lobby how many players are still missing.
    /// </summary>
    public IReadOnlyList<HostRosterEntry> Roster
    {
        get
        {
            PlayerId[] seats = { _server.FirstPlayerId, _server.SecondPlayerId };
            var entries = new List<HostRosterEntry>(seats.Length);

            foreach (PlayerId seat in seats)
            {
                // GetPlayerView locks internally; this is a lightweight point-in-time read.
                PlayerSummary summary = _server.GetPlayerView(seat).Viewer;
                ClientConnection? connection = _clients.Values.FirstOrDefault(c => c.Registered && c.Seat.Equals(seat));

                entries.Add(new HostRosterEntry(
                    seat,
                    summary.Name,
                    summary.Color,
                    Connected: connection is not null,
                    ClientId: connection?.ClientId));
            }

            return entries;
        }
    }
}