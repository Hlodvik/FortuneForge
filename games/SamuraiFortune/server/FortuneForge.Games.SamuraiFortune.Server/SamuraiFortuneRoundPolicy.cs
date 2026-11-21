using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.SamuraiFortune.Server;

internal static class SamuraiFortuneRoundPolicy
{
    private static readonly int BoostCost = SamuraiFortuneEngine.CreateGame().SpecialPoints!.ActivationCost;

    internal static SamuraiFortunePreparedRound Prepare(string account, SamuraiFortuneRoundRequest request,
        ClassicSlotRoundState before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = SamuraiFortuneSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            before.Energy, mode, before.Pity, request.UseFreeSpin);
        var outcome = engine.Outcome;
        var progress = Advance(before, outcome, spendsLegacy);
        var round = new SamuraiFortunePreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(progress.SettledPayout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(SamuraiFortunePreparedRound round)
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

    private static void ValidateRound(SamuraiFortunePreparedRound round, string? mode, long wagerCents,
        bool spendsLegacy, SamuraiFortuneRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.Audit;
        if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != SamuraiFortuneEngine.GameId ||
            audit.EngineVersion != SamuraiFortuneEngine.EngineVersion || audit.FeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost || audit.EnergyBalanceBefore != round.Before.Energy ||
            audit.PityMissesBefore != round.Before.Pity || audit.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            audit.GeneratedAtUtc < round.StartedAtUtc || round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.PointValueInCents != SamuraiFortuneEngine.WagerPointValueInCents || outcome.Payout.TotalPoints < 0 ||
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

        _ = SamuraiFortuneRoundStateCodec.Storage.Write(progress.After);
        SamuraiFortuneSpinEvidence.Validate(round.Engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(SamuraiFortuneRoundRequest request, ClassicSlotRoundState before)
    {
        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new SamuraiFortuneFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new SamuraiFortuneFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } saved && saved < SamuraiFortuneEngine.MinimumWagerPoints)
            throw new SamuraiFortuneLegacyFreeWagerUnplayableException(saved);
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new SamuraiFortuneFreeWagerLockedException(locked);

        // Unpinned historical credits choose a legal cash wager on their first free spin.
        SamuraiFortuneEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        if (request.UseSpecialBoost && before.SpecialPoints < BoostCost)
            throw new SamuraiFortuneInsufficientSpecialPointsException(before.SpecialPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, SamuraiFortuneEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(ClassicSlotRoundState before, bool freeSpin) =>
        ClassicSlotRoundProgression.SpendsLegacy(before, freeSpin);

    internal static SamuraiFortuneRoundProgress Advance(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome, bool spendsLegacy)
    {
        var progress = ClassicSlotRoundProgression.AdvanceEnergyAndCollections(before with { Energy = 0 }, outcome, SamuraiFortuneEngine.Profile,
            BoostCost, SamuraiFortuneEngine.MinimumWagerPoints, spendsLegacy);
        return new(progress.After, progress.EnergySettlement, progress.CollectionFreeSpinsAwarded,
            progress.AwardedFeatureMode, progress.CollectionFreeSpinWagerPoints);
    }

    internal static string OutcomeDigest(ClassicSlotSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
