using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.Wukong.Server;

internal sealed record WukongRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class WukongSpinEvidence
{
    internal static WukongRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new WukongEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(WukongRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new WukongEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (WukongRoundPolicy.OutcomeDigest(reproduced) != WukongRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Wukong's recorded draws do not reproduce its prepared outcome.");
    }
}
