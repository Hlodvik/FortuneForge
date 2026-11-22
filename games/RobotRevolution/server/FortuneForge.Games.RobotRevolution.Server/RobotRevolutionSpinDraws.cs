using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution.Server;

internal sealed record RobotRevolutionRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class RobotRevolutionSpinEvidence
{
    internal static RobotRevolutionRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new RobotRevolutionEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(RobotRevolutionRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new RobotRevolutionEngine(replay, audit.EngineVersion).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (RobotRevolutionRoundPolicy.OutcomeDigest(reproduced) != RobotRevolutionRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Fifth Sun’s recorded draws do not reproduce its prepared outcome.");
    }
}
