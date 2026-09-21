namespace Sequence.Domain;

/// <summary>
/// Source of randomness with an explicit contract so the engine can be made deterministic.
/// </summary>
public interface IRandom
{
    int Next(int maxExclusive);

    int Next(int minInclusive, int maxExclusive);
}

/// <summary>
/// A <see cref="IRandom"/> implementation backed by <see cref="Random"/> with a fixed seed.
/// Two instances created with the same seed produce identical sequences.
/// </summary>
public sealed class SeededRandom : IRandom
{
    private readonly Random _random;

    public SeededRandom(int seed) => _random = new Random(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}