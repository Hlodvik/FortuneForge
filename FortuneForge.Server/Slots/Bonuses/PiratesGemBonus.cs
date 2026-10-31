using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FortuneForge.Server.Slots.Bonuses;

/// <summary>
/// A Pirates gem can wake its feature once the pile has started to grow. The
/// chance rises on each gem and the twenty-fifth always awards the feature.
/// The spin ID makes the draw unpredictable before a spin and replayable later.
/// </summary>
internal static class PiratesGemBonus
{
    public const string RuleVersion = "pirates-gem-chance-v1";
    public const int MaximumGems = 25;
    private const int FirstEligibleGem = 7;
    internal static readonly string[] Modes = ["sync", "rows", "paw", "rand"];
    internal static readonly IReadOnlyDictionary<string, string> ModesBySymbol =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEAL_SYNC"] = "sync", ["SEAL_ROWS"] = "rows",
            ["SEAL_PAW"] = "paw", ["SEAL_RAND"] = "rand"
        };

    internal static string AddFeatureMode(string? current, string mode)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(mode));
        var active = new HashSet<string>(
            current?.Split('-', StringSplitOptions.RemoveEmptyEntries) ?? [],
            StringComparer.Ordinal) { mode };
        return string.Join("-", Modes.Where(active.Contains));
    }

    internal static PiratesGemProgress Advance(
        Guid spinId,
        string mode,
        int currentCount,
        long currentWagerTotal,
        int gemsAwarded,
        long wagerPerGem)
    {
        if (currentCount is < 0 or >= MaximumGems || currentWagerTotal < 0 ||
            gemsAwarded < 0 || wagerPerGem < 0)
            throw new ArgumentOutOfRangeException(nameof(currentCount), "Invalid Pirates gem collection state.");

        var count = currentCount;
        var wagerTotal = currentCount == 0 ? 0 : currentWagerTotal;
        var awards = new List<PiratesGemAward>();
        for (var gemIndex = 0; gemIndex < gemsAwarded; gemIndex++)
        {
            count++;
            wagerTotal = checked(wagerTotal + wagerPerGem);
            if (!ShouldAward(spinId, mode, gemIndex, count)) continue;

            awards.Add(new PiratesGemAward(count, wagerTotal));
            count = 0;
            wagerTotal = 0;
        }

        return new PiratesGemProgress(count, wagerTotal, awards);
    }

    private static bool ShouldAward(Guid spinId, string mode, int gemIndex, int count)
    {
        if (count < FirstEligibleGem) return false;
        if (count >= MaximumGems) return true;

        var modeIndex = mode switch
        {
            "sync" => (byte)0,
            "rows" => (byte)1,
            "paw" => (byte)2,
            "rand" => (byte)3,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        Span<byte> input = stackalloc byte[25];
        spinId.TryWriteBytes(input);
        input[16] = modeIndex;
        BinaryPrimitives.WriteInt32LittleEndian(input[17..21], gemIndex);
        BinaryPrimitives.WriteInt32LittleEndian(input[21..25], count);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);

        // 3% on gem seven, then +1.5 percentage points per gem. This keeps the
        // expected trigger near the previous fifteen-gem award cadence.
        var chanceBasisPoints = 300 + 150 * (count - FirstEligibleGem);
        var draw = BinaryPrimitives.ReadUInt32LittleEndian(hash) % 10_000;
        return draw < chanceBasisPoints;
    }
}

internal sealed record PiratesGemProgress(
    int Count,
    long WagerTotal,
    IReadOnlyList<PiratesGemAward> Awards);

internal sealed record PiratesGemAward(int GemCount, long WagerTotal);

internal sealed record PiratesGemSettlementEvidence(
    string RuleVersion,
    Dictionary<string, long> CountsBefore,
    Dictionary<string, long> WagerCentsBefore,
    Dictionary<string, long> CountsAfter,
    Dictionary<string, long> WagerCentsAfter,
    int FreeSpinsAwarded,
    string? FeatureMode,
    long FreeSpinWagerPoints);
