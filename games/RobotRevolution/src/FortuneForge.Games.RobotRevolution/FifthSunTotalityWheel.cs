using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution;

public sealed record FifthSunAlignment(int DialStep, IReadOnlyList<SunstoneRing> Rings,
    IReadOnlyList<string> AwakenedGuardianIds, int AlignmentMultiplier, long TotalAwardPoints);

/// <summary>Three distinct board-derived alignments with round-scoped guardian memory.</summary>
public static class FifthSunTotalityWheel
{
    public const string MathVersion = "fifth-sun-totality-v1";
    private static readonly (string Id, string Symbol, int[] Path)[] Guardians =
    [
        ("jaguar", "JAGUAR", [0, 1, 2, 1, 0]),
        ("eagle", "EAGLE", [3, 2, 1, 2, 3]),
        ("serpent", "SERPENT", [1, 2, 3, 2, 1])
    ];

    public static SunstoneEclipseFeature Resolve(IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints, string? mode)
    {
        ArgumentNullException.ThrowIfNull(reels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        if (reels.Count != 5 || reels.Any(reel => reel.Count != 4)) throw new ArgumentException("Totality uses five four-row reels.", nameof(reels));
        if (mode is not null && !FifthSunTotalityDefinitions.IsMode(mode)) throw new ArgumentException("Unknown Eclipse mode.", nameof(mode));
        var active = mode is not null;
        var initial = reels.SelectMany((column, reel) => column.Select((symbol, row) => symbol == "SUNSTONE" ? (reel + 1) * (row + 1) : 0)).Sum() % 4;
        var direction = mode == FifthSunTotalityDefinitions.LunarMode ? -1 : 1;
        var awakened = new HashSet<string>(StringComparer.Ordinal);
        var alignments = new List<FifthSunAlignment>();
        for (var turn = 0; turn < (active ? FifthSunTotalityDefinitions.AlignmentsPerRound : 1); turn++)
        {
            var step = (initial + direction * turn + 4) % 4;
            var rings = Guardians.Select((guardian, index) =>
            {
                var rotation = (step + index) % 4;
                var path = guardian.Path.Select((row, reel) => new GridPosition(reel, (row + rotation) % 4)).ToArray();
                var matched = path.Where(cell => reels[cell.Reel][cell.Row] is "SUNSTONE" or "WILD" || reels[cell.Reel][cell.Row] == guardian.Symbol).ToArray();
                var tier = matched.Length switch { 3 => 0.5m, 4 => 1m, 5 => 2m, _ => 0m };
                if (matched.Length >= 3) awakened.Add(guardian.Id);
                return new SunstoneRing(guardian.Id, guardian.Symbol, rotation, path, matched, matched.Length, matched.Length >= 3, tier, 0);
            }).ToArray();
            var linked = Math.Max(1, awakened.Count);
            var scale = active ? FifthSunTotalityDefinitions.EclipseWheelScale : 1m;
            rings = rings.Select(ring => ring with { AwardPoints = Scale(wagerPoints, ring.BaseMultiplier * linked * scale) }).ToArray();
            alignments.Add(new(step, rings, Guardians.Where(guardian => awakened.Contains(guardian.Id)).Select(guardian => guardian.Id).ToArray(), linked,
                rings.Sum(ring => ring.AwardPoints)));
        }
        var heart = new SunstoneHeart(2, 2, reels[2][2], reels[2][2] == "SUNSTONE");
        var totality = active ? awakened.Count == 3 : awakened.Count == 3 && heart.Lit;
        var tribute = totality ? Scale(wagerPoints, active ? FifthSunTotalityDefinitions.TotalityAwardMultiplier * (heart.Lit ? 2 : 1) : 2m) : 0;
        var maximum = active ? FifthSunTotalityDefinitions.AlignmentsPerRound * 18m * FifthSunTotalityDefinitions.EclipseWheelScale + 2m * FifthSunTotalityDefinitions.TotalityAwardMultiplier : 20m;
        return new(3, "fifth-sun", MathVersion, initial, 4, active, heart, alignments[^1].Rings,
            awakened.Count, Math.Max(1, awakened.Count), totality, tribute, alignments.Sum(alignment => alignment.TotalAwardPoints) + tribute, maximum)
        { EclipseMode = mode == FifthSunTotalityDefinitions.SolarMode ? "solar" : mode == FifthSunTotalityDefinitions.LunarMode ? "lunar" : null,
            Alignments = alignments, TotalityAwardPoints = tribute,
            WheelAwardScale = active ? FifthSunTotalityDefinitions.EclipseWheelScale : 1m };
    }

    public static ClassicFeaturePayout CalculatePayout(IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints, string? mode)
    {
        var feature = Resolve(reels, wagerPoints, mode);
        var awards = new List<PaylinePayout>();
        foreach (var (alignment, turn) in feature.Alignments!.Select((alignment, turn) => (alignment, turn)))
        foreach (var (ring, index) in alignment.Rings.Select((ring, index) => (ring, index)))
        {
            if (ring.AwardPoints == 0) continue;
            var id = 1001 + turn * 10 + index;
            var wilds = ring.MatchedPositions.Where(cell => reels[cell.Reel][cell.Row] == "WILD").ToArray();
            awards.Add(new(id, ring.AwardPoints, [new(new(id, ring.SymbolId, ring.Matches, ring.MatchedPositions, wilds),
                checked((long)Math.Ceiling(ring.BaseMultiplier * alignment.AlignmentMultiplier * feature.WheelAwardScale!.Value)), ring.AwardPoints)]));
        }
        if (feature.EclipseAwardPoints > 0)
            awards.Add(new(1010, feature.EclipseAwardPoints, [new(new(1010, "SUNSTONE", 1, [new(2, 2)], []),
                feature.Heart.Lit ? 2 : 1, feature.EclipseAwardPoints)]));
        return new(0, 0, 0, awards);
    }

    private static long Scale(long wagerPoints, decimal multiplier) => checked((long)Math.Floor(wagerPoints * multiplier));
}
