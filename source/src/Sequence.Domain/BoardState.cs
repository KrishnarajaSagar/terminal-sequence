using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Sequence.Domain;

/// <summary>
/// The chip state of the board. Every edit returns a new immutable instance,
/// leaving the original unchanged.
/// </summary>
public sealed record BoardState
{
    public static BoardState Empty { get; } =
        new(ImmutableDictionary<BoardPosition, PlayerId>.Empty, ImmutableHashSet<BoardPosition>.Empty);

    public ImmutableDictionary<BoardPosition, PlayerId> Chips { get; init; }

    /// <summary>Positions sealed as part of a completed sequence; they can no longer be changed.</summary>
    public ImmutableHashSet<BoardPosition> LockedPositions { get; init; }

    private BoardState(ImmutableDictionary<BoardPosition, PlayerId> chips, ImmutableHashSet<BoardPosition> lockedPositions)
    {
        Chips = chips;
        LockedPositions = lockedPositions;
    }

    public bool IsOccupied(BoardPosition position) => Chips.ContainsKey(position);

    public bool IsLocked(BoardPosition position) => LockedPositions.Contains(position);

    public PlayerId? ChipsAt(BoardPosition position) =>
        Chips.TryGetValue(position, out PlayerId owner) ? owner : null;

    public IReadOnlyCollection<BoardPosition> OccupiedPositionsOf(PlayerId player) =>
        new ReadOnlyCollection<BoardPosition>(Chips.Where(pair => pair.Value == player).Select(pair => pair.Key).ToList());

    /// <summary>Places a chip. Throws if the space already holds a chip or is locked.</summary>
    public BoardState PlaceChip(BoardPosition position, PlayerId player)
    {
        if (IsOccupied(position))
        {
            throw new InvalidOperationException($"Cannot place a chip on {position}: the space is already occupied.");
        }

        if (IsLocked(position))
        {
            throw new InvalidOperationException($"Cannot place a chip on {position}: the space is locked by a completed sequence.");
        }

        return this with { Chips = Chips.SetItem(position, player) };
    }

    /// <summary>Removes a chip drawn away by a one-eyed jack. Locked positions cannot be removed.</summary>
    public BoardState RemoveChip(BoardPosition position)
    {
        if (!IsOccupied(position))
        {
            return this;
        }

        if (IsLocked(position))
        {
            throw new InvalidOperationException($"Cannot remove the chip on {position}: the space is locked by a completed sequence.");
        }

        return this with { Chips = Chips.Remove(position) };
    }

    /// <summary>Seals positions as part of a completed sequence.</summary>
    public BoardState LockPositions(IReadOnlyCollection<BoardPosition> positions) =>
        this with { LockedPositions = LockedPositions.Union(positions) };

    /// <summary>All positions a completed sequence can be scored at later.</summary>
    public IReadOnlyCollection<BoardPosition> Positions => (IReadOnlyCollection<BoardPosition>)Chips.Keys;
}