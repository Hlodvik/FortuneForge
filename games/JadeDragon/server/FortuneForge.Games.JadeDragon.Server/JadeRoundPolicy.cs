using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.JadeDragon.Server;

internal static class JadeRoundPolicy
{
    private static readonly JadeDragonEngine LegacyRules = new();
    private static readonly int LegacyBoostCost = LegacyRules.Definition.SpecialPoints!.ActivationCost;

    internal static JadePreparedRound Prepare(string account, JadeRoundRequest request, JadeRoundBefore before,
        DateTime now, GameRequestObservation? observation, IGameRandomSource random)
    {
        var (version, wager, wagerCents, spendsLegacy) = Terms(request, before);
        JadeRoundEngineResult outcome;
        if (version == JadeDragonCascadeRules.Version)
            outcome = new(new JadeDragonCascadeEngine(random).Spin(wager, request.UseFreeSpin), null, null);
        else
            outcome = JadeSpinEvidence.SpinLegacy(random, wager, account, request.UseSpecialBoost,
                before.Pity, before.Carry!.Mode, before.Energy);

        var (payout, payoutCents, spin) = Settlement(outcome, version);
        var progress = Advance(request, before, outcome, version, wager, spendsLegacy);
        _ = JadeRoundStateCodec.Write(progress.After);
        var round = new JadePreparedRound(Guid.NewGuid(), account, request, before, outcome, OutcomeDigest(outcome),
            wagerCents, payoutCents, spendsLegacy, now, observation, spin, version, wager, payout, progress);
        JadeSpinEvidence.Validate(round);
        return round;
    }

    internal static void Validate(JadePreparedRound round)
    {
        try
        {
            var (engine, wager, wagerCents, spendsLegacy) = Terms(round.Request, round.Before);
            var (payout, payoutCents, spin) = Settlement(round.Outcome, engine);
            var progress = Advance(round.Request, round.Before, round.Outcome, engine, wager, spendsLegacy);
            _ = JadeRoundStateCodec.Write(progress.After);
            if (spin == Guid.Empty || round.SpinId != spin || round.EngineVersion != engine ||
                round.EffectiveWagerPoints != wager || round.WagerCents != wagerCents || round.PayoutCents != payoutCents ||
                round.SpendsLegacy != spendsLegacy || round.OutcomeSha256 != OutcomeDigest(round.Outcome) ||
                !SamePayout(round.SettledPayout, payout) ||
                progress.CollectionFreeSpinsAwarded != round.Progress.CollectionFreeSpinsAwarded ||
                progress.TotalFreeSpinsAwarded != round.Progress.TotalFreeSpinsAwarded ||
                !progress.After.Matches(round.Progress.After))
                throw new GameWalletUnavailableException();
            JadeSpinEvidence.Validate(round);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }

    private static (string Version, long WagerPoints, long WagerCents, bool SpendsLegacy) Terms(
        JadeRoundRequest request, JadeRoundBefore before)
    {
        if (request.UseFreeSpin && before.Carry is null) throw new JadeFreeSpinsUnavailableException();
        if (!request.UseFreeSpin && before.Carry is not null) throw new JadeFreeSpinsRequiredException();
        if (before.Carry is { } carry && request.WagerPoints != carry.WagerPoints)
            throw new JadeFreeWagerLockedException(carry.WagerPoints);

        var version = before.Carry?.EngineVersion ?? JadeDragonCascadeRules.Version;
        var wager = before.Carry?.WagerPoints ?? request.WagerPoints;
        if (version == JadeDragonCascadeRules.Version)
        {
            JadeDragonCascadeRules.ValidatePaidWager(wager);
            if (request.UseSpecialBoost)
                throw new ArgumentException("Jade Dragon cascades do not use a special-point boost.");
        }
        else
        {
            if (version != JadeDragonEngine.Version || !request.UseFreeSpin)
                throw new GameWalletUnavailableException();
            LegacyRules.ValidateWager(wager);
            if (request.UseSpecialBoost && before.SpecialPoints < LegacyBoostCost)
                throw new JadeInsufficientSpecialPointsException(before.SpecialPoints, LegacyBoostCost);
        }

        return (version, wager, request.UseFreeSpin ? 0 : checked(wager * JadeDragonCascadeRules.WagerPointValueInCents),
            request.UseFreeSpin && before.LegacyRemaining > 0);
    }

    private static (SpinPayout Payout, long PayoutCents, Guid SpinId) Settlement(JadeRoundEngineResult outcome, string version)
    {
        if (version == JadeDragonCascadeRules.Version && outcome is { Cascade: { } cascade, Legacy: null, LegacyAudit: null, LegacyDrawIndexes: null })
        {
            if (cascade.Outcome.TotalPoints < 0) throw new GameWalletUnavailableException();
            return (new(cascade.Outcome.TotalPoints, []),
                checked(cascade.Outcome.TotalPoints * JadeDragonCascadeRules.PayoutPointValueInCents), cascade.SpinId);
        }
        if (version == JadeDragonEngine.Version && outcome is { Cascade: null, Legacy: { } legacy, LegacyAudit: not null, LegacyDrawIndexes: not null })
        {
            if (legacy.Payout.TotalPoints < 0 || legacy.PointValueInCents != JadeDragonCascadeRules.WagerPointValueInCents)
                throw new GameWalletUnavailableException();
            return (legacy.Payout, checked(legacy.Payout.TotalPoints * JadeDragonCascadeRules.WagerPointValueInCents), legacy.SpinId);
        }
        throw new GameWalletUnavailableException();
    }

    private static JadeRoundProgress Advance(JadeRoundRequest request, JadeRoundBefore before, JadeRoundEngineResult outcome,
        string version, long wager, bool spendsLegacy)
    {
        var legacyRemaining = checked(before.LegacyRemaining - (spendsLegacy ? 1 : 0));
        var nativeRemaining = checked(before.NativeRemaining - (request.UseFreeSpin && !spendsLegacy ? 1 : 0));
        var next = before;
        int awards;
        var collectionAwards = 0;
        var nextWager = wager;
        var mode = before.Carry?.Mode;
        if (outcome.Legacy is { } legacy)
        {
            var collections = JadeDragonCollections.AdvancePaid(before.SealCounts, before.SealWagerPoints,
                legacy.CollectionSymbolsAwarded, wager, legacy.FreeSpinsAwarded);
            collectionAwards = collections.FreeSpinsAwarded;
            awards = checked(legacy.FreeSpinsAwarded + collectionAwards);
            nextWager = collections.FreeSpinWagerPoints > 0 ? collections.FreeSpinWagerPoints : wager;
            mode = collections.FeatureMode ?? mode;
            next = next with
            {
                SpecialPoints = checked(before.SpecialPoints - (request.UseSpecialBoost ? LegacyBoostCost : 0) + legacy.SpecialPointsAwarded),
                Pity = legacy.ConsecutiveFiveMisses,
                Energy = 0,
                SealCounts = collections.Counts,
                SealWagerPoints = collections.WagerPointTotals
            };
        }
        else
        {
            awards = outcome.Cascade!.Outcome.FreeSpinsAwarded;
        }

        nativeRemaining = checked(nativeRemaining + awards);
        var combined = checked(nativeRemaining + legacyRemaining);
        if (nativeRemaining < 0 || legacyRemaining < 0 || awards < 0)
            throw new GameWalletUnavailableException();
        return new(next with
        {
            NativeRemaining = nativeRemaining, LegacyRemaining = legacyRemaining,
            Carry = combined > 0 ? new(combined, nextWager, mode, version) : null
        }, collectionAwards, awards);
    }

    internal static string OutcomeDigest(JadeRoundEngineResult outcome) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));

    private static bool SamePayout(SpinPayout left, SpinPayout right) =>
        JsonSerializer.Serialize(left, RoundAuditCapture.Json) == JsonSerializer.Serialize(right, RoundAuditCapture.Json);
}
