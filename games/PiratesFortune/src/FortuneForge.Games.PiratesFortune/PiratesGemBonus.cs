using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FortuneForge.Games.PiratesFortune;

public sealed record PiratesGemAward(int GemCount, long WagerTotal);
public sealed record PiratesGemProgress(int Count, long WagerTotal,
    IReadOnlyList<PiratesGemAward> Awards);
public sealed record PiratesGemGrant(string Mode, int GemCount, long WagerTotalCents);
public sealed record PiratesPaidGemProgress(IReadOnlyDictionary<string, long> Counts,
    IReadOnlyDictionary<string, long> WagerCents, int FreeSpinsAwarded, string? FeatureMode,
    long FreeSpinWagerPoints, bool Changed, IReadOnlyList<PiratesGemGrant> Grants);

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

    /// <summary>Settles every landed gem against the original spin identity and exact tracked wagers.</summary>
    public static PiratesPaidGemProgress AdvancePaid(Guid spinId, IReadOnlyDictionary<string, long> counts,
        IReadOnlyDictionary<string, long> wagerCents, IReadOnlyDictionary<string, int> landedSeals,
        long spinWagerCents, decimal pointValueInCents, int collectionAwardedSpins, string? scatterFeatureMode)
    {
        var nextCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var nextWagers = new Dictionary<string, long>(StringComparer.Ordinal);
        var grants = new List<PiratesGemGrant>();
        var modeOnAward = scatterFeatureMode;
        var lockedWager = 0L;
        var changed = false;
        foreach (var mode in Modes)
        {
            var count = counts.GetValueOrDefault(mode);
            var wagerTotal = wagerCents.GetValueOrDefault(mode);
            var landed = landedSeals.GetValueOrDefault($"SEAL_{mode.ToUpperInvariant()}");
            if (landed > 0)
            {
                var track = Advance(spinId, mode, checked((int)count), wagerTotal, landed, spinWagerCents);
                count = track.Count;
                wagerTotal = track.WagerTotal;
                changed = true;
                foreach (var award in track.Awards)
                {
                    modeOnAward = AddFeatureMode(modeOnAward, mode);
                    grants.Add(new(mode, award.GemCount, award.WagerTotal));
                    if (lockedWager > 0) continue;
                    var points = award.WagerTotal / pointValueInCents;
                    if (points != decimal.Truncate(points))
                        throw new InvalidOperationException("The awarded gem wager does not align with the game's point value.");
                    lockedWager = Math.Max(1L,
                        checked((long)Math.Round(points / award.GemCount, MidpointRounding.AwayFromZero)));
                }
            }
            nextCounts.Add(mode, count);
            nextWagers.Add(mode, wagerTotal);
        }
        return new(nextCounts, nextWagers, checked(grants.Count * collectionAwardedSpins), modeOnAward,
            lockedWager, changed, grants);
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
