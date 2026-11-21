using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PiratesFortune.Server;

internal sealed record PiratesRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class PiratesSpinEvidence
{
    internal static PiratesRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new PiratesFortuneEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(PiratesRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new PiratesFortuneEngine(replay).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (PiratesRoundPolicy.OutcomeDigest(reproduced) != PiratesRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Pirates’ Fortune’s recorded draws do not reproduce its prepared outcome.");
    }
}
