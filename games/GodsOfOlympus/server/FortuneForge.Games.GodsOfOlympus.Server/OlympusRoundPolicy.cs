using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.GodsOfOlympus.Server;

internal static class OlympusRoundPolicy
{
    private static readonly int BoostCost = GodsOfOlympusEngine.CreateGame().SpecialPoints!.ActivationCost;

    internal static OlympusPreparedRound Prepare(string account, OlympusRoundRequest request,
        ClassicSlotRoundState before, DateTime started, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (mode, wagerCents, spendsLegacy) = Terms(request, before);
        var engine = OlympusSpinEvidence.Record(random, account, request.WagerPoints, request.UseSpecialBoost,
            before.Energy, mode, before.Pity, request.UseFreeSpin);
        var progress = Advance(before, engine, spendsLegacy);
        var round = new OlympusPreparedRound(Guid.NewGuid(), account, request, before, engine, progress,
            OutcomeDigest(engine), wagerCents, PayoutCents(engine), spendsLegacy, started, observation);
        ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        return round;
    }

    internal static void Validate(OlympusPreparedRound round)
    {
        try
        {
            var (mode, wagerCents, spendsLegacy) = Terms(round.Request, round.Before);
            var progress = Advance(round.Before, round.Engine, spendsLegacy);
            ValidateRound(round, mode, wagerCents, spendsLegacy, progress);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }

    private static void ValidateRound(OlympusPreparedRound round, string? mode, long wagerCents, bool spendsLegacy,
        OlympusRoundProgress progress)
    {
        var engine = round.Engine;
        if (round.AdmissionId == Guid.Empty || round.SpinId == Guid.Empty || engine.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            engine.GeneratedAtUtc < round.StartedAtUtc || round.WagerCents != wagerCents || round.SpendsLegacy != spendsLegacy ||
            round.PayoutCents < 0 || round.PayoutCents != PayoutCents(engine) || !progress.After.Matches(round.Progress.After) ||
            progress.CollectionFreeSpinsAwarded != round.Progress.CollectionFreeSpinsAwarded ||
            progress.AwardedFeatureMode != round.Progress.AwardedFeatureMode ||
            progress.CollectionFreeSpinWagerPoints != round.Progress.CollectionFreeSpinWagerPoints ||
            round.OutcomeSha256 != OutcomeDigest(engine))
            throw new GameWalletUnavailableException();

        if (round.Request.UseFreeSpin)
        {
            if (engine.Classic is not { } classic || engine.Tumble is not null || classic.GameId != GodsOfOlympusEngine.GameId ||
                classic.Audit.EngineVersion != GodsOfOlympusEngine.ClassicEngineVersion || classic.WagerPoints != round.Request.WagerPoints ||
                classic.PointValueInCents != GodsOfOlympusEngine.WagerPointValueInCents || !classic.IsFreeSpin ||
                classic.SpecialBoostApplied != round.Request.UseSpecialBoost || classic.Audit.FeatureMode != mode ||
                classic.Audit.GeneratedAtUtc != engine.GeneratedAtUtc || classic.Audit.PityMissesBefore != round.Before.Pity ||
                classic.Audit.EnergyBalanceBefore != round.Before.Energy || classic.EnergyAwarded != 0)
                throw new GameWalletUnavailableException();
        }
        else if (engine.Tumble is not { } tumble || engine.Classic is not null || engine.ClassicDraws is not null ||
            tumble.GameId != GodsOfOlympusEngine.GameId || tumble.EngineVersion != OlympusTumbleRules.EngineVersion ||
            tumble.WagerPoints != round.Request.WagerPoints || tumble.PointValueInCents != GodsOfOlympusEngine.WagerPointValueInCents)
        {
            throw new GameWalletUnavailableException();
        }

        _ = OlympusRoundStateCodec.Storage.Write(progress.After);
        OlympusSpinEvidence.Validate(engine, round.AccountId);
    }

    private static (string? Mode, long WagerCents, bool SpendsLegacy) Terms(OlympusRoundRequest request, ClassicSlotRoundState before)
    {
        if (!request.UseFreeSpin && request.UseSpecialBoost)
            throw new ArgumentException("Olympus tumble spins do not use the classic special-point boost.", nameof(request));

        var carry = before.Carry;
        if (request.UseFreeSpin && carry is null) throw new OlympusFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && carry is not null) throw new OlympusFreeSpinsRequiredException();
        if (carry?.WagerPoints is { } saved && saved < GodsOfOlympusEngine.MinimumWagerPoints)
            throw new OlympusLegacyFreeWagerUnplayableException(saved);
        if (carry?.WagerPoints is { } locked && request.WagerPoints != locked)
            throw new OlympusFreeWagerLockedException(locked);

        // Old banks without a saved lock use the current legal cash wager on the first free spin.
        GodsOfOlympusEngine.ValidateWager(request.WagerPoints, isFreeSpin: carry?.WagerPoints is not null);
        if (request.UseSpecialBoost && before.SpecialPoints < BoostCost)
            throw new OlympusInsufficientSpecialPointsException(before.SpecialPoints, BoostCost);

        var wagerCents = request.UseFreeSpin ? 0 : ToCents(request.WagerPoints, GodsOfOlympusEngine.WagerPointValueInCents);
        return (carry?.Mode, wagerCents, SpendsLegacy(before, request.UseFreeSpin));
    }

    internal static bool SpendsLegacy(ClassicSlotRoundState before, bool freeSpin) => freeSpin && before.LegacyRemaining > 0;

    internal static OlympusRoundProgress Advance(ClassicSlotRoundState before, OlympusRoundEngineResult engine, bool spendsLegacy)
    {
        // Cash tumbles leave all classic carry, seals, and dormant meter state unchanged.
        if (engine.Tumble is not null) return new(before, 0, null, 0);
        var classic = engine.Classic ?? throw new GameWalletUnavailableException();
        var profile = GodsOfOlympusEngine.Profile;
        var current = ClassicSlotCollections.TrackIds.Select(mode => new SlotCollectionState(mode,
            checked((int)before.SealCounts.GetValueOrDefault(mode)), before.SealWagerPoints.GetValueOrDefault(mode))).ToArray();
        var collections = ClassicSlotCollections.Advance(profile, current, classic.CollectionsAwarded,
            classic.WagerPoints, energyCompleted: false);
        var awardMode = (classic.FreeSpinsAwarded > 0 ? profile.ScatterFeatureMode : null) ?? collections.FeatureMode;
        var legacy = checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0));
        var native = checked(before.NativeRemaining - (spendsLegacy ? 0 : 1) + classic.FreeSpinsAwarded + collections.FreeSpinsAwarded);
        if (legacy < 0 || native < 0) throw new GameWalletUnavailableException();
        var remaining = checked(legacy + native);
        var awardedWager = collections.FreeSpinWagerPoints > 0
            ? Math.Max(GodsOfOlympusEngine.MinimumWagerPoints, collections.FreeSpinWagerPoints)
            : classic.WagerPoints;
        var carry = remaining == 0 ? null : new ClassicSlotRoundCarry(remaining, awardedWager, awardMode ?? before.Carry?.Mode);
        var after = new ClassicSlotRoundState(native, legacy, carry,
            checked(before.SpecialPoints - (classic.SpecialBoostApplied ? BoostCost : 0) + classic.SpecialPointsAwarded),
            0, classic.ConsecutiveFiveMisses,
            collections.Collections.ToDictionary(track => track.Mode, track => (long)track.Count, StringComparer.Ordinal),
            collections.Collections.ToDictionary(track => track.Mode, track => track.WagerPointTotal, StringComparer.Ordinal));
        return new(after, collections.FreeSpinsAwarded, awardMode, collections.FreeSpinWagerPoints);
    }

    internal static string OutcomeDigest(OlympusRoundEngineResult engine) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(engine, RoundAuditCapture.Json));

    internal static long ToCents(long points, decimal pointValue) =>
        checked((long)Math.Round(points * pointValue, MidpointRounding.AwayFromZero));

    private static long PayoutCents(OlympusRoundEngineResult engine) => engine.Tumble is { } tumble
        ? ToCents(tumble.Outcome.TotalPoints, tumble.PointValueInCents)
        : engine.Classic is { } classic
            ? ToCents(classic.Payout.TotalPoints, classic.PointValueInCents)
            : throw new GameWalletUnavailableException();
}
