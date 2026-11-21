using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault.Server;

internal static class MidnightVaultRoundPolicy
{
    internal static MidnightVaultPreparedRound Prepare(string account, MidnightVaultRoundRequest request,
        MidnightVaultRoundBefore before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (model, mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = new MidnightVaultEngine(random);
        var outcome = engine.Spin(request.WagerPoints, account, request.UseSpecialBoost, before.Energy, mode,
            before.Pity, request.UseFreeSpin, model);
        var progress = Advance(before, outcome, spendsLegacy);
        var round = new MidnightVaultPreparedRound(Guid.NewGuid(), account, request, before, outcome, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, model, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(MidnightVaultPreparedRound round)
    {
        try
        {
            var (model, mode, wagerCents, spendsLegacy) = Terms(round.Request, round.Before);
            var progress = Advance(round.Before, round.Outcome, spendsLegacy);
            ValidateRound(round, model, mode, wagerCents, spendsLegacy, progress);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }

    private static void ValidateRound(MidnightVaultPreparedRound round, string model, string? mode, long wagerCents,
        bool spendsLegacy, MidnightVaultRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.EngineAudit;
        if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != MidnightVaultEngine.GameId ||
            outcome.PaytableId != model || audit.FreeSpinFeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost || audit.EnergyBalanceBefore != round.Before.Energy ||
            audit.PityMissesBefore != round.Before.Pity || audit.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            audit.GeneratedAtUtc < round.StartedAtUtc || round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.Payout.TotalPoints < 0 || round.PayoutCents != ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) ||
            !progress.After.Matches(round.Progress.After) ||
            progress.CollectionFreeSpinsAwarded != round.Progress.CollectionFreeSpinsAwarded ||
            !SameEvidence(progress.BonusEvidence, round.Progress.BonusEvidence) ||
            round.OutcomeSha256 != OutcomeDigest(outcome) ||
            MidnightVaultOutcomeReplay.Check(outcome, outcome.Payout).Count != 0)
            throw new GameWalletUnavailableException();
        _ = MidnightVaultRoundStateCodec.Write(progress.After);
    }

    private static (string Model, string? Mode, long WagerCents, bool SpendsLegacy) Terms(
        MidnightVaultRoundRequest request, MidnightVaultRoundBefore before)
    {
        var head = before.Rounds.FirstOrDefault();
        if (request.UseFreeSpin && head is null) throw new MidnightVaultFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && head is not null) throw new MidnightVaultFreeSpinsRequiredException();
        if (head is not null && request.WagerPoints != head.WagerPoints)
            throw new MidnightVaultFreeWagerLockedException(head.WagerPoints);
        var model = head?.MathModelId ?? MidnightVaultSlotDefinitions.PaytableId;
        var game = MidnightVaultEngine.CreateGame(model);
        var boostCost = game.SpecialPoints!.ActivationCost;
        if (request.UseSpecialBoost && before.SpecialPoints < boostCost)
            throw new MidnightVaultInsufficientSpecialPointsException(before.SpecialPoints, boostCost);
        if (request.UseFreeSpin) MidnightVaultBonusPolicy.ValidateDerivedWager(request.WagerPoints);
        else MidnightVaultBonusPolicy.ValidateCashWager(request.WagerPoints);
        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, game.Wagering.PointValueInCents);
        return (model, head?.FeatureMode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(MidnightVaultRoundBefore before, bool useFreeSpin) =>
        useFreeSpin && before.LegacyRemaining > 0;

    internal static MidnightVaultRoundProgress Advance(MidnightVaultRoundBefore before, MidnightVaultSpinOutcome outcome,
        bool spendsLegacy)
    {
        var admitted = outcome.IsFreeSpin ? before.Rounds.FirstOrDefault() : null;
        var afterConsumption = outcome.IsFreeSpin ? MidnightVaultBonusRounds.Consume(before.Rounds) : before.Rounds;
        var bonus = MidnightVaultBonusRounds.Advance(afterConsumption, before.SealCounts, before.SealWagerPoints,
            outcome.SealsAwarded, outcome.WagerPoints, outcome.FreeSpinsAwarded,
            outcome.EngineAudit.FeatureProfile.ScatterFeatureMode, outcome.PaytableId, outcome.SpinId);
        var boostCost = outcome.SpecialBoostApplied ? outcome.EngineAudit.BaseGame.SpecialPoints!.ActivationCost : 0;
        // The legacy meter may affect the admitted outcome, but Midnight's settlement has no energy multiplier or awards.
        var after = new MidnightVaultRoundBefore(bonus.Rounds, checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0)),
            checked(before.SpecialPoints - boostCost + outcome.SpecialPointsAwarded), 0, outcome.ConsecutiveFiveMisses,
            bonus.CollectionCounts, bonus.CollectionWagerPoints);
        var evidence = new MidnightVaultBonusEvidence(admitted, before.Rounds, afterConsumption, after.Rounds,
            before.SealCounts, before.SealWagerPoints, after.SealCounts, after.SealWagerPoints, bonus.AwardedRounds);
        var collections = bonus.AwardedRounds.Where(round => round.Id.Contains(":collection:", StringComparison.Ordinal))
            .Sum(round => round.TotalSpins);
        return new(after, evidence, collections);
    }

    internal static string OutcomeDigest(MidnightVaultSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));

    private static bool SameEvidence(MidnightVaultBonusEvidence left, MidnightVaultBonusEvidence right) =>
        JsonSerializer.Serialize(left, RoundAuditCapture.Json) == JsonSerializer.Serialize(right, RoundAuditCapture.Json);
}
