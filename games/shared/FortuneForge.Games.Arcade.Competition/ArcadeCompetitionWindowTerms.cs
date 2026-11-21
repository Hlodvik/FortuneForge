using System.Collections.Immutable;
using System.Globalization;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Arcade.Competition;

internal static class ArcadeCompetitionWindowTerms
{
    internal const string Field = "windowTerms";

    internal static ArcadeCompetitionRulesOptions Read(DocumentSnapshot snapshot, ArcadeCompetitionRulesOptions fallback)
    {
        if (!snapshot.Exists || !snapshot.ContainsField(Field)) return fallback;
        if (snapshot.ToDictionary().GetValueOrDefault(Field) is not IReadOnlyDictionary<string, object> data ||
            !Equals(data.GetValueOrDefault("schemaVersion"), 1L) ||
            data.GetValueOrDefault("entryFeeCents") is not long fee ||
            data.GetValueOrDefault("houseCutCapBasisPoints") is not long cap || cap is < 0 or > 10_000 ||
            data.GetValueOrDefault("prizeSchedules") is not IReadOnlyDictionary<string, object> schedules)
            throw InvalidTerms();

        var decoded = ImmutableDictionary.CreateBuilder<int, ImmutableArray<int>>();
        foreach (var (key, value) in schedules)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var places) ||
                value is not IEnumerable<object> array || decoded.ContainsKey(places))
                throw InvalidTerms();

            var allocations = ImmutableArray.CreateBuilder<int>();
            foreach (var item in array)
            {
                if (item is not long share || share is < 0 or > 10_000) throw InvalidTerms();
                allocations.Add((int)share);
            }
            decoded.Add(places, allocations.ToImmutable());
        }
        var rules = fallback with { EntryFeeCents = fee, HouseCutCapBasisPoints = (int)cap, PrizeSchedules = decoded.ToImmutable() };
        ArcadeCompetitionRules.ValidateOptions(rules);
        return rules;
    }

    internal static Dictionary<string, object> Encode(ArcadeCompetitionRulesOptions rules)
    {
        ArcadeCompetitionRules.ValidateOptions(rules);
        return new()
        {
            ["schemaVersion"] = 1L,
            ["entryFeeCents"] = rules.EntryFeeCents,
            ["houseCutCapBasisPoints"] = (long)rules.HouseCutCapBasisPoints,
            ["prizeSchedules"] = rules.PrizeSchedules.ToDictionary(
                pair => pair.Key.ToString(CultureInfo.InvariantCulture),
                pair => (object)pair.Value.Select(share => (long)share).ToArray(), StringComparer.Ordinal),
        };
    }

    private static InvalidOperationException InvalidTerms() => new("The stored arcade competition window terms are invalid.");
}
