using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PhantomManor.Server;

internal static class PhantomManorRoundPolicy
{
    private static readonly int BoostCost = PhantomManorEngine.CreateGame().SpecialPoints!.ActivationCost;

    internal static PhantomManorPreparedRound Prepare(string account, PhantomManorRoundRequest request,
        ClassicSlotRoundState before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = PhantomManorSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            before.Energy, mode, before.Pity, request.UseFreeSpin);
        var outcome = engine.Outcome;
        var progress = Advance(before, outcome, spendsLegacy);
        var round = new PhantomManorPreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(outcome), wagerCents, ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents),
            spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(PhantomManorPreparedRound round)
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

    private static void ValidateRound(PhantomManorPreparedRound round, string? mode, long wagerCents,
        bool spendsLegacy, PhantomManorRoundProgress progress)
    {
        var outcome = round.Outcome;
        var audit = outcome.Audit;
        if (round.AdmissionId == Guid.Empty || outcome.SpinId == Guid.Empty || outcome.GameId != PhantomManorEngine.GameId ||
            audit.EngineVersion != PhantomManorEngine.EngineVersion || audit.FeatureMode != mode ||
            outcome.WagerPoints != round.Request.WagerPoints || outcome.IsFreeSpin != round.Request.UseFreeSpin ||
            outcome.SpecialBoostApplied != round.Request.UseSpecialBoost || audit.EnergyBalanceBefore != round.Before.Energy ||
            audit.PityMissesBefore != round.Before.Pity || audit.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            audit.GeneratedAtUtc < round.StartedAtUtc || round.SpendsLegacy != spendsLegacy || round.WagerCents != wagerCents ||
            outcome.PointValueInCents != PhantomManorEngine.WagerPointValueInCents || outcome.Payout.TotalPoints < 0 ||
            outcome.EnergyAwarded != 0 || outcome.CollectionsAwarded.Count != 0 ||
            round.PayoutCents != ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) ||
            !progress.After.Matches(round.Progress.After) || round.OutcomeSha256 != OutcomeDigest(outcome))
            throw new GameWalletUnavailableException();

        _ = PhantomManorRoundStateCodec.Storage.Write(progress.After);
        PhantomManorSpinEvidence.Validate(round.Engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(PhantomManorRoundRequest request, ClassicSlotRoundState before)
    {
        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new PhantomManorFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new PhantomManorFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } saved && saved < PhantomManorEngine.MinimumWagerPoints)
            throw new PhantomManorLegacyFreeWagerUnplayableException(saved);
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new PhantomManorFreeWagerLockedException(locked);

        PhantomManorEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        if (request.UseSpecialBoost && before.SpecialPoints < BoostCost)
            throw new PhantomManorInsufficientSpecialPointsException(before.SpecialPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, PhantomManorEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(ClassicSlotRoundState before, bool freeSpin) =>
        ClassicSlotRoundProgression.SpendsLegacy(before, freeSpin);

    internal static PhantomManorRoundProgress Advance(ClassicSlotRoundState before, ClassicSlotSpinOutcome outcome, bool spendsLegacy) =>
        new(ClassicSlotRoundProgression.AdvanceScatterOnly(before, outcome, PhantomManorEngine.Profile, BoostCost, spendsLegacy));

    internal static string OutcomeDigest(ClassicSlotSpinOutcome outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));
}
