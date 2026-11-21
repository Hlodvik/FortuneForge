using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.DesertTreasures.Server;

public static class DesertTreasuresSpinReport
{
    public const string AuditGameId = DesertTreasuresPaidRouteContract.AuditGameId;
    public const string ResultCollection = ClassicSlotReportWriter.ResultCollection;
    private const long OutcomeSchemaVersion = 7;

    internal static void Write(Transaction transaction, FirestoreDb database, DesertTreasuresPreparedRound round,
        DesertTreasuresRoundResult settled, DateTime completedAtUtc) =>
        ClassicSlotReportWriter.Write(transaction, database, Capture(round, settled, completedAtUtc));

    internal static ClassicSlotPaidReport Capture(DesertTreasuresPreparedRound round, DesertTreasuresRoundResult settled, DateTime completedAtUtc)
    {
        var wallet = settled.Wallet;
        var features = settled.Features;
        var outcome = round.Outcome;
        var audit = ClassicSlotRecordedAudit.From(outcome.Audit);
        var result = DesertTreasuresSpinResponse.Capture(round);
        var energy = round.Progress.EnergySettlement;
        var spinId = outcome.SpinId.ToString("N");
        if (features is null ||
            wallet.SettlementId != GameWalletSettlementIdentity.CreateId(DesertTreasuresEngine.GameId, round.AccountId, spinId) ||
            wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch ||
            wallet.LegacyFreeGamesRemaining != round.Progress.After.LegacyRemaining ||
            wallet.LegacyFreeGameTransactionId != (round.SpendsLegacy ? $"{spinId}-free-game-use" : null) ||
            checked(wallet.BalanceBeforeCents - wallet.BalanceAfterWagerCents) != round.WagerCents ||
            checked(wallet.BalanceAfterCents - wallet.BalanceAfterWagerCents) != round.PayoutCents ||
            round.PayoutCents != DesertTreasuresRoundPolicy.ToCents(energy.Payout.TotalPoints, outcome.PointValueInCents) ||
            round.OutcomeSha256 != DesertTreasuresRoundPolicy.OutcomeDigest(outcome) || completedAtUtc < audit.GeneratedAtUtc)
            throw new InvalidOperationException("The Desert Treasures report does not match its settled wallet receipt.");

        var rulesSha256 = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new
        {
            audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable, audit.FeatureProfile
        }));
        var evidence = new
        {
            result, admissionId = round.AdmissionId, round.StartedAtUtc,
            requestId = round.Observation?.RequestId, engineAudit = audit, rulesSha256,
            settledPayout = energy.Payout, isFreeSpin = outcome.IsFreeSpin, isNeonDiscoPlay = false,
            activeFreeSpinFeatureMode = audit.FreeSpinFeatureMode, neonDiscoSpinsBefore = 0, neonDiscoSpinsRemaining = 0,
            admittedMathModelId = (string?)null, midnightBonusEvidence = (object?)null,
            energy.PayoutMultiplier, piratesGemEvidence = (object?)null, outcomeSchemaVersion = OutcomeSchemaVersion,
            engineOutcomeSha256 = round.OutcomeSha256, walletSettlementId = wallet.SettlementId, desertTreasuresDraws = round.Engine.Draws,
            desertTreasuresProgress = new DesertTreasuresNativeProgressEvidence(round.Request.OperationId, round.Before, round.Progress.After,
                round.SpendsLegacy, wallet.LegacyFreeGamesRemaining, wallet.LegacyFreeGameTransactionId,
                round.Progress.CollectionFreeSpinsAwarded, round.Progress.AwardedFeatureMode, round.Progress.CollectionFreeSpinWagerPoints,
                energy.MeterBalanceBeforeReset, energy.EnergyAddedToMeter,
                energy.MultiplierApplied)
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
            ["payoutBreakdown"] = SlotSpinResultFields.PayoutBreakdown(energy.Payout),
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
            ["energyMultiplierApplied"] = energy.MultiplierApplied,
            ["payoutMultiplier"] = (double)energy.PayoutMultiplier,
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
            ["engineVersion"] = audit.EngineVersion,
            ["rulesSha256"] = rulesSha256,
            ["admittedAtUtc"] = Timestamp.FromDateTime(round.StartedAtUtc),
            ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(audit.GeneratedAtUtc),
            ["result"] = win ? "win" : "loss",
            ["createdAt"] = createdAt,
            ["walletRoundSettlementId"] = wallet.SettlementId,
            ["engineOutcomeSha256"] = round.OutcomeSha256,
            ["slotSettlementJson"] = FrozenSettlement(round, features, wallet.BalanceAfterCents)
        };

        return ClassicSlotReportWriter.Prepare(fields, summary, journal, GameplayWitnesses(round, spinId, createdAt));
    }

    private static string FrozenSettlement(DesertTreasuresPreparedRound round, DesertTreasuresRoundStateReply features, long balanceCents) =>
        JsonSerializer.Serialize(new
        {
            SlotsCreditsBalance = balanceCents / 100m, features.FreeSpinsRemaining, features.SpecialPointsBalance,
            features.EnergyBalance, Payout = round.Progress.SettledPayout,
            EnergyMultiplierApplied = round.Progress.EnergySettlement.MultiplierApplied, round.Progress.EnergySettlement.PayoutMultiplier,
            features.FreeSpinWagerPoints, features.SealCollections, features.FreeSpinFeatureMode,
            SealFreeSpinsAwarded = round.Progress.CollectionFreeSpinsAwarded, BonusRound = (object?)null,
            QueuedBonusRounds = Array.Empty<object>(), AwardedBonusRounds = Array.Empty<object>(), NeonDiscoSpinsRemaining = 0
        });

    private static Dictionary<string, Dictionary<string, object>> GameplayWitnesses(DesertTreasuresPreparedRound round,
        string spinId, Timestamp createdAt)
    {
        const string freeGames = "freeGames:" + DesertTreasuresEngine.GameId;
        const string specialPoints = "specialPoints:" + DesertTreasuresEngine.GameId;
        const string energyCurrency = "energy:" + DesertTreasuresEngine.GameId;
        var outcome = round.Outcome;
        var before = round.Before;
        var after = round.Progress.After;
        var energy = round.Progress.EnergySettlement;
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
        if (energy.EnergyAddedToMeter > 0)
            Add($"{spinId}-energy-award", energyCurrency, "energy-award", energy.EnergyAddedToMeter,
                energy.MeterBalanceBeforeReset, createdAt);
        if (energy.MultiplierApplied)
            Add($"{spinId}-energy-multiplier-use", energyCurrency, "energy-multiplier-use", -energy.MeterBalanceBeforeReset,
                energy.FinalEnergyBalance, createdAt);
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
