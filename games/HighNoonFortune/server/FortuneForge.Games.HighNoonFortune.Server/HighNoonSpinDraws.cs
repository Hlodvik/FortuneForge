using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.HighNoonFortune.Server;

internal sealed record HighNoonRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class HighNoonSpinEvidence
{
    internal static HighNoonRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new HighNoonFortuneEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(HighNoonRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new HighNoonFortuneEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (HighNoonRoundPolicy.OutcomeDigest(reproduced) != HighNoonRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("High Noon Fortune’s recorded draws do not reproduce its prepared outcome.");
    }
}
