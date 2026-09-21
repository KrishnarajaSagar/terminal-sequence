using System.Collections.Immutable;
using Sequence.Domain;

namespace Sequence.Engine;

/// <summary>
/// Derives the player-specific view of a game. Only what a client is allowed to see is
/// included; hidden information never leaves the authoritative <see cref="GameState"/>.
/// </summary>
public static class StateProjection
{
    public static PlayerView GetPlayerView(GameState state, PlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(state);

        PlayerInfo viewer = state.GetPlayer(playerId); // throws for unknown seats

        ImmutableList<PlayerSummary> players = state.Players
            .Select(p => new PlayerSummary(
                Id: p.Id,
                Name: p.Name,
                Color: p.Color,
                SequenceCount: state.SequenceCount(p.Id),
                IsCurrentPlayer: p.Id == state.CurrentPlayerId))
            .ToImmutableList();

        return new PlayerView(
            ViewerId: playerId,
            ViewerName: viewer.Name,
            Board: state.Board,
            Hand: viewer.Hand,
            DiscardPile: state.DiscardPile,
            CardsRemainingInDrawPile: state.CardsRemainingInDrawPile,
            SequenceTarget: state.Setup.SequenceTarget,
            CurrentPlayerId: state.CurrentPlayerId,
            TurnNumber: state.TurnNumber,
            Status: state.Status,
            Winner: state.Winner,
            CompletedSequences: state.CompletedSequences,
            Players: players,
            IsYourTurn: playerId == state.CurrentPlayerId,
            HasExchangedDeadCardThisTurn: state.CurrentPlayerHasExchangedDeadCard);
    }
}