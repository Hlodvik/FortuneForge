using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.Wukong.Server;

internal static class WukongRoundPolicy
{
    private static readonly int BoostCost = WukongEngine.CreateGame().SpecialPoints!.ActivationCost;

    internal static WukongPreparedRound Prepare(string account, WukongRoundRequest request,
        WukongRoundBefore before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = WukongSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            before.Energy, mode, before.Pity, request.UseFreeSpin);
        var progress = Advance(before, engine.Outcome, spendsLegacy);
        var outcome = engine.Outcome;
        var round = new WukongPreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(progress.SettledPayout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(WukongPreparedRound round)
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

    private static void ValidateRound(WukongPreparedRound round, string? mode, long wagerCents,
        bool spendsLegacy, WukongRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.Audit;
        if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != WukongEngine.GameId ||
            audit.EngineVersion != WukongEngine.EngineVersion || audit.FeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost || audit.EnergyBalanceBefore != round.Before.Energy ||
            audit.PityMissesBefore != round.Before.Pity || audit.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            audit.GeneratedAtUtc < round.StartedAtUtc || round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.PointValueInCents != WukongEngine.WagerPointValueInCents || outcome.FreeSpinsAwarded != 0 ||
            outcome.Payout.TotalPoints < 0 || progress.SettledPayout.TotalPoints < 0 ||
            round.PayoutCents != ToCents(progress.SettledPayout.TotalPoints, outcome.PointValueInCents) ||
            !progress.After.Matches(round.Progress.After) ||
            progress.CollectionFreeSpinsAwarded != round.Progress.CollectionFreeSpinsAwarded ||
            JsonSerializer.Serialize(progress.EnergySettlement, RoundAuditCapture.Json) !=
                JsonSerializer.Serialize(round.Progress.EnergySettlement, RoundAuditCapture.Json) ||
            round.OutcomeSha256 != OutcomeDigest(outcome))
            throw new GameWalletUnavailableException();
        _ = WukongRoundStateCodec.Write(progress.After);
        WukongSpinEvidence.Validate(round.Engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(WukongRoundRequest request, WukongRoundBefore before)
    {
        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new WukongFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new WukongFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new WukongFreeWagerLockedException(locked);
        // A migrated balance with no saved lock originally accepted the player's current even wager.
        WukongEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        if (request.UseSpecialBoost && before.SpecialPoints < BoostCost)
            throw new WukongInsufficientSpecialPointsException(before.SpecialPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, WukongEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(WukongRoundBefore before, bool freeSpin) => freeSpin && before.LegacyRemaining > 0;

    internal static WukongRoundProgress Advance(WukongRoundBefore before, ClassicSlotSpinOutcome outcome, bool spendsLegacy)
    {
        var energy = ClassicSlotEnergy.Settle(before.Energy, outcome.EnergyAwarded, outcome.Payout);
        var current = ClassicSlotCollections.TrackIds.Select(mode => new SlotCollectionState(mode,
            checked((int)before.SealCounts.GetValueOrDefault(mode)), before.SealWagerPoints.GetValueOrDefault(mode))).ToArray();
        var collections = ClassicSlotCollections.Advance(WukongEngine.Profile, current, outcome.CollectionsAwarded,
            outcome.WagerPoints, energy.MultiplierApplied);

        var legacy = checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0));
        var native = checked(before.NativeRemaining - (outcome.IsFreeSpin && !spendsLegacy ? 1 : 0) + collections.FreeSpinsAwarded);
        if (legacy < 0 || native < 0) throw new GameWalletUnavailableException();
        var remaining = checked(legacy + native);
        var carry = remaining == 0 ? null : new WukongCarry(remaining,
            collections.FreeSpinWagerPoints > 0 ? collections.FreeSpinWagerPoints : outcome.WagerPoints,
            collections.FeatureMode ?? before.Carry?.Mode);
        var after = new WukongRoundBefore(native, legacy, carry,
            checked(before.SpecialPoints - (outcome.SpecialBoostApplied ? BoostCost : 0) + outcome.SpecialPointsAwarded),
            energy.FinalEnergyBalance, outcome.ConsecutiveFiveMisses,
            collections.Collections.ToDictionary(track => track.Mode, track => (long)track.Count, StringComparer.Ordinal),
            collections.Collections.ToDictionary(track => track.Mode, track => track.WagerPointTotal, StringComparer.Ordinal));
        return new(after, energy, collections.FreeSpinsAwarded);
    }

    internal static string OutcomeDigest(ClassicSlotSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
