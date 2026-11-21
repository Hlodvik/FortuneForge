using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.SamuraiFortune.Server;

internal sealed record SamuraiFortuneRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class SamuraiFortuneSpinEvidence
{
    internal static SamuraiFortuneRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new SamuraiFortuneEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(SamuraiFortuneRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new SamuraiFortuneEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (SamuraiFortuneRoundPolicy.OutcomeDigest(reproduced) != SamuraiFortuneRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Samurai Fortune’ recorded draws do not reproduce its prepared outcome.");
    }
}
