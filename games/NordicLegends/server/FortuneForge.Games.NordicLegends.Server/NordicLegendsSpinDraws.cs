using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.NordicLegends.Server;

internal sealed record NordicLegendsRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class NordicLegendsSpinEvidence
{
    internal static NordicLegendsRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new NordicLegendsEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(NordicLegendsRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new NordicLegendsEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (NordicLegendsRoundPolicy.OutcomeDigest(reproduced) != NordicLegendsRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Nordic Legends’s recorded draws do not reproduce its prepared outcome.");
    }
}
