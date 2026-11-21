using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PiratesFortune.Server;

internal static class PiratesRoundPolicy
{
    private static readonly int BoostCost = PiratesFortuneEngine.CreateGame().SpecialPoints!.ActivationCost;
    private static readonly long MinimumWager = PiratesFortuneEngine.CreateGame().Wagering.MinimumWagerPoints;

    internal static PiratesPreparedRound Prepare(string account, PiratesRoundRequest request,
        PiratesRoundBefore before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = PiratesSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            before.Energy, mode, before.Pity, request.UseFreeSpin);
        var outcome = engine.Outcome;
        var progress = Advance(before, outcome, spendsLegacy);
        var round = new PiratesPreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(PiratesPreparedRound round)
    {
        try
        {
            var (mode, wagerCents, spendsLegacy) = Terms(round.Request, round.Before);
            var progress = Advance(round.Before, round.Outcome, spendsLegacy);
            ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }

    private static void ValidateRound(PiratesPreparedRound round, string? mode, long wagerCents, bool spendsLegacy,
        PiratesRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.Audit;
        if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != PiratesFortuneEngine.GameId ||
            audit.EngineVersion != PiratesFortuneEngine.EngineVersion || audit.FeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost || audit.EnergyBalanceBefore != round.Before.Energy ||
            outcome.EnergyAwarded != 0 || audit.PityMissesBefore != round.Before.Pity ||
            audit.GeneratedAtUtc.Kind != DateTimeKind.Utc || audit.GeneratedAtUtc < round.StartedAtUtc ||
            round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.PointValueInCents != PiratesFortuneEngine.WagerPointValueInCents || outcome.Payout.TotalPoints < 0 ||
            round.PayoutCents != ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) ||
            !progress.After.Matches(round.Progress.After) ||
            progress.CollectionFreeSpinsAwarded != round.Progress.CollectionFreeSpinsAwarded ||
            progress.AwardedFeatureMode != round.Progress.AwardedFeatureMode ||
            progress.CollectionFreeSpinWagerPoints != round.Progress.CollectionFreeSpinWagerPoints ||
            !progress.GemAwards.SequenceEqual(round.Progress.GemAwards) ||
            round.OutcomeSha256 != OutcomeDigest(outcome))
            throw new GameWalletUnavailableException();
        _ = PiratesRoundStateCodec.Write(progress.After);
        PiratesSpinEvidence.Validate(round.Engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(PiratesRoundRequest request, PiratesRoundBefore before)
    {
        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new PiratesFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new PiratesFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } saved && saved < MinimumWager)
            throw new PiratesLegacyFreeWagerUnplayableException(saved);
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new PiratesFreeWagerLockedException(locked);

        // The legacy paid path used the current even wager when no carry lock was saved.
        PiratesFortuneEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        if (request.UseSpecialBoost && before.SpecialPoints < BoostCost)
            throw new PiratesInsufficientSpecialPointsException(before.SpecialPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, PiratesFortuneEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(PiratesRoundBefore before, bool freeSpin) => freeSpin && before.LegacyRemaining > 0;

    internal static PiratesRoundProgress Advance(PiratesRoundBefore before, ClassicSlotSpinOutcome outcome, bool spendsLegacy)
    {
        var profile = PiratesFortuneEngine.Profile;
        var pointValue = outcome.PointValueInCents;
        var centsBefore = before.SealWagerPoints.ToDictionary(pair => pair.Key,
            pair => ToCents(pair.Value, pointValue), StringComparer.Ordinal);
        var gems = PiratesGemBonus.AdvancePaid(outcome.SpinId, before.SealCounts, centsBefore, outcome.CollectionsAwarded,
            ToCents(outcome.WagerPoints, pointValue), pointValue, profile.CollectionAwardedSpins,
            outcome.FreeSpinsAwarded > 0 ? profile.ScatterFeatureMode : null);
        var legacy = checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0));
        var native = checked(before.NativeRemaining - (outcome.IsFreeSpin && !spendsLegacy ? 1 : 0) +
            outcome.FreeSpinsAwarded + gems.FreeSpinsAwarded);
        if (legacy < 0 || native < 0) throw new GameWalletUnavailableException();
        var remaining = checked(legacy + native);
        // A legacy track with incomplete wager history can prove a grant but produce a subminimum average.
        var awardedWager = gems.FreeSpinWagerPoints > 0 ? Math.Max(MinimumWager, gems.FreeSpinWagerPoints) : outcome.WagerPoints;
        var carry = remaining == 0 ? null : new PiratesCarry(remaining,
            awardedWager, gems.FeatureMode ?? before.Carry?.Mode);
        // Earlier account state can still carry energy for audit, but this model never uses a meter at settlement.
        var after = new PiratesRoundBefore(native, legacy, carry,
            checked(before.SpecialPoints - (outcome.SpecialBoostApplied ? BoostCost : 0) + outcome.SpecialPointsAwarded),
            0, outcome.ConsecutiveFiveMisses, gems.Counts,
            gems.WagerCents.ToDictionary(pair => pair.Key, pair => checked((long)(pair.Value / pointValue)), StringComparer.Ordinal));
        return new(after, gems.FreeSpinsAwarded, gems.FeatureMode, gems.FreeSpinWagerPoints, gems.Grants);
    }

    internal static string OutcomeDigest(ClassicSlotSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
