using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.OceanOdyssey.Server;

internal sealed record OceanOdysseyRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class OceanOdysseySpinEvidence
{
    internal static OceanOdysseyRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new OceanOdysseyEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(OceanOdysseyRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new OceanOdysseyEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (OceanOdysseyRoundPolicy.OutcomeDigest(reproduced) != OceanOdysseyRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Ocean Odyssey’ recorded draws do not reproduce its prepared outcome.");
    }
}
