namespace YO4X.Mql5.Engine.Feed;

/// <summary>
/// A seeded splitmix64 generator. The engine never uses <see cref="System.Random"/> so that a run
/// is reproducible byte-for-byte from its seed alone.
/// </summary>
public sealed class Mql5DeterministicRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
    private const ulong Mix1 = 0xBF58476D1CE4E5B9UL;
    private const ulong Mix2 = 0x94D049BB133111EBUL;

    private ulong state;

    /// <summary>Initializes a new instance seeded with <paramref name="seed"/>.</summary>
    public Mql5DeterministicRandom(ulong seed) => state = seed;

    /// <summary>Gets the seed value the generator currently holds.</summary>
    public ulong State => state;

    /// <summary>Returns the next 64-bit value in the stream.</summary>
    public ulong NextUInt64()
    {
        unchecked
        {
            state += GoldenGamma;
            ulong z = state;
            z = (z ^ (z >> 30)) * Mix1;
            z = (z ^ (z >> 27)) * Mix2;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Returns the next value in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Returns the next value in [-1, 1).</summary>
    public double NextSigned() => (NextDouble() * 2.0) - 1.0;

    /// <summary>Returns the next integer in [minInclusive, maxExclusive).</summary>
    public int NextInt32(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        return (int)((long)minInclusive + (long)(NextUInt64() % range));
    }
}
