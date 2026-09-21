namespace Sequence.Engine;

/// <summary>
/// Derives the per-shuffle seed from a base seed and the round counter kept on the
/// game state. Each round of deck replacement uses a distinct seed, keeping the game
/// functional and (given the same base seed) fully reproducible.
/// </summary>
public static class RandomSeeds
{
    public static int Derive(int baseSeed, int round) => unchecked((baseSeed * 397) + round);
}