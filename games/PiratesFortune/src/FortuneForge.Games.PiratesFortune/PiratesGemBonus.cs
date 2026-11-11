using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FortuneForge.Games.PiratesFortune;

public sealed record PiratesGemAward(int GemCount, long WagerTotal);
public sealed record PiratesGemProgress(int Count, long WagerTotal,
    IReadOnlyList<PiratesGemAward> Awards);

public static class PiratesGemBonus
{
    public const string RuleVersion = "pirates-gem-chance-v1";
    public const int MaximumGems = 25;
    public const int FirstEligibleGem = 7;
    public static IReadOnlyList<string> Modes { get; } = ["sync", "rows", "paw", "rand"];

    public static string AddFeatureMode(string? current, string mode)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) throw new ArgumentOutOfRangeException(nameof(mode));
        var active = new HashSet<string>(current?.Split('-', StringSplitOptions.RemoveEmptyEntries) ?? [],
            StringComparer.Ordinal) { mode };
        return string.Join("-", Modes.Where(active.Contains));
    }

    public static PiratesGemProgress Advance(Guid spinId, string mode, int currentCount,
        long currentWagerTotal, int gemsAwarded, long wagerPerGem)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal) || currentCount is < 0 or >= MaximumGems ||
            currentWagerTotal < 0 || gemsAwarded < 0 || wagerPerGem < 0)
            throw new ArgumentOutOfRangeException(nameof(currentCount));
        var count = currentCount;
        var total = currentCount == 0 ? 0 : currentWagerTotal;
        var awards = new List<PiratesGemAward>();
        for (var index = 0; index < gemsAwarded; index++)
        {
            count++; total = checked(total + wagerPerGem);
            if (!ShouldAward(spinId, mode, index, count)) continue;
            awards.Add(new(count, total)); count = 0; total = 0;
        }
        return new(count, total, awards);
    }

    private static bool ShouldAward(Guid spinId, string mode, int gemIndex, int count)
    {
        if (count < FirstEligibleGem) return false;
        if (count >= MaximumGems) return true;
        Span<byte> input = stackalloc byte[25]; spinId.TryWriteBytes(input);
        input[16] = checked((byte)Modes.IndexOf(mode));
        BinaryPrimitives.WriteInt32LittleEndian(input[17..21], gemIndex);
        BinaryPrimitives.WriteInt32LittleEndian(input[21..25], count);
        Span<byte> hash = stackalloc byte[32]; SHA256.HashData(input, hash);
        var chance = 300 + 150 * (count - FirstEligibleGem);
        return BinaryPrimitives.ReadUInt32LittleEndian(hash) % 10_000 < chance;
    }

    private static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
        return -1;
    }
}
