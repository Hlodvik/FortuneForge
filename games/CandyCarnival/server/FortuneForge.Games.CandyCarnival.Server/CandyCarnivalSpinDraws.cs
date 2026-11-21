using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.CandyCarnival.Server;

internal sealed record CandyCarnivalRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class CandyCarnivalSpinEvidence
{
    internal static CandyCarnivalRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new CandyCarnivalEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(CandyCarnivalRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new CandyCarnivalEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (CandyCarnivalRoundPolicy.OutcomeDigest(reproduced) != CandyCarnivalRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Candy Carnival’s recorded draws do not reproduce its prepared outcome.");
    }
}
