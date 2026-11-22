using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.RobotRevolution;

public sealed record SunstoneHeart(int Reel, int Row, string SymbolId, bool Lit);
public sealed record SunstoneRing(string Id, string SymbolId, int RotationSteps,
    IReadOnlyList<GridPosition> Path, IReadOnlyList<GridPosition> MatchedPositions,
    int Matches, bool Aligned, decimal BaseMultiplier, long AwardPoints);
public sealed record SunstoneEclipseFeature(int ContractVersion, string Kind, string MathVersion,
    int DialStep, int Rows, bool IsEclipseSpin, SunstoneHeart Heart, IReadOnlyList<SunstoneRing> Rings,
    int AlignedRings, int AlignmentMultiplier, bool EclipseTriggered, long EclipseAwardPoints,
    long TotalAwardPoints, decimal MaxAwardMultiplier)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EclipseMode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FifthSunAlignment>? Alignments { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalityAwardPoints { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? WheelAwardScale { get; init; }
}

internal sealed record SunwheelVocabulary(int ContractVersion, string Kind, string MathVersion,
    string Sunstone, string Wild, IReadOnlyList<string> GuardianSymbols);

/// <summary>Game-owned, board-derived calendar geometry and fully settled feature awards.</summary>
public static class SunstoneEclipse
{
    public const string MathVersion = "sunstone-eclipse-v1";
    private static readonly SunwheelVocabulary Vocabulary = new(1, "sunstone-eclipse", MathVersion,
        "POWER", "ACE", ["7", "6", "5"]);
    private static readonly (string Id, string Symbol, int[] Path)[] Guardians =
    [
        ("jaguar", "7", [0, 1, 2, 1, 0]),
        ("eagle", "6", [3, 2, 1, 2, 3]),
        ("serpent", "5", [1, 2, 3, 2, 1])
    ];

    public static SunstoneEclipseFeature Resolve(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, bool isEclipseSpin) => Resolve(reels, wagerPoints, isEclipseSpin, Vocabulary);

    internal static SunstoneEclipseFeature Resolve(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, bool isEclipseSpin, SunwheelVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(reels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        if (reels.Count != 5 || reels[0].Count is not (4 or 6) || reels.Any(reel => reel.Count != reels[0].Count))
            throw new ArgumentException("Sunstone Eclipse requires five equally sized four- or six-row reels.", nameof(reels));

        var rows = reels[0].Count;
        var dialStep = reels.SelectMany((column, reel) => column.Select((symbol, row) =>
            symbol == vocabulary.Sunstone ? (reel + 1) * (row + 1) : 0)).Sum() % rows;
        var rings = Guardians.Select((guardian, index) =>
        {
            var rotation = (dialStep + index) % rows;
            var path = guardian.Path.Select((row, reel) => new GridPosition(reel, (row + rotation) % rows)).ToArray();
            var symbol = vocabulary.GuardianSymbols[index];
            var matched = path.Where(position => reels[position.Reel][position.Row] == vocabulary.Sunstone ||
                reels[position.Reel][position.Row] == vocabulary.Wild || reels[position.Reel][position.Row] == symbol).ToArray();
            var multiplier = matched.Length switch { 3 => 0.5m, 4 => 1m, 5 => 2m, _ => 0m };
            return new SunstoneRing(guardian.Id, symbol, rotation, path, matched,
                matched.Length, matched.Length >= 3, multiplier, 0);
        }).ToArray();
        var aligned = rings.Count(ring => ring.Aligned);
        var linked = Math.Max(1, aligned);
        var freeMultiplier = isEclipseSpin ? 2m : 1m;
        rings = rings.Select(ring => ring with
        {
            AwardPoints = Scale(wagerPoints, ring.BaseMultiplier * linked * freeMultiplier)
        }).ToArray();
        var heart = new SunstoneHeart(2, rows / 2, reels[2][rows / 2], reels[2][rows / 2] == vocabulary.Sunstone);
        var eclipse = aligned == 3 && heart.Lit;
        var eclipseAward = eclipse ? Scale(wagerPoints, 2m * freeMultiplier) : 0;
        return new(vocabulary.ContractVersion, vocabulary.Kind, vocabulary.MathVersion, dialStep, rows, isEclipseSpin, heart, rings,
            aligned, linked, eclipse, eclipseAward, checked(rings.Sum(ring => ring.AwardPoints) + eclipseAward),
            20m * freeMultiplier);
    }

    private static long Scale(long wagerPoints, decimal multiplier) =>
        checked((long)Math.Floor(wagerPoints * multiplier));

    internal static ClassicFeaturePayout CalculatePayout(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, decimal payoutMultiplier, bool isEclipseSpin) =>
        CalculatePayout(reels, wagerPoints, payoutMultiplier, isEclipseSpin, Vocabulary, true);

    internal static ClassicFeaturePayout CalculatePayout(IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints, decimal payoutMultiplier, bool isEclipseSpin, SunwheelVocabulary vocabulary, bool patternBonus)
    {
        var feature = Resolve(reels, wagerPoints, isEclipseSpin, vocabulary);
        var patterns = patternBonus ? ClassicSlotEngine.CalculatePatternPayouts(reels, wagerPoints, payoutMultiplier) : [];
        var awards = new List<PaylinePayout>(patterns);
        for (var index = 0; index < feature.Rings.Count; index++)
        {
            var ring = feature.Rings[index];
            if (ring.AwardPoints == 0) continue;
            var id = 1001 + index;
            var wilds = ring.MatchedPositions.Where(position => reels[position.Reel][position.Row] == vocabulary.Wild).ToArray();
            awards.Add(new(id, ring.AwardPoints,
                [new(new(id, ring.SymbolId, ring.Matches, ring.MatchedPositions, wilds),
                    checked((long)Math.Ceiling(ring.BaseMultiplier * feature.AlignmentMultiplier * (isEclipseSpin ? 2 : 1))),
                    ring.AwardPoints)]));
        }
        if (feature.EclipseAwardPoints > 0)
            awards.Add(new(1010, feature.EclipseAwardPoints,
                [new(new(1010, vocabulary.Sunstone, 1, [new(feature.Heart.Reel, feature.Heart.Row)], []),
                    isEclipseSpin ? 4 : 2, feature.EclipseAwardPoints)]));
        return new(0, 0, patterns.Sum(line => line.AmountPoints), awards);
    }
}
