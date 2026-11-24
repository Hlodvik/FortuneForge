using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.CosmicFortune;

public sealed record PharaohsAscentFreeSpinBatch(int Remaining, long WagerPoints, string? Mode);

/// <summary>Each earned batch retains the stake and reel power that funded it.</summary>
public static class PharaohsAscentFreeSpins
{
    private static readonly long MaximumWagerPoints = CosmicFortuneEngine.CreateGame().Wagering.MaximumWagerPoints
        ?? throw new InvalidOperationException("The funded wager limit is missing.");
    public static IReadOnlyList<PharaohsAscentFreeSpinBatch> Advance(IReadOnlyList<PharaohsAscentFreeSpinBatch>? before,
        bool usedFreeSpin, IReadOnlyList<PharaohsAscentFreeSpinBatch> awards)
    {
        Validate(before);
        Validate(awards);
        var queue = before?.ToList() ?? [];
        if (usedFreeSpin)
        {
            if (queue.Count == 0) throw new InvalidOperationException("The funded spin queue is empty.");
            if (queue[0].Remaining == 1) queue.RemoveAt(0);
            else queue[0] = queue[0] with { Remaining = queue[0].Remaining - 1 };
        }
        queue.AddRange(awards);
        return queue;
    }

    public static void Validate(IReadOnlyList<PharaohsAscentFreeSpinBatch>? batches)
    {
        if (batches is null) return;
        if (batches.Any(batch => batch is null || batch.Remaining <= 0 || batch.WagerPoints < CosmicFortuneEngine.MinimumWagerPoints ||
            batch.WagerPoints > MaximumWagerPoints ||
            batch.Mode is not null && !SlotProfile.IsFeatureMode(batch.Mode)))
            throw new InvalidOperationException("The saved funded spin queue is invalid.");
        _ = batches.Sum(batch => batch.Remaining);
    }
}
