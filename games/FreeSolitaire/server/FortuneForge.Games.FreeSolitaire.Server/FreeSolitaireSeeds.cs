using System.Security.Cryptography;

namespace FortuneForge.Games.FreeSolitaire.Server;

public static class FreeSolitaireSeeds
{
    public static uint Create()
    {
        uint seed;
        do { seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint))); }
        while (seed == 0);
        return seed;
    }
}
