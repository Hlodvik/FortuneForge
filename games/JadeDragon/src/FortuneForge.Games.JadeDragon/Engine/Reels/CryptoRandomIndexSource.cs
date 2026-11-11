using System.Security.Cryptography;

namespace FortuneForge.Games.JadeDragon.Reels;

public sealed class CryptoRandomIndexSource : IRandomIndexSource
{
    public int Next(int maximumExclusive)
    {
        if (maximumExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
        }

        return RandomNumberGenerator.GetInt32(maximumExclusive);
    }
}
