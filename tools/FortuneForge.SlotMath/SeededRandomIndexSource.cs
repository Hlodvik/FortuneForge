using FortuneForge.Server.Slots.Reels;

sealed class SeededRandomIndexSource(int seed) : IRandomIndexSource
{
    private readonly Random _random = new(seed);

    public int Next(int maximumExclusive) => _random.Next(maximumExclusive);
}
