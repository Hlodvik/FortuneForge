using System.Security.Cryptography;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

public sealed class CryptographicSlotRandom : IGameRandomSource
{
    public int Next(int maximumExclusive) => RandomNumberGenerator.GetInt32(maximumExclusive);
}
