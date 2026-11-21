using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ReelRiches.Server;

internal sealed record ReelRichesRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class ReelRichesSpinEvidence
{
    internal static ReelRichesRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new ReelRichesEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(ReelRichesRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new ReelRichesEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (ReelRichesRoundPolicy.OutcomeDigest(reproduced) != ReelRichesRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Reel Riches’ recorded draws do not reproduce its prepared outcome.");
    }
}
