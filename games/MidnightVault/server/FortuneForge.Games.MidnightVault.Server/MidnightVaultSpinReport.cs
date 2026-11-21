using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.MidnightVault.Server;

public static class MidnightVaultSpinReport
{
    public const string AuditGameId = MidnightVaultPaidRouteContract.AuditGameId;
    public const string ResultCollection = "slotSpinResults";
    private const long OutcomeSchemaVersion = 7;

    internal static void Write(Transaction transaction, FirestoreDb database, MidnightVaultPreparedRound round,
        MidnightVaultRoundResult settled, DateTime completedAtUtc)
    {
        var report = Capture(round, settled, completedAtUtc);
        var spinId = round.Outcome.SpinId.ToString("N");
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

    internal static MidnightVaultSpinReporting Capture(MidnightVaultPreparedRound round, MidnightVaultRoundResult settled,
        DateTime completedAtUtc)
    {
        var wallet = settled.Wallet;
        var features = settled.Features;
        var outcome = round.Outcome;
        var audit = outcome.EngineAudit;
        var result = MidnightVaultSpinResponse.Capture(round);
        var bonus = round.Progress.BonusEvidence;
        var spinId = outcome.SpinId.ToString("N");
        if (features is null ||
            wallet.SettlementId != GameWalletSettlementIdentity.CreateId(MidnightVaultEngine.GameId, round.AccountId, spinId) ||
            wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch ||
            wallet.LegacyFreeGamesRemaining != round.Progress.After.LegacyRemaining ||
            wallet.LegacyFreeGameTransactionId != (round.SpendsLegacy ? $"{spinId}-free-game-use" : null) ||
            checked(wallet.BalanceBeforeCents - wallet.BalanceAfterWagerCents) != round.WagerCents ||
            checked(wallet.BalanceAfterCents - wallet.BalanceAfterWagerCents) != round.PayoutCents ||
            round.PayoutCents != MidnightVaultRoundPolicy.ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) ||
            round.OutcomeSha256 != MidnightVaultRoundPolicy.OutcomeDigest(outcome) || completedAtUtc < audit.GeneratedAtUtc)
            throw new InvalidOperationException("The Midnight Vault report does not match its settled wallet receipt.");

        var rulesSha256 = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
        {
            audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable, audit.FeatureProfile
        }));
        var evidence = new
        {
            result, admissionId = round.AdmissionId, round.StartedAtUtc,
            requestId = round.Observation?.RequestId, engineAudit = audit, rulesSha256,
            settledPayout = outcome.Payout, isFreeSpin = outcome.IsFreeSpin, isNeonDiscoPlay = false,
            activeFreeSpinFeatureMode = audit.FreeSpinFeatureMode, neonDiscoSpinsBefore = 0, neonDiscoSpinsRemaining = 0,
            admittedMathModelId = outcome.PaytableId, midnightBonusEvidence = bonus,
            payoutMultiplier = 1m, piratesGemEvidence = (object?)null, outcomeSchemaVersion = OutcomeSchemaVersion,
            engineOutcomeSha256 = round.OutcomeSha256, walletSettlementId = wallet.SettlementId,
            midnightProgress = new MidnightVaultNativeProgressEvidence(round.Request.OperationId,
                round.Before.LegacyRemaining, round.SpendsLegacy, wallet.LegacyFreeGamesRemaining,
                wallet.LegacyFreeGameTransactionId, round.Before.SpecialPoints, round.Progress.After.SpecialPoints,
                round.Progress.After.Energy, round.Progress.CollectionFreeSpinsAwarded)
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
            ["mathModelId"] = result.PaytableId,
            ["reelStops"] = result.ReelStops.Select(stop => (long)stop).ToArray(),
            ["visibleSymbolWindow"] = SlotSpinResultFields.VisibleSymbolWindow(result.Reels),
            ["payoutBreakdown"] = SlotSpinResultFields.PayoutBreakdown(outcome.Payout),
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
            ["monkeyPawCount"] = 0,
            ["moneyGrabPoints"] = 0L,
            ["bananaBonusPoints"] = result.BananaBonusPoints,
            ["sealsAwarded"] = result.SealsAwarded.ToDictionary(pair => pair.Key, pair => (object)(long)pair.Value,
                StringComparer.Ordinal),
            ["sealFreeSpinsAwarded"] = round.Progress.CollectionFreeSpinsAwarded,
            ["sealFreeSpinFeatureMode"] = features.FreeSpinFeatureMode ?? string.Empty,
            ["specialBoostApplied"] = result.SpecialBoostApplied,
            ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
            ["fiveMatchPityTriggered"] = result.FiveMatchPityTriggered,
            ["pityMissesBefore"] = round.Before.Pity,
            ["outcomeSchemaVersion"] = OutcomeSchemaVersion,
            ["engineVersion"] = audit.EngineVersion,
            ["rulesSha256"] = rulesSha256,
            ["admittedAtUtc"] = Timestamp.FromDateTime(round.StartedAtUtc),
            ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(audit.GeneratedAtUtc),
            ["result"] = win ? "win" : "loss",
            ["createdAt"] = createdAt,
            ["walletRoundSettlementId"] = wallet.SettlementId,
            ["engineOutcomeSha256"] = round.OutcomeSha256,
            ["midnightSettlementJson"] = FrozenSettlement(round, features, wallet.BalanceAfterCents)
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
        }, summary, journal, GameplayWitnesses(round, spinId, createdAt));
    }

    private static string FrozenSettlement(MidnightVaultPreparedRound round, MidnightVaultRoundStateReply features,
        long balanceCents) => JsonSerializer.Serialize(new
    {
        SlotsCreditsBalance = balanceCents / 100m, features.FreeSpinsRemaining, features.SpecialPointsBalance,
        features.EnergyBalance, round.Outcome.Payout, EnergyMultiplierApplied = false, PayoutMultiplier = 1m,
        features.FreeSpinWagerPoints, features.SealCollections, features.FreeSpinFeatureMode,
        SealFreeSpinsAwarded = round.Progress.CollectionFreeSpinsAwarded, features.BonusRound, features.QueuedBonusRounds,
        AwardedBonusRounds = round.Progress.BonusEvidence.AwardedRounds, NeonDiscoSpinsRemaining = 0
    });

    private static Dictionary<string, Dictionary<string, object>> GameplayWitnesses(MidnightVaultPreparedRound round,
        string spinId, Timestamp createdAt)
    {
        const string freeGamesCurrency = "freeGames:" + MidnightVaultEngine.GameId;
        const string specialPointsCurrency = "specialPoints:" + MidnightVaultEngine.GameId;
        var outcome = round.Outcome;
        var bonus = round.Progress.BonusEvidence;
        var after = round.Progress.After;
        var witnesses = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        if (outcome.IsFreeSpin && !round.SpendsLegacy)
            Add($"{round.AdmissionId:N}-free-game-use", freeGamesCurrency, "free-game-use", -1,
                bonus.RoundsAfterConsumption.Sum(item => (long)item.RemainingSpins));
        if (bonus.AwardedRounds.Count > 0)
            Add($"{spinId}-free-games-award", freeGamesCurrency, "free-game-award",
                bonus.AwardedRounds.Sum(item => (long)item.TotalSpins), after.Rounds.Sum(item => (long)item.RemainingSpins));
        if (outcome.SpecialBoostApplied)
            Add($"{round.AdmissionId:N}-special-boost-use", specialPointsCurrency, "special-boost-use",
                checked(after.SpecialPoints - (long)round.Before.SpecialPoints - outcome.SpecialPointsAwarded),
                checked(after.SpecialPoints - (long)outcome.SpecialPointsAwarded));
        if (outcome.SpecialPointsAwarded > 0)
            Add($"{spinId}-special-points-award", specialPointsCurrency, "special-point-award",
                outcome.SpecialPointsAwarded, after.SpecialPoints);
        return witnesses;

        void Add(string id, string currency, string type, long amount, long balanceAfter) => witnesses.Add(id, new()
        {
            ["transactionId"] = id,
            ["userId"] = round.AccountId,
            ["currencyId"] = currency,
            ["amount"] = amount,
            ["balanceAfter"] = balanceAfter,
            ["type"] = type,
            ["idempotencyKey"] = id,
            ["createdAt"] = type is "free-game-use" or "special-boost-use" ? Timestamp.FromDateTime(round.StartedAtUtc) : createdAt
        });
    }
}

internal sealed record MidnightVaultSpinReporting(Dictionary<string, object> Result, Dictionary<string, object> Statistics,
    SinglePlayerRoundRecord Round, GameSecurityEventDocument Journal,
    Dictionary<string, Dictionary<string, object>> GameplayWitnesses);
