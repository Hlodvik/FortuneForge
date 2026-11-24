namespace FortuneForge.Games.FrontierFortune;

public sealed record FrontierTarget(int Index, string Kind, int HitPoints, int MaxHitPoints, long RewardPoints);
public sealed record FrontierShotPreview(int TargetIndex, int[] HitIndices, int[] ClearedIndices, long AwardPoints,
    int ShotsRestored, int ShotsAfter, bool FinishesBonus);
public sealed record FrontierBonus(string BonusId, long WagerPoints, int ShotsRemaining, long CollectedPoints,
    bool Completed, FrontierTarget[] Cells, FrontierShotPreview[] Previews);
public sealed record FrontierBonusTransition(FrontierBonus Bonus, FrontierShotPreview? Shot, long PayoutPoints);

/// <summary>Every target and consequence is visible; a shot is one immutable, deterministic transition.</summary>
public static class FrontierBounties
{
    public static FrontierBonus Create(string bonusId, long wager, IReadOnlyList<string> kinds)
    {
        FrontierRules.ValidateWager(wager);
        var rules = FrontierRules.Definition.Bonus;
        if (!Guid.TryParseExact(bonusId, "N", out var id) || id == Guid.Empty || kinds.Count != 15)
            throw new ArgumentException("The bounty board identity is invalid.");
        var cells = kinds.Select((kind, index) =>
        {
            var rule = rules.Targets.SingleOrDefault(x => x.Kind == kind) ?? throw new ArgumentException("Unknown target.");
            return new FrontierTarget(index, kind, rule.HitPoints, rule.HitPoints, checked(wager * rule.RewardMultiplier));
        }).ToArray();
        var board = new FrontierBonus(bonusId, wager, rules.StartingShots, 0, false, cells, []);
        Validate(board);
        return WithPreviews(board);
    }

    public static FrontierBonusTransition Shoot(FrontierBonus bonus, int targetIndex)
    {
        Validate(bonus);
        if (bonus.Completed || bonus.ShotsRemaining <= 0 || targetIndex is < 0 or >= 15 || bonus.Cells[targetIndex].HitPoints == 0)
            throw new ArgumentException("Choose an uncleared target on the active bounty board.");
        var (cells, preview) = Calculate(bonus, targetIndex);
        var next = bonus with { Cells = cells, ShotsRemaining = preview.ShotsAfter,
            CollectedPoints = checked(bonus.CollectedPoints + preview.AwardPoints), Completed = preview.FinishesBonus, Previews = [] };
        next = WithPreviews(next);
        return new(next, preview, next.Completed ? next.CollectedPoints : 0);
    }

    public static FrontierBonusTransition Bank(FrontierBonus bonus)
    {
        Validate(bonus);
        if (bonus.Completed) throw new ArgumentException("That bounty has already been banked.");
        return new(bonus with { Completed = true, Previews = [] }, null, bonus.CollectedPoints);
    }

    public static void Validate(FrontierBonus bonus)
    {
        FrontierRules.ValidateWager(bonus.WagerPoints);
        var rules = FrontierRules.Definition.Bonus;
        if (!Guid.TryParseExact(bonus.BonusId, "N", out var id) || id == Guid.Empty || bonus.Cells is not { Length: 15 } ||
            bonus.ShotsRemaining < 0 || bonus.ShotsRemaining > rules.MaximumShots || !bonus.Completed && bonus.ShotsRemaining == 0)
            throw new ArgumentException("The saved bounty board is invalid.");
        foreach (var rule in rules.Targets)
            if (bonus.Cells.Count(x => x.Kind == rule.Kind) != rule.Count) throw new ArgumentException("The saved targets are invalid.");
        foreach (var (cell, index) in bonus.Cells.Select((cell, index) => (cell, index)))
        {
            var rule = rules.Targets.SingleOrDefault(x => x.Kind == cell.Kind);
            if (rule is null || cell.Index != index || cell.MaxHitPoints != rule.HitPoints || cell.HitPoints < 0 ||
                cell.HitPoints > cell.MaxHitPoints || cell.RewardPoints != bonus.WagerPoints * rule.RewardMultiplier)
                throw new ArgumentException("The saved target values are invalid.");
        }
        if (bonus.CollectedPoints != bonus.Cells.Where(x => x.HitPoints == 0).Sum(x => x.RewardPoints))
            throw new ArgumentException("The saved bounty total is invalid.");
    }

    public static FrontierBonus WithPreviews(FrontierBonus bonus) => bonus with
    {
        Previews = bonus.Completed ? [] : bonus.Cells.Where(x => x.HitPoints > 0)
            .Select(x => Calculate(bonus, x.Index).Preview).ToArray()
    };

    private static (FrontierTarget[] Cells, FrontierShotPreview Preview) Calculate(FrontierBonus bonus, int targetIndex)
    {
        var cells = bonus.Cells.ToArray();
        var pending = new Queue<int>();
        var visited = new HashSet<int>();
        List<int> hits = [], cleared = [];
        long award = 0;
        var supplies = 0;
        pending.Enqueue(targetIndex);
        while (pending.TryDequeue(out var index))
        {
            if (!visited.Add(index) || cells[index].HitPoints == 0) continue;
            var cell = cells[index];
            cells[index] = cell with { HitPoints = cell.HitPoints - 1 };
            hits.Add(index);
            if (cells[index].HitPoints > 0) continue;
            cleared.Add(index);
            award = checked(award + cell.RewardPoints);
            if (cell.Kind == "supplies") supplies++;
            if (cell.Kind != "ricochet") continue;
            var column = index % 5;
            var row = index / 5;
            if (column > 0) pending.Enqueue(index - 1);
            if (column < 4) pending.Enqueue(index + 1);
            if (row > 0) pending.Enqueue(index - 5);
            if (row < 2) pending.Enqueue(index + 5);
        }
        var shots = Math.Min(FrontierRules.Definition.Bonus.MaximumShots,
            bonus.ShotsRemaining - 1 + supplies * FrontierRules.Definition.Bonus.SupplyShots);
        return (cells, new(targetIndex, hits.ToArray(), cleared.ToArray(), award,
            shots - (bonus.ShotsRemaining - 1), shots, shots == 0 || cells.Where(x => x.RewardPoints > 0).All(x => x.HitPoints == 0)));
    }
}
