using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry.Server;

internal static class GoldFoundryRoundPolicy
{
    internal static GoldFoundryPreparedRound Prepare(string account, GoldFoundryRoundRequest request,
        GoldFoundryRoundBefore before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var engine = new GoldFoundryEngine(random);
        var outcome = engine.Spin(request.WagerPoints, account, request.UseSpecialBoost, before.Energy,
            persistedPityMisses: before.Pity, isFreeSpin: request.UseFreeSpin);
        var wagerCents = ToCents(request.WagerPoints, outcome.PointValueInCents);
        var crucible = GoldFoundryCrucibleRules.Settle(before.Energy, outcome.EnergyAwarded, before.CrucibleWageredCents, wagerCents);
        var payoutCents = checked(ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) + crucible.PayoutCents);
        var round = new GoldFoundryPreparedRound(Guid.NewGuid(), account, request, before, outcome, crucible,
            OutcomeDigest(outcome), wagerCents, payoutCents, started, observation);
        Validate(round);
        return round;
    }

    internal static void Validate(GoldFoundryPreparedRound round)
    {
        var outcome = round.Outcome;
        try
        {
            var wagerCents = ToCents(round.Request.WagerPoints, outcome.PointValueInCents);
            var expected = GoldFoundryCrucibleRules.Settle(round.Before.Energy, outcome.EnergyAwarded,
                round.Before.CrucibleWageredCents, wagerCents);
            if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty ||
                round.Request.UseFreeSpin || round.Request.UseSpecialBoost ||
                outcome.GameId != GoldFoundryEngine.GameId || outcome.WagerPoints != round.Request.WagerPoints ||
                outcome.EngineAudit.PityMissesBefore != round.Before.Pity || outcome.EngineAudit.EnergyBalanceBefore != round.Before.Energy ||
                outcome.EngineAudit.GeneratedAtUtc.Kind != DateTimeKind.Utc || outcome.EngineAudit.GeneratedAtUtc < round.StartedAtUtc ||
                wagerCents <= 0 || round.WagerCents != wagerCents || expected != round.Crucible ||
                outcome.Payout.TotalPoints < 0 ||
                round.PayoutCents != checked(ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) + expected.PayoutCents) ||
                round.OutcomeSha256 != OutcomeDigest(outcome) || GoldFoundryOutcomeReplay.Check(outcome, outcome.Payout).Count != 0)
                throw new GameWalletUnavailableException();
            _ = GoldFoundryRoundStateCodec.Write(round.After);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }

    internal static string OutcomeDigest(GoldFoundrySpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
