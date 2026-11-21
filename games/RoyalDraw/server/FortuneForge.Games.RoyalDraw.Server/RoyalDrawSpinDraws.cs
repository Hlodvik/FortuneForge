using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RoyalDraw.Server;

internal sealed record RoyalDrawRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class RoyalDrawSpinEvidence
{
    internal static RoyalDrawRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new RoyalDrawEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(RoyalDrawRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new RoyalDrawEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (RoyalDrawRoundPolicy.OutcomeDigest(reproduced) != RoyalDrawRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Royal Draw’ recorded draws do not reproduce its prepared outcome.");
    }
}
