using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.GodsOfOlympus.Server;

public static class OlympusSpinReport
{
    public const string AuditGameId = OlympusPaidRouteContract.AuditGameId;
    public const string ResultCollection = "slotSpinResults";
    private const long OutcomeSchemaVersion = 7;

    internal static void Write(Transaction transaction, FirestoreDb database, OlympusPreparedRound round,
        OlympusRoundResult settled, DateTime completedAtUtc)
    {
        var report = Capture(round, settled, completedAtUtc);
        var spinId = round.Engine.SpinId.ToString("N");
        transaction.Create(database.Collection(ResultCollection).Document(spinId), report.Result);
        transaction.Set(database.Collection("userSlotStatistics").Document(round.AccountId), report.Statistics, SetOptions.MergeAll);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Round.Settlement.Id),
            report.Round.Settlement.Fields);

        var audit = RoundAuditDocuments.Create(report.Round.Audit);
        var auditId = report.Round.Audit.Summary.Id;
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(auditId), audit.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(auditId), audit.Evidence);
        transaction.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(report.Journal.Id), report.Journal.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(AuditGameId, spinId)), new Dictionary<string, object>
            {
                ["gameId"] = AuditGameId, ["instanceId"] = spinId,
                ["eventIds"] = FieldValue.ArrayUnion(report.Journal.Id), ["schemaVersion"] = 1L
            }, SetOptions.MergeAll);

        var participation = AcceptedGameParticipationDocuments.Create(round.AccountId, AuditGameId, completedAtUtc);
        transaction.Set(database.Collection("catalogPlayerActivity").Document(participation.DocOwnerKey)
            .Collection("games").Document(participation.GameId), participation.Fields, SetOptions.MergeAll);

        foreach (var (id, fields) in report.GameplayWitnesses)
            transaction.Create(database.Collection("balanceTransactions").Document(id), fields);
    }

    internal static OlympusSpinReporting Capture(OlympusPreparedRound round, OlympusRoundResult settled, DateTime completedAtUtc)
    {
        var wallet = settled.Wallet;
        var features = settled.Features;
        var result = OlympusSpinResponse.Capture(round);
        var audit = OlympusSpinEvidence.CaptureAudit(round);
        var spinId = result.SpinId.ToString("N");
        var mathModelId = round.Engine.Tumble?.EngineVersion;
        var engineVersion = mathModelId ?? round.Engine.Classic!.Audit.EngineVersion;
        var admittedMode = round.Engine.Classic?.Audit.FeatureMode;
        if (features is null ||
            wallet.SettlementId != GameWalletSettlementIdentity.CreateId(GodsOfOlympusEngine.GameId, round.AccountId, spinId) ||
            wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch ||
            wallet.LegacyFreeGamesRemaining != round.Progress.After.LegacyRemaining ||
            wallet.LegacyFreeGameTransactionId != (round.SpendsLegacy ? $"{spinId}-free-game-use" : null) ||
            checked(wallet.BalanceBeforeCents - wallet.BalanceAfterWagerCents) != round.WagerCents ||
            checked(wallet.BalanceAfterCents - wallet.BalanceAfterWagerCents) != round.PayoutCents ||
            round.PayoutCents != OlympusRoundPolicy.ToCents(result.Payout.TotalPoints, result.PointValueInCents) ||
            round.OutcomeSha256 != OlympusRoundPolicy.OutcomeDigest(round.Engine) || completedAtUtc < round.Engine.GeneratedAtUtc)
            throw new InvalidOperationException("The Olympus report does not match its settled wallet receipt.");

        var rulesSha256 = OlympusSpinEvidence.RulesDigest(audit);
        var evidence = new
        {
            result, admissionId = round.AdmissionId, round.StartedAtUtc,
            requestId = round.Observation?.RequestId, engineAudit = audit, rulesSha256,
            settledPayout = result.Payout, isFreeSpin = result.IsFreeSpin, isNeonDiscoPlay = false,
            activeFreeSpinFeatureMode = admittedMode, neonDiscoSpinsBefore = 0, neonDiscoSpinsRemaining = 0,
            admittedMathModelId = mathModelId, midnightBonusEvidence = (object?)null,
            payoutMultiplier = 1m, piratesGemEvidence = (object?)null, outcomeSchemaVersion = OutcomeSchemaVersion,
            engineOutcomeSha256 = round.OutcomeSha256, walletSettlementId = wallet.SettlementId,
            olympusClassicDraws = round.Engine.ClassicDraws,
            olympusProgress = new OlympusNativeProgressEvidence(round.Request.OperationId, round.Before, round.Progress.After,
                round.SpendsLegacy, wallet.LegacyFreeGamesRemaining, wallet.LegacyFreeGameTransactionId,
                round.Progress.CollectionFreeSpinsAwarded, round.Progress.AwardedFeatureMode,
                round.Progress.CollectionFreeSpinWagerPoints)
        };

        using var observation = round.Observation is { } observed ? GameSecurityContext.Push(observed) : null;
        var summary = SinglePlayerRoundRecord.Create(AuditGameId, spinId, round.AccountId,
            round.WagerCents, round.PayoutCents, round.StartedAtUtc, completedAtUtc, ResultCollection, spinId,
            evidence, "slot-reels-paylines-and-features-v1", settlementVerified: true, outcomeAuditorVersion: "slot-outcome-auditor-v1");
        var journal = GameSecurityEventDocuments.Capture(AuditGameId, spinId, 1, completedAtUtc,
            evidence, [round.AccountId], "round-settled");

        var wagerRand = (double)(round.WagerCents / 100m);
        var payoutRand = (double)(round.PayoutCents / 100m);
        var netRand = (double)(checked(round.PayoutCents - round.WagerCents) / 100m);
        var win = round.PayoutCents > 0;
        var createdAt = Timestamp.FromDateTime(completedAtUtc);
        var fields = new Dictionary<string, object>
        {
            ["spinId"] = spinId,
            ["admissionId"] = round.AdmissionId.ToString("N"),
            ["requestId"] = round.Observation?.RequestId ?? string.Empty,
            ["userId"] = round.AccountId,
            ["gameId"] = result.GameId,
            ["reelSetId"] = result.ReelSetId,
            ["symbolSetId"] = result.SymbolSetId,
            ["paytableId"] = result.PaytableId,
            ["mathModelId"] = mathModelId ?? string.Empty,
            ["reelStops"] = result.ReelStops.Select(stop => (long)stop).ToArray(),
            ["visibleSymbolWindow"] = SlotSpinResultFields.VisibleSymbolWindow(result.Reels),
            ["payoutBreakdown"] = SlotSpinResultFields.PayoutBreakdown(result.Payout),
            ["wageredSlotsCredits"] = wagerRand,
            ["payoutWagerPoints"] = result.WagerPoints,
            ["wonSlotsCredits"] = payoutRand,
            ["netSlotsCredits"] = netRand,
            ["isFreeSpin"] = result.IsFreeSpin,
            ["isNeonDiscoPlay"] = false,
            ["neonDiscoSpinsRemaining"] = 0,
            ["freeSpinsAwarded"] = result.FreeSpinsAwarded,
            ["specialPointsAwarded"] = result.SpecialPointsAwarded,
            ["energyAwarded"] = result.EnergyAwarded,
            ["energyMultiplierApplied"] = false,
            ["payoutMultiplier"] = 1d,
            ["monkeyPawCount"] = result.MonkeyPawCount,
            ["moneyGrabPoints"] = result.MoneyGrabPoints,
            ["bananaBonusPoints"] = result.BananaBonusPoints,
            ["sealsAwarded"] = result.SealsAwarded.ToDictionary(pair => pair.Key, pair => (object)(long)pair.Value,
                StringComparer.Ordinal),
            ["sealFreeSpinsAwarded"] = round.Progress.CollectionFreeSpinsAwarded,
            ["sealFreeSpinFeatureMode"] = round.Progress.AwardedFeatureMode ?? string.Empty,
            ["specialBoostApplied"] = result.SpecialBoostApplied,
            ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
            ["fiveMatchPityTriggered"] = result.FiveMatchPityTriggered,
            ["pityMissesBefore"] = round.Before.Pity,
            ["outcomeSchemaVersion"] = OutcomeSchemaVersion,
            ["engineVersion"] = engineVersion,
            ["rulesSha256"] = rulesSha256,
            ["admittedAtUtc"] = Timestamp.FromDateTime(round.StartedAtUtc),
            ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(round.Engine.GeneratedAtUtc),
            ["result"] = win ? "win" : "loss",
            ["createdAt"] = createdAt,
            ["walletRoundSettlementId"] = wallet.SettlementId,
            ["engineOutcomeSha256"] = round.OutcomeSha256,
            [mathModelId is null ? "slotSettlementJson" : "olympusTumbleSettlementJson"] =
                FrozenSettlement(result.Payout, round.Progress.CollectionFreeSpinsAwarded, features, wallet.BalanceAfterCents)
        };

        return new(fields, new()
        {
            ["userId"] = round.AccountId,
            ["spinsPlayed"] = FieldValue.Increment(1),
            ["wins"] = FieldValue.Increment(win ? 1 : 0),
            ["losses"] = FieldValue.Increment(win ? 0 : 1),
            ["creditsWagered"] = FieldValue.Increment(wagerRand),
            ["creditsWon"] = FieldValue.Increment(payoutRand),
            ["netCredits"] = FieldValue.Increment(netRand),
            ["updatedAt"] = createdAt
        }, summary, journal, GameplayWitnesses(round, result, spinId, createdAt));
    }

    private static string FrozenSettlement(SpinPayout payout, int sealFreeSpinsAwarded, OlympusRoundStateReply features, long balanceCents) =>
        JsonSerializer.Serialize(new
        {
            SlotsCreditsBalance = balanceCents / 100m, features.FreeSpinsRemaining, features.SpecialPointsBalance,
            features.EnergyBalance, Payout = payout, EnergyMultiplierApplied = false, PayoutMultiplier = 1m,
            features.FreeSpinWagerPoints, features.SealCollections, features.FreeSpinFeatureMode,
            SealFreeSpinsAwarded = sealFreeSpinsAwarded, BonusRound = (object?)null,
            QueuedBonusRounds = Array.Empty<object>(), AwardedBonusRounds = Array.Empty<object>(), NeonDiscoSpinsRemaining = 0
        });

    private static Dictionary<string, Dictionary<string, object>> GameplayWitnesses(OlympusPreparedRound round,
        OlympusSpinResponse outcome, string spinId, Timestamp createdAt)
    {
        const string freeGames = "freeGames:" + GodsOfOlympusEngine.GameId;
        const string specialPoints = "specialPoints:" + GodsOfOlympusEngine.GameId;
        var before = round.Before;
        var after = round.Progress.After;
        var witnesses = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        var totalFreeAwards = checked(outcome.FreeSpinsAwarded + round.Progress.CollectionFreeSpinsAwarded);
        if (outcome.IsFreeSpin && !round.SpendsLegacy)
            Add($"{round.AdmissionId:N}-free-game-use", freeGames, "free-game-use", -1,
                checked(before.NativeRemaining + (long)before.LegacyRemaining - 1), Timestamp.FromDateTime(round.StartedAtUtc));
        if (totalFreeAwards > 0)
            Add($"{spinId}-free-games-award", freeGames, "free-game-award", totalFreeAwards,
                checked(after.NativeRemaining + (long)after.LegacyRemaining), createdAt);
        if (outcome.SpecialBoostApplied)
            Add($"{round.AdmissionId:N}-special-boost-use", specialPoints, "special-boost-use",
                checked(after.SpecialPoints - (long)before.SpecialPoints - outcome.SpecialPointsAwarded),
                checked(after.SpecialPoints - (long)outcome.SpecialPointsAwarded), Timestamp.FromDateTime(round.StartedAtUtc));
        if (outcome.SpecialPointsAwarded > 0)
            Add($"{spinId}-special-points-award", specialPoints, "special-point-award",
                outcome.SpecialPointsAwarded, after.SpecialPoints, createdAt);
        return witnesses;

        void Add(string id, string currency, string type, long amount, long balanceAfter, Timestamp capturedAt) => witnesses.Add(id, new()
        {
            ["transactionId"] = id,
            ["userId"] = round.AccountId,
            ["currencyId"] = currency,
            ["amount"] = amount,
            ["balanceAfter"] = balanceAfter,
            ["type"] = type,
            ["idempotencyKey"] = id,
            ["createdAt"] = capturedAt
        });
    }
}

internal sealed record OlympusSpinReporting(Dictionary<string, object> Result, Dictionary<string, object> Statistics,
    SinglePlayerRoundRecord Round, GameSecurityEventDocument Journal,
    Dictionary<string, Dictionary<string, object>> GameplayWitnesses);
