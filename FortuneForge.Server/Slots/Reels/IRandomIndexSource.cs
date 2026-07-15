namespace FortuneForge.Server.Slots.Reels;

public interface IRandomIndexSource
{
    int Next(int maximumExclusive);
}
