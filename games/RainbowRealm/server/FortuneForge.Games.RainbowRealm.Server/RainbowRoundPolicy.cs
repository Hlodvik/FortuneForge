using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RainbowRealm.Server;

internal static class RainbowRoundPolicy
{
    private static readonly int BoostCost = RainbowRealmEngine.CreateGame().SpecialPoints!.ActivationCost;

    internal static RainbowPreparedRound Prepare(string account, RainbowRoundRequest request,
        RainbowRoundBefore before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = RainbowSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            mode, before.Pity, request.UseFreeSpin);
        var outcome = engine.Outcome;
        var progress = Advance(before, outcome, spendsLegacy);
        var round = new RainbowPreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(RainbowPreparedRound round)
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

    private static void ValidateRound(RainbowPreparedRound round, string? mode, long wagerCents, bool spendsLegacy,
        RainbowRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.Audit;
        if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != RainbowRealmEngine.GameId ||
            audit.EngineVersion != RainbowRealmEngine.EngineVersion || audit.FeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost || audit.EnergyBalanceBefore != 0 ||
            outcome.EnergyAwarded != 0 || audit.PityMissesBefore != round.Before.Pity ||
            audit.GeneratedAtUtc.Kind != DateTimeKind.Utc || audit.GeneratedAtUtc < round.StartedAtUtc ||
            round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.PointValueInCents != RainbowRealmEngine.WagerPointValueInCents || outcome.Payout.TotalPoints < 0 ||
            round.PayoutCents != ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) ||
            !progress.After.Matches(round.Progress.After) ||
            progress.CollectionFreeSpinsAwarded != round.Progress.CollectionFreeSpinsAwarded ||
            round.OutcomeSha256 != OutcomeDigest(outcome))
            throw new GameWalletUnavailableException();
        _ = RainbowRoundStateCodec.Write(progress.After);
        RainbowSpinEvidence.Validate(round.Engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(RainbowRoundRequest request, RainbowRoundBefore before)
    {
        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new RainbowFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new RainbowFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new RainbowFreeWagerLockedException(locked);

        // Earlier credits without a saved lock permitted the player's current even wager.
        RainbowRealmEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        if (request.UseSpecialBoost && before.SpecialPoints < BoostCost)
            throw new RainbowInsufficientSpecialPointsException(before.SpecialPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, RainbowRealmEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(RainbowRoundBefore before, bool freeSpin) => freeSpin && before.LegacyRemaining > 0;

    internal static RainbowRoundProgress Advance(RainbowRoundBefore before, ClassicSlotSpinOutcome outcome, bool spendsLegacy)
    {
        var profile = RainbowRealmEngine.Profile;
        var current = ClassicSlotCollections.TrackIds.Select(mode => new SlotCollectionState(mode,
            checked((int)before.SealCounts.GetValueOrDefault(mode)), before.SealWagerPoints.GetValueOrDefault(mode))).ToArray();
        var collections = ClassicSlotCollections.Advance(profile, current, outcome.CollectionsAwarded, outcome.WagerPoints,
            energyCompleted: false);
        var legacy = checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0));
        var native = checked(before.NativeRemaining - (outcome.IsFreeSpin && !spendsLegacy ? 1 : 0) +
            outcome.FreeSpinsAwarded + collections.FreeSpinsAwarded);
        if (legacy < 0 || native < 0) throw new GameWalletUnavailableException();
        var remaining = checked(legacy + native);
        var scatterMode = outcome.FreeSpinsAwarded > 0 ? profile.ScatterFeatureMode : null;
        var carry = remaining == 0 ? null : new RainbowCarry(remaining,
            collections.FreeSpinWagerPoints > 0 ? collections.FreeSpinWagerPoints : outcome.WagerPoints,
            scatterMode ?? collections.FeatureMode ?? before.Carry?.Mode);
        var after = new RainbowRoundBefore(native, legacy, carry,
            checked(before.SpecialPoints - (outcome.SpecialBoostApplied ? BoostCost : 0) + outcome.SpecialPointsAwarded),
            outcome.ConsecutiveFiveMisses,
            collections.Collections.ToDictionary(track => track.Mode, track => (long)track.Count, StringComparer.Ordinal),
            collections.Collections.ToDictionary(track => track.Mode, track => track.WagerPointTotal, StringComparer.Ordinal));
        return new(after, collections.FreeSpinsAwarded);
    }

    internal static string OutcomeDigest(ClassicSlotSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
