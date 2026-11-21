using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RainbowRealm.Server;

internal sealed record RainbowRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class RainbowSpinEvidence
{
    internal static RainbowRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new RainbowRealmEngine(recording).Spin(wagerPoints, accountId, useBoost, 0, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(RainbowRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new RainbowRealmEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied, 0,
                audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (RainbowRoundPolicy.OutcomeDigest(reproduced) != RainbowRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Rainbow Realm's recorded draws do not reproduce its prepared outcome.");
    }
}
