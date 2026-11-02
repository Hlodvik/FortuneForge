namespace FortuneForge.Server.Slots.Bonuses;

public sealed record SlotBonusRound(string Id, int TotalSpins, int RemainingSpins,
    long WagerPoints, string? FeatureMode, string MathModelId);

public sealed record CollectedBonusTrackRules(string Id, string SealSymbolId, int Target, string? FeatureMode);

public sealed record SlotBonusModelRules(string Id, int ScatterSpins, int CollectionSpins,
    IReadOnlyList<string> FeatureModes, bool AllowFeatureModeCombinations = false, bool AllowLegacyCarryTotals = false);

public sealed record CollectedBonusRules(long MinimumWagerPoints, long MaximumWagerPoints,
    IReadOnlyList<CollectedBonusTrackRules> Tracks, IReadOnlyList<SlotBonusModelRules> Models);

public sealed record SlotBonusProgress(IReadOnlyList<SlotBonusRound> Rounds,
    IReadOnlyDictionary<string, long> CollectionCounts, IReadOnlyDictionary<string, long> CollectionWagerPoints,
    IReadOnlyList<SlotBonusRound> AwardedRounds);

/// <summary>Deterministic queue and exact native-point collection arithmetic, with caller-supplied rules.</summary>
public static class SlotBonusRounds
{
    public static void Validate(CollectedBonusRules rules, IReadOnlyList<SlotBonusRound> rounds)
    {
        ValidateRules(rules);
        ArgumentNullException.ThrowIfNull(rounds);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var round in rounds)
        {
            if (round is null || string.IsNullOrWhiteSpace(round.Id) || !ids.Add(round.Id) ||
                round.TotalSpins <= 0 || round.RemainingSpins <= 0 || round.RemainingSpins > round.TotalSpins)
                throw new InvalidOperationException("A bonus round identifier or spin count is invalid.");
            ValidateWager(rules, round.WagerPoints);
            var model = Model(rules, round.MathModelId);
            if (!model.AllowLegacyCarryTotals && round.TotalSpins != model.ScatterSpins && round.TotalSpins != model.CollectionSpins)
                throw new InvalidOperationException("The bonus round length differs from its configured model.");
            ValidateMode(model, round.FeatureMode);
        }
    }

    public static IReadOnlyList<SlotBonusRound> Consume(CollectedBonusRules rules, IReadOnlyList<SlotBonusRound> rounds)
    {
        Validate(rules, rounds);
        if (rounds.Count == 0) throw new InvalidOperationException("There is no awarded bonus spin to consume.");
        var head = rounds[0];
        return head.RemainingSpins == 1 ? rounds.Skip(1).ToArray()
            : new[] { head with { RemainingSpins = head.RemainingSpins - 1 } }.Concat(rounds.Skip(1)).ToArray();
    }

    public static SlotBonusProgress Advance(CollectedBonusRules rules, IReadOnlyList<SlotBonusRound> roundsAfterConsumption,
        IReadOnlyDictionary<string, long> counts, IReadOnlyDictionary<string, long> wagerPointTotals,
        IReadOnlyDictionary<string, int> awardedSeals, long spinWagerPoints, int scatterAwardedSpins,
        string? scatterMode, string mathModelId, Guid spinId)
    {
        Validate(rules, roundsAfterConsumption);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(wagerPointTotals);
        ArgumentNullException.ThrowIfNull(awardedSeals);
        ValidateWager(rules, spinWagerPoints);
        var model = Model(rules, mathModelId);
        ValidateMode(model, scatterMode);
        if (spinId == Guid.Empty) throw new ArgumentException("A settled spin identifier is required.", nameof(spinId));
        if (counts.Keys.Concat(wagerPointTotals.Keys).Any(key => !rules.Tracks.Any(track => track.Id == key)) ||
            awardedSeals.Any(pair => pair.Value < 0 || !rules.Tracks.Any(track => track.SealSymbolId == pair.Key)))
            throw new InvalidOperationException("An unknown collection track or invalid seal award was supplied.");
        if (scatterAwardedSpins != 0 && scatterAwardedSpins != model.ScatterSpins)
            throw new InvalidOperationException("The scatter award differs from its configured model.");
        var nextCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var nextTotals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var track in rules.Tracks)
        {
            var count = counts.GetValueOrDefault(track.Id);
            var total = wagerPointTotals.GetValueOrDefault(track.Id);
            if (count < 0 || count >= track.Target || total < checked(count * rules.MinimumWagerPoints) ||
                total > checked(count * rules.MaximumWagerPoints))
                throw new InvalidOperationException("A stored collection count or exact wager total is invalid.");
            nextCounts[track.Id] = count;
            nextTotals[track.Id] = total;
        }
        var awarded = new List<SlotBonusRound>();
        if (scatterAwardedSpins > 0)
            awarded.Add(new($"{spinId}:scatter", model.ScatterSpins, model.ScatterSpins, spinWagerPoints, scatterMode, mathModelId));
        foreach (var track in rules.Tracks)
        {
            long incoming = awardedSeals.GetValueOrDefault(track.SealSymbolId);
            var ordinal = 0;
            while (incoming > 0)
            {
                var take = Math.Min(incoming, track.Target - nextCounts[track.Id]);
                nextCounts[track.Id] = checked(nextCounts[track.Id] + take);
                nextTotals[track.Id] = checked(nextTotals[track.Id] + take * spinWagerPoints);
                incoming -= take;
                if (nextCounts[track.Id] != track.Target) continue;
                var wager = checked((nextTotals[track.Id] + track.Target / 2) / track.Target);
                ValidateWager(rules, wager);
                awarded.Add(new($"{spinId}:collection:{track.Id}:{ordinal++}", model.CollectionSpins, model.CollectionSpins,
                    wager, track.FeatureMode, mathModelId));
                nextCounts[track.Id] = 0;
                nextTotals[track.Id] = 0;
            }
        }
        var rounds = roundsAfterConsumption.Concat(awarded).ToArray();
        Validate(rules, rounds);
        return new(rounds, nextCounts, nextTotals, awarded.ToArray());
    }

    private static void ValidateRules(CollectedBonusRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.MinimumWagerPoints <= 0 || rules.MaximumWagerPoints < rules.MinimumWagerPoints ||
            rules.Tracks is null || rules.Models is null || rules.Models.Count == 0 ||
            rules.Tracks.Any(track => track is null || string.IsNullOrWhiteSpace(track.Id) ||
                string.IsNullOrWhiteSpace(track.SealSymbolId) || track.Target <= 0) ||
            rules.Tracks.Select(track => track.Id).Distinct(StringComparer.Ordinal).Count() != rules.Tracks.Count ||
            rules.Tracks.Select(track => track.SealSymbolId).Distinct(StringComparer.Ordinal).Count() != rules.Tracks.Count ||
            rules.Models.Any(model => model is null || string.IsNullOrWhiteSpace(model.Id) || model.ScatterSpins <= 0 ||
                model.CollectionSpins <= 0 || model.FeatureModes is null || model.FeatureModes.Any(string.IsNullOrWhiteSpace)) ||
            rules.Models.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count() != rules.Models.Count)
            throw new InvalidOperationException("The collected-bonus rules are invalid.");
    }

    private static SlotBonusModelRules Model(CollectedBonusRules rules, string id) =>
        rules.Models.SingleOrDefault(model => model.Id == id)
            ?? throw new InvalidOperationException("The bonus math model is not registered in these rules.");

    private static void ValidateWager(CollectedBonusRules rules, long wager)
    {
        if (wager < rules.MinimumWagerPoints || wager > rules.MaximumWagerPoints)
            throw new ArgumentOutOfRangeException(nameof(wager), "The bonus wager is outside its trusted bounds.");
    }

    private static void ValidateMode(SlotBonusModelRules model, string? mode)
    {
        if (mode is null) return;
        var valid = model.AllowFeatureModeCombinations
            ? !string.IsNullOrWhiteSpace(mode) && mode.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                is { Length: > 0 } parts && parts.All(part => model.FeatureModes.Contains(part, StringComparer.Ordinal))
            : model.FeatureModes.Contains(mode, StringComparer.Ordinal);
        if (!valid) throw new InvalidOperationException("The bonus feature mode is not registered in its model.");
    }
}
