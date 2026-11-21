using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.DesertTreasures.Server;

internal sealed record DesertTreasuresRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class DesertTreasuresSpinEvidence
{
    internal static DesertTreasuresRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new DesertTreasuresEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(DesertTreasuresRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new DesertTreasuresEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (DesertTreasuresRoundPolicy.OutcomeDigest(reproduced) != DesertTreasuresRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Desert Treasures’ recorded draws do not reproduce its prepared outcome.");
    }
}
