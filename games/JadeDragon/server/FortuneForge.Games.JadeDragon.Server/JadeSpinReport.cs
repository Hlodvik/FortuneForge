using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.JadeDragon.Server;

public static class JadeSpinReport
{
    public const string AuditGameId = JadePaidRouteContract.AuditGameId;
    public const string ResultCollection = "slotSpinResults";
    private const long OutcomeSchemaVersion = 7;

    internal static void Write(Transaction transaction, FirestoreDb database, JadePreparedRound round,
        JadeRoundResult settled, DateTime completedAtUtc)
    {
        var report = Capture(round, settled, completedAtUtc);
        var spinId = round.SpinId.ToString("N");
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
    }

    internal static JadeSpinReporting Capture(JadePreparedRound round, JadeRoundResult settled, DateTime completedAtUtc)
    {
        var wallet = settled.Wallet;
        var features = settled.Features ?? throw Invalid();
        var audit = JadeSpinEvidence.Capture(round);
        var result = JadeSpinResponse.Capture(round);
        var payoutCents = checked(round.SettledPayout.TotalPoints * (result.PayoutPointValueInCents ?? result.PointValueInCents));
        var spinId = round.SpinId.ToString("N");
        if (wallet.SettlementId != GameWalletSettlementIdentity.CreateId(JadeDragonEngine.GameId, round.AccountId, spinId) ||
            round.OutcomeSha256 != JadeRoundPolicy.OutcomeDigest(round.Outcome) ||
            wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch ||
            wallet.BalanceBeforeCents - wallet.BalanceAfterWagerCents != round.WagerCents ||
            wallet.BalanceAfterCents - wallet.BalanceAfterWagerCents != round.PayoutCents ||
            round.PayoutCents != payoutCents || completedAtUtc < audit.GeneratedAtUtc)
            throw Invalid();

        var isCascade = round.EngineVersion == JadeDragonCascadeRules.Version;
        var mathModel = isCascade ? JadeDragonCascadeRules.Version : null;
        var admittedMode = round.Request.UseFreeSpin ? round.Before.Carry?.Mode : null;
        var collectionMode = round.Progress.CollectionFreeSpinsAwarded > 0 ? JadeDragonCollections.CollectionFeatureMode : null;
        var rulesSha256 = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
        {
            audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable, audit.FeatureProfile
        }));
        var evidence = new
        {
            result, admissionId = round.AdmissionId, round.StartedAtUtc,
            requestId = round.Observation?.RequestId, engineAudit = audit, rulesSha256,
            settledPayout = round.SettledPayout, isFreeSpin = round.Request.UseFreeSpin, isNeonDiscoPlay = false,
            activeFreeSpinFeatureMode = admittedMode, neonDiscoSpinsBefore = 0, neonDiscoSpinsRemaining = 0,
            admittedMathModelId = mathModel, midnightBonusEvidence = (object?)null,
            payoutMultiplier = 1m, piratesGemEvidence = (object?)null, outcomeSchemaVersion = OutcomeSchemaVersion,
            engineOutcomeSha256 = round.OutcomeSha256, walletSettlementId = wallet.SettlementId,
            jadeProgress = new { before = round.Before, after = round.Progress.After,
                round.Progress.CollectionFreeSpinsAwarded, round.Progress.TotalFreeSpinsAwarded, round.SpendsLegacy }
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
            ["mathModelId"] = mathModel ?? string.Empty,
            ["reelStops"] = result.ReelStops.Select(stop => (long)stop).ToArray(),
            ["visibleSymbolWindow"] = SlotSpinResultFields.VisibleSymbolWindow(result.Reels),
            ["payoutBreakdown"] = SlotSpinResultFields.PayoutBreakdown(round.SettledPayout),
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
            ["sealFreeSpinFeatureMode"] = collectionMode ?? string.Empty,
            ["specialBoostApplied"] = result.SpecialBoostApplied,
            ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
            ["fiveMatchPityTriggered"] = result.FiveMatchPityTriggered,
            ["pityMissesBefore"] = round.Before.Pity,
            ["outcomeSchemaVersion"] = OutcomeSchemaVersion,
            ["engineVersion"] = round.EngineVersion,
            ["rulesSha256"] = rulesSha256,
            ["admittedAtUtc"] = Timestamp.FromDateTime(round.StartedAtUtc),
            ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(audit.GeneratedAtUtc),
            ["result"] = win ? "win" : "loss",
            ["createdAt"] = createdAt,
            ["walletRoundSettlementId"] = wallet.SettlementId,
            ["engineOutcomeSha256"] = round.OutcomeSha256,
            [isCascade ? "jadeDragonCascadeSettlementJson" : "slotSettlementJson"] = FrozenSettlement(round, features, wallet.BalanceAfterCents)
        };
        if (isCascade) fields["payoutPointValueInCents"] = JadeDragonCascadeRules.PayoutPointValueInCents;

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
        }, summary, journal);
    }

    private static string FrozenSettlement(JadePreparedRound round, JadeRoundStateReply features, long balanceCents) =>
        JsonSerializer.Serialize(new
        {
            SlotsCreditsBalance = balanceCents / 100m, features.FreeSpinsRemaining, features.SpecialPointsBalance,
            features.EnergyBalance, Payout = round.SettledPayout, EnergyMultiplierApplied = false, PayoutMultiplier = 1m,
            features.FreeSpinWagerPoints, features.SealCollections, features.FreeSpinFeatureMode,
            SealFreeSpinsAwarded = round.Progress.CollectionFreeSpinsAwarded, BonusRound = (object?)null,
            QueuedBonusRounds = Array.Empty<object>(), AwardedBonusRounds = Array.Empty<object>(), NeonDiscoSpinsRemaining = 0
        });

    private static InvalidOperationException Invalid() => new("The Jade Dragon report does not match its settled wallet receipt.");
}

internal sealed record JadeSpinReporting(Dictionary<string, object> Result, Dictionary<string, object> Statistics,
    SinglePlayerRoundRecord Round, GameSecurityEventDocument Journal);
