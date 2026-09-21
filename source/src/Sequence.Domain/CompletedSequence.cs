using System.Collections.Immutable;

namespace Sequence.Domain;

/// <summary>
/// A sequence is exactly five adjacent board positions. A single line of six or more
/// marks belongs to at most two sequences per player (the two anchored 5-windows);
/// each is recorded separately and deduplicated by set equality so a line is never
/// over-counted.
/// </summary>
public sealed record CompletedSequence(PlayerId Player, ImmutableArray<BoardPosition> Positions)
{
    /// <summary>Builds a sequence from any set of positions, normalized to a stable
    /// row-major order so two equal sequences compare equal.</summary>
    public static CompletedSequence Create(PlayerId player, IEnumerable<BoardPosition> positions) =>
        new(player, Order(positions));

    public static ImmutableArray<BoardPosition> Order(IEnumerable<BoardPosition> positions) =>
        positions
            .OrderBy(p => p.Row)
            .ThenBy(p => p.Col)
            .ToImmutableArray();

    public override string ToString() => string.Join('|', Positions.Select(p => p.ToString()));
}