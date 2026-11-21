using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ReelRiches.Server;

internal static class ReelRichesRoundPolicy
{
    private static readonly int BoostCost = ReelRichesEngine.CreateGame().SpecialPoints!.ActivationCost;

    internal static ReelRichesPreparedRound Prepare(string account, ReelRichesRoundRequest request,
        ClassicSlotRoundState before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = ReelRichesSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            before.Energy, mode, before.Pity, request.UseFreeSpin);
        var outcome = engine.Outcome;
        var progress = Advance(before, outcome, spendsLegacy);
        var round = new ReelRichesPreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(progress.SettledPayout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(ReelRichesPreparedRound round)
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

    private static void ValidateRound(ReelRichesPreparedRound round, string? mode, long wagerCents,
        bool spendsLegacy, ReelRichesRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.Audit;
        if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != ReelRichesEngine.GameId ||
            audit.EngineVersion != ReelRichesEngine.EngineVersion || audit.FeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost || audit.EnergyBalanceBefore != round.Before.Energy ||
            audit.PityMissesBefore != round.Before.Pity || audit.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            audit.GeneratedAtUtc < round.StartedAtUtc || round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.PointValueInCents != ReelRichesEngine.WagerPointValueInCents || outcome.Payout.TotalPoints < 0 ||
            progress.SettledPayout.TotalPoints < 0 ||
            round.PayoutCents != ToCents(progress.SettledPayout.TotalPoints, outcome.PointValueInCents) ||
            !progress.After.Matches(round.Progress.After) ||
            progress.CollectionFreeSpinsAwarded != round.Progress.CollectionFreeSpinsAwarded ||
            progress.AwardedFeatureMode != round.Progress.AwardedFeatureMode ||
            progress.CollectionFreeSpinWagerPoints != round.Progress.CollectionFreeSpinWagerPoints ||
            JsonSerializer.Serialize(progress.EnergySettlement, RoundAuditCapture.Json) !=
                JsonSerializer.Serialize(round.Progress.EnergySettlement, RoundAuditCapture.Json) ||
            round.OutcomeSha256 != OutcomeDigest(outcome))
            throw new GameWalletUnavailableException();

        _ = ReelRichesRoundStateCodec.Storage.Write(progress.After);
        ReelRichesSpinEvidence.Validate(round.Engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(ReelRichesRoundRequest request, ClassicSlotRoundState before)
    {
        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new ReelRichesFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new ReelRichesFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } saved && saved < ReelRichesEngine.MinimumWagerPoints)
            throw new ReelRichesLegacyFreeWagerUnplayableException(saved);
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new ReelRichesFreeWagerLockedException(locked);

        // Unpinned historical credits choose a legal cash wager on their first free spin.
        ReelRichesEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        if (request.UseSpecialBoost && before.SpecialPoints < BoostCost)
            throw new ReelRichesInsufficientSpecialPointsException(before.SpecialPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, ReelRichesEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(ClassicSlotRoundState before, bool freeSpin) => freeSpin && before.LegacyRemaining > 0;

    internal static ReelRichesRoundProgress Advance(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome, bool spendsLegacy)
    {
        var profile = ReelRichesEngine.Profile;
        var energy = ClassicSlotEnergy.Settle(before.Energy, outcome.EnergyAwarded, outcome.Payout);
        var current = ClassicSlotCollections.TrackIds.Select(mode => new SlotCollectionState(mode,
            checked((int)before.SealCounts.GetValueOrDefault(mode)), before.SealWagerPoints.GetValueOrDefault(mode))).ToArray();
        var collections = ClassicSlotCollections.Advance(profile, current, outcome.CollectionsAwarded,
            outcome.WagerPoints, energy.MultiplierApplied);
        var awardedMode = (outcome.FreeSpinsAwarded > 0 ? profile.ScatterFeatureMode : null) ?? collections.FeatureMode;
        var legacy = checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0));
        var native = checked(before.NativeRemaining - (outcome.IsFreeSpin && !spendsLegacy ? 1 : 0) +
            outcome.FreeSpinsAwarded + collections.FreeSpinsAwarded);
        if (legacy < 0 || native < 0) throw new GameWalletUnavailableException();
        var remaining = checked(legacy + native);
        var awardedWager = collections.FreeSpinWagerPoints > 0
            ? Math.Max(ReelRichesEngine.MinimumWagerPoints, collections.FreeSpinWagerPoints)
            : outcome.WagerPoints;
        var carry = remaining == 0 ? null : new ClassicSlotRoundCarry(remaining, awardedWager, awardedMode ?? before.Carry?.Mode);
        var after = new ClassicSlotRoundState(native, legacy, carry,
            checked(before.SpecialPoints - (outcome.SpecialBoostApplied ? BoostCost : 0) + outcome.SpecialPointsAwarded),
            energy.FinalEnergyBalance, outcome.ConsecutiveFiveMisses,
            collections.Collections.ToDictionary(track => track.Mode, track => (long)track.Count, StringComparer.Ordinal),
            collections.Collections.ToDictionary(track => track.Mode, track => track.WagerPointTotal, StringComparer.Ordinal));
        return new(after, energy, collections.FreeSpinsAwarded, awardedMode, collections.FreeSpinWagerPoints);
    }

    internal static string OutcomeDigest(ClassicSlotSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
