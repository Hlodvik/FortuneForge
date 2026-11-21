using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PhantomManor.Server;

internal sealed record PhantomManorRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class PhantomManorSpinEvidence
{
    internal static PhantomManorRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new PhantomManorEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(PhantomManorRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new PhantomManorEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (PhantomManorRoundPolicy.OutcomeDigest(reproduced) != PhantomManorRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Phantom Manor’s recorded draws do not reproduce its prepared outcome.");
    }
}
