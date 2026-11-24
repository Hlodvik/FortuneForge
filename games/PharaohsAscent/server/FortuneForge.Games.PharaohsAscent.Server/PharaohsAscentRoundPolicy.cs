using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent.Server;

internal static class PharaohsAscentRoundPolicy
{
    private static readonly int BoostCost = PharaohsAscentEngine.CreateGame().SpecialPoints!.ActivationCost;

    internal static PharaohsAscentPreparedRound Prepare(string account, PharaohsAscentRoundRequest request,
        ClassicSlotRoundState before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        if (request.TombAction is not null)
            return SealedTombRoundPolicy.Prepare(account, request, before, started, observation, random);
        if (!request.UseFreeSpin) PharaohsAscentWagering.ValidateNewPaidWager(request.WagerPoints);
        var version = EngineVersionForSpin(before, request.UseFreeSpin);
        var (mode, wagerCents, spendsLegacy) = Terms(request, before, version);
        var engine = PharaohsAscentSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            EnergyBeforeSpin(before, request.WagerPoints, version), mode,
            PityBeforeSpin(before, request.WagerPoints, version), request.UseFreeSpin, version);
        var outcome = engine.Outcome;
        var progress = Advance(before, outcome, spendsLegacy);
        var round = new PharaohsAscentPreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(progress.SettledPayout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(PharaohsAscentPreparedRound round)
    {
        try
        {
            if (round.Request.TombAction is not null)
            {
                SealedTombRoundPolicy.Validate(round);
                return;
            }
            var (mode, wagerCents, spendsLegacy) = Terms(round.Request, round.Before, round.Outcome.Audit.EngineVersion);
            var progress = Advance(round.Before, round.Outcome, spendsLegacy);
            ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }

    private static void ValidateRound(PharaohsAscentPreparedRound round, string? mode, long wagerCents,
        bool spendsLegacy, ClassicSlotActiveRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.Audit;
        if (round.TombEvidence is not null || round.Engine is null || round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != PharaohsAscentEngine.GameId ||
            !PharaohsAscentEngine.SupportsEngineVersion(audit.EngineVersion) || audit.FeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost ||
            audit.EnergyBalanceBefore != EnergyBeforeSpin(round.Before, round.Request.WagerPoints, audit.EngineVersion) ||
            audit.PityMissesBefore != PityBeforeSpin(round.Before, round.Request.WagerPoints, audit.EngineVersion) || audit.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            audit.GeneratedAtUtc < round.StartedAtUtc || round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.PointValueInCents != PharaohsAscentEngine.WagerPointValueInCents || outcome.Payout.TotalPoints < 0 ||
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

        _ = PharaohsAscentRoundStateCodec.Storage.Write(progress.After);
        PharaohsAscentSpinEvidence.Validate(round.Engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(PharaohsAscentRoundRequest request,
        ClassicSlotRoundState before, string engineVersion)
    {
        var tomb = SealedTombState.Parse(before.NativeStateJson);
        ValidateFundedQueue(before, tomb);
        if (tomb.ActiveRun is not null || tomb.LastRun is not null)
            throw new SealedTombConflictException("tomb-expedition-active", "Finish The Sealed Tomb and review its treasure before spinning the reels.");
        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new PharaohsAscentFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new PharaohsAscentFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } saved && saved < PharaohsAscentEngine.MinimumWagerPoints)
            throw new PharaohsAscentLegacyFreeWagerUnplayableException(saved);
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new PharaohsAscentFreeWagerLockedException(locked);

        // Unpinned historical credits choose a legal cash wager on their first free spin.
        PharaohsAscentEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        var boostPoints = BoostPointsAvailable(before, request.WagerPoints, engineVersion);
        if (request.UseSpecialBoost && boostPoints < BoostCost)
            throw new PharaohsAscentInsufficientSpecialPointsException(boostPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, PharaohsAscentEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(ClassicSlotRoundState before, bool freeSpin) =>
        ClassicSlotRoundProgression.SpendsLegacy(before, freeSpin);

    internal static ClassicSlotActiveRoundProgress Advance(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome, bool spendsLegacy)
    {
        var current = outcome.Audit.EngineVersion == PharaohsAscentEngine.EngineVersion;
        var savedTomb = SealedTombState.Parse(before.NativeStateJson);
        var sunfire = current ? PharaohsAscentSunfire.Settle(before.Energy, outcome.EnergyAwarded,
            outcome.WagerPoints, outcome.Payout, savedTomb.Sunfire) : null;
        var progress = ClassicSlotRoundProgression.AdvanceEnergyAndCollections(before, outcome,
            PharaohsAscentEngine.ProfileForVersion(outcome.Audit.EngineVersion), BoostCost,
            PharaohsAscentEngine.MinimumWagerPoints, spendsLegacy, sunfire?.Settlement,
            sunfire?.CompletionWagerPoints, roundFundedWagerDown: current);
        if (!PharaohsAscentEngine.UsesSealedTombFunding(outcome.Audit.EngineVersion)) return progress;
        var tomb = SealedTombRules.Accrue(savedTomb, outcome.SpinId,
            outcome.WagerPoints, outcome.IsFreeSpin, outcome.Reels.Sum(reel => reel.Count(symbol => symbol == PharaohsAscentScarabs.SymbolId)),
            outcome.Audit.EngineVersion);
        if (current)
        {
            var boostFunding = PharaohsAscentBoosts.AfterSpin(savedTomb, outcome.WagerPoints,
                before.SpecialPoints, outcome.SpecialBoostApplied, outcome.SpecialPointsAwarded);
            if (PharaohsAscentBoosts.Total(boostFunding) != progress.After.SpecialPoints)
                throw new InvalidOperationException("The funded boost credits disagree with the settled point balance.");
            var collections = ClassicSlotCollections.Advance(PharaohsAscentEngine.Profile,
                ClassicSlotCollections.TrackIds.Select(mode => new SlotCollectionState(mode,
                    checked((int)before.SealCounts.GetValueOrDefault(mode)), before.SealWagerPoints.GetValueOrDefault(mode))).ToArray(),
                outcome.CollectionsAwarded, outcome.WagerPoints, sunfire!.Settlement.MultiplierApplied,
                sunfire.CompletionWagerPoints, roundFundedWagerDown: true);
            var awards = new List<PharaohsAscentFreeSpinBatch>();
            if (outcome.FreeSpinsAwarded > 0)
                awards.Add(new(outcome.FreeSpinsAwarded, outcome.WagerPoints, PharaohsAscentEngine.Profile.ScatterFeatureMode));
            awards.AddRange((collections.FundedAwards ?? []).Select(award =>
                new PharaohsAscentFreeSpinBatch(award.Spins, Math.Max(PharaohsAscentEngine.MinimumWagerPoints, award.WagerPoints), award.Mode)));
            var batches = PharaohsAscentFreeSpins.Advance(savedTomb.FreeSpinBatches, outcome.IsFreeSpin, awards);
            var remaining = batches.Sum(batch => batch.Remaining);
            if (progress.After.LegacyRemaining != 0 || remaining != progress.After.NativeRemaining)
                throw new InvalidOperationException("The funded spin batches disagree with the free-game bank.");
            var carry = remaining == 0 ? null : new ClassicSlotRoundCarry(remaining, batches[0].WagerPoints, batches[0].Mode);
            progress = progress with { After = progress.After with { Carry = carry } };
            tomb = tomb with { Sunfire = sunfire.FundingAfter, FreeSpinBatches = batches,
                BoostFunding = boostFunding,
                PityByWager = PharaohsAscentPity.AfterSpin(savedTomb, outcome.WagerPoints, outcome.ConsecutiveFiveMisses) };
        }
        return progress with { After = progress.After with { NativeStateJson = SealedTombState.Serialize(tomb) } };
    }

    internal static int PityBeforeSpin(ClassicSlotRoundState state, long wagerPoints, string? engineVersion = null) =>
        (engineVersion ?? PharaohsAscentEngine.EngineVersion) == PharaohsAscentEngine.EngineVersion
            ? PharaohsAscentPity.BeforeSpin(SealedTombState.Parse(state.NativeStateJson), wagerPoints, state.Pity)
            : state.Pity;

    internal static int BoostPointsAvailable(ClassicSlotRoundState state, long wagerPoints, string? engineVersion = null) =>
        (engineVersion ?? PharaohsAscentEngine.EngineVersion) == PharaohsAscentEngine.EngineVersion
            ? PharaohsAscentBoosts.BeforeSpin(SealedTombState.Parse(state.NativeStateJson), wagerPoints, state.SpecialPoints)
            : state.SpecialPoints;

    internal static long EnergyBeforeSpin(ClassicSlotRoundState state, long wagerPoints, string? engineVersion = null) =>
        (engineVersion ?? PharaohsAscentEngine.EngineVersion) == PharaohsAscentEngine.EngineVersion
            ? PharaohsAscentSunfire.AppearanceEnergy(state.Energy, wagerPoints, SealedTombState.Parse(state.NativeStateJson).Sunfire)
            : state.Energy;

    internal static string EngineVersionForSpin(ClassicSlotRoundState state, bool useFreeSpin) =>
        useFreeSpin && state.Carry is not null && SealedTombState.Parse(state.NativeStateJson).FreeSpinBatches is null
            ? PharaohsAscentEngine.PreviousTombEngineVersion : PharaohsAscentEngine.EngineVersion;

    private static void ValidateFundedQueue(ClassicSlotRoundState state, SealedTombState tomb)
    {
        if (tomb.FreeSpinBatches is not { } batches) return;
        var remaining = batches.Sum(batch => batch.Remaining);
        if (state.LegacyRemaining != 0 || remaining != state.NativeRemaining ||
            (remaining == 0 ? state.Carry is not null : state.Carry != new ClassicSlotRoundCarry(remaining, batches[0].WagerPoints, batches[0].Mode)))
            throw new InvalidOperationException("The saved funded spin batches disagree with their active bank.");
    }

    internal static string OutcomeDigest(ClassicSlotSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
