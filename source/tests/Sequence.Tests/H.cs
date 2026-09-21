using System.Collections.Immutable;
using Sequence.Domain;
using Sequence.Engine;

namespace Sequence.Tests;

internal static class H
{
    public static PlayerId P0 { get; } = new(0);

    public static PlayerId P1 { get; } = new(1);

    public static GameState Start(int seed = 12345, int target = 2) =>
        GameEngine.StartGame(new GameSetup("Alice", "Bob", seed, target));

    public static Card CardAt(int row, int col) =>
        BoardLayout.Standard.GetCardAt(new BoardPosition(row, col))!.Value;

    public static BoardPosition Pos(int row, int col) => new(row, col);

    public static ImmutableList<Card> Hand(params string[] codes) =>
        ImmutableList.CreateRange(codes.Select(Card.Parse));

    /// <summary>Places chips directly (used to set up board scenarios).</summary>
    public static BoardState BoardWith(params (BoardPosition Position, PlayerId Owner)[] chips)
    {
        BoardState board = BoardState.Empty;
        foreach ((BoardPosition position, PlayerId owner) in chips)
        {
            board = board.PlaceChip(position, owner);
        }

        return board;
    }

    /// <summary>Overrides both hands and sets the current player, leaving everything else alone.</summary>
    public static GameState With(GameState state, PlayerId current, ImmutableList<Card>? p0Hand = null, ImmutableList<Card>? p1Hand = null)
    {
        GameState s = state;
        if (p0Hand is not null)
        {
            s = s.WithPlayerHand(P0, p0Hand);
        }

        if (p1Hand is not null)
        {
            s = s.WithPlayerHand(P1, p1Hand);
        }

        return s with { CurrentPlayerId = current };
    }

    public static GameState WithBoard(GameState state, BoardState board, PlayerId current) =>
        state.WithBoard(board) with { CurrentPlayerId = current };
}