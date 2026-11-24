using FortuneForge.Games.Slots;

namespace FortuneForge.Games.PharaohsAscent.RtpAnalysis;

/// <summary>Test-only SplitMix64 stream with rejection sampling for unbiased bounded draws.</summary>
internal sealed class SeededSource(ulong seed) : IGameRandomSource
{
    private ulong state = seed;
    public int Next(int maximumExclusive)
    {
        if (maximumExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
        var bound = (ulong)maximumExclusive;
        var threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do { value = Mix(state += 0x9E3779B97F4A7C15UL); } while (value < threshold);
        return (int)(value % bound);
    }
    internal static ulong For(ulong seed, long stake, string purpose)
    {
        var hash = seed ^ (ulong)stake;
        foreach (var character in purpose) hash = unchecked((hash ^ character) * 0x100000001B3UL);
        return Mix(hash);
    }
    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
