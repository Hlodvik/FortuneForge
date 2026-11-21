using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.GoldFoundry.Server;

public static class GoldFoundrySpinReport
{
    public const string AuditGameId = GoldFoundryPaidRouteContract.AuditGameId;
    public const string ResultCollection = "slotSpinResults";
    private const long OutcomeSchemaVersion = 7;

    internal static void Write(Transaction transaction, FirestoreDb database, GoldFoundryPreparedRound round,
        GoldFoundryRoundResult settled, DateTime completedAtUtc)
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

        foreach (var (id, fields) in report.EnergyWitnesses)
            transaction.Create(database.Collection("balanceTransactions").Document(id), fields);
    }

    internal static GoldFoundrySpinReporting Capture(GoldFoundryPreparedRound round, GoldFoundryRoundResult settled,
        DateTime completedAtUtc)
    {
        var wallet = settled.Wallet;
        var features = settled.Features;
        var outcome = round.Outcome;
        var audit = outcome.EngineAudit;
        var result = GoldFoundrySpinResponse.Capture(round);
        var crucible = round.Crucible;
        var spinId = outcome.SpinId.ToString("N");
        if (features is null || features.EnergyBalance != crucible.EnergyBalance ||
            features.CrucibleWageredCents != crucible.WageredCents || features.CruciblePourValueCents != crucible.PourValueCents ||
            wallet.SettlementId != GameWalletSettlementIdentity.CreateId(GoldFoundryEngine.GameId, round.AccountId, spinId) ||
            wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch ||
            checked(wallet.BalanceBeforeCents - wallet.BalanceAfterWagerCents) != round.WagerCents ||
            checked(wallet.BalanceAfterCents - wallet.BalanceAfterWagerCents) != round.PayoutCents ||
            round.PayoutCents != checked(GoldFoundryRoundPolicy.ToCents(outcome.Payout.TotalPoints, outcome.PointValueInCents) + crucible.PayoutCents) ||
            round.OutcomeSha256 != GoldFoundryRoundPolicy.OutcomeDigest(outcome) || completedAtUtc < audit.GeneratedAtUtc)
            throw new InvalidOperationException("The Gold Foundry report does not match its settled wallet receipt.");

        var rulesSha256 = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
        {
            audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable, audit.FeatureProfile
        }));
        var evidence = new
        {
            result, admissionId = round.AdmissionId, round.StartedAtUtc,
            requestId = round.Observation?.RequestId, engineAudit = audit, rulesSha256,
            settledPayout = outcome.Payout, isFreeSpin = false, isNeonDiscoPlay = false,
            activeFreeSpinFeatureMode = (string?)null, neonDiscoSpinsBefore = 0, neonDiscoSpinsRemaining = 0,
            admittedMathModelId = (string?)null, midnightBonusEvidence = (object?)null,
            payoutMultiplier = 1m, piratesGemEvidence = (object?)null, outcomeSchemaVersion = OutcomeSchemaVersion,
            crucible = new { wageredBeforeCents = round.Before.CrucibleWageredCents, settlement = crucible },
            engineOutcomeSha256 = round.OutcomeSha256, walletSettlementId = wallet.SettlementId
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
            ["mathModelId"] = string.Empty,
            ["reelStops"] = result.ReelStops.Select(stop => (long)stop).ToArray(),
            ["visibleSymbolWindow"] = SlotSpinResultFields.VisibleSymbolWindow(result.Reels),
            ["payoutBreakdown"] = SlotSpinResultFields.PayoutBreakdown(outcome.Payout),
            ["wageredSlotsCredits"] = wagerRand,
            ["payoutWagerPoints"] = result.WagerPoints,
            ["wonSlotsCredits"] = payoutRand,
            ["netSlotsCredits"] = netRand,
            ["isFreeSpin"] = false,
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
            ["sealFreeSpinsAwarded"] = 0,
            ["sealFreeSpinFeatureMode"] = string.Empty,
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
            ["goldFoundrySettlementJson"] = FrozenSettlement(outcome.Payout, crucible, features, wallet.BalanceAfterCents),
            ["crucibleWageredBeforeCents"] = round.Before.CrucibleWageredCents,
            ["crucibleWageredCents"] = crucible.WageredCents,
            ["cruciblePayoutCents"] = crucible.PayoutCents,
            ["cruciblePoured"] = crucible.Poured
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
        }, summary, journal, EnergyWitnesses(round, spinId, createdAt));
    }

    private static string FrozenSettlement(SpinPayout payout, GoldFoundryCrucibleSettlement crucible,
        GoldFoundryRoundStateReply features, long balanceCents) => JsonSerializer.Serialize(new
    {
        SlotsCreditsBalance = balanceCents / 100m, features.FreeSpinsRemaining, features.SpecialPointsBalance,
        features.EnergyBalance, Payout = payout, EnergyMultiplierApplied = false, PayoutMultiplier = 1m,
        features.FreeSpinWagerPoints, features.SealCollections, features.FreeSpinFeatureMode,
        SealFreeSpinsAwarded = 0, CrucibleWageredCents = crucible.WageredCents, CruciblePourValueCents = crucible.PourValueCents,
        CruciblePoured = crucible.Poured, CruciblePayoutCents = crucible.PayoutCents, BonusRound = (object?)null,
        QueuedBonusRounds = Array.Empty<object>(), AwardedBonusRounds = Array.Empty<object>(), NeonDiscoSpinsRemaining = 0
    });

    private static Dictionary<string, Dictionary<string, object>> EnergyWitnesses(GoldFoundryPreparedRound round,
        string spinId, Timestamp createdAt)
    {
        var crucible = round.Crucible;
        var witnesses = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        if (crucible.EnergyAddedToMeter > 0)
            Add("energy-award", crucible.EnergyAddedToMeter, crucible.MeterBalanceBeforeReset);
        if (crucible.Poured)
            Add("energy-crucible-pour", -crucible.MeterBalanceBeforeReset, crucible.EnergyBalance);
        return witnesses;

        void Add(string kind, long amount, long balanceAfter)
        {
            var id = $"{spinId}-{kind}";
            witnesses.Add(id, new()
            {
                ["transactionId"] = id,
                ["userId"] = round.AccountId,
                ["currencyId"] = $"energy:{GoldFoundryEngine.GameId}",
                ["amount"] = amount,
                ["balanceAfter"] = balanceAfter,
                ["type"] = kind,
                ["idempotencyKey"] = id,
                ["createdAt"] = createdAt
            });
        }
    }
}

internal sealed record GoldFoundrySpinReporting(Dictionary<string, object> Result, Dictionary<string, object> Statistics,
    SinglePlayerRoundRecord Round, GameSecurityEventDocument Journal,
    Dictionary<string, Dictionary<string, object>> EnergyWitnesses);
