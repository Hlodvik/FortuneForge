using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.ArcaneArchives.Server;

/// <summary>Projects a settled native Arcane spin into the platform's existing reporting documents.</summary>
public static class ArcaneSpinReport
{
    public const string ResultCollection = "slotSpinResults";
    private const string EvidenceKind = "slot-reels-paylines-and-features-v1";
    private const string AuditorVersion = "slot-outcome-auditor-v1";
    private const long OutcomeSchemaVersion = 7;

    public static void Write(Transaction transaction, FirestoreDb database, ArcanePreparedRound round,
        ArcaneFeatureSettlement features, decimal walletBalance, string walletSettlementId, DateTime completedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletSettlementId);
        var report = Capture(round, features, walletBalance, walletSettlementId, completedAtUtc);
        var spinId = round.Outcome.SpinId.ToString("N");
        var gameId = ArcaneFeatureEndpoints.AuditGameId;

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
            .Document(GameSecurityEventDocuments.ManifestId(gameId, spinId)),
            new Dictionary<string, object>
            {
                ["gameId"] = gameId, ["instanceId"] = spinId,
                ["eventIds"] = FieldValue.ArrayUnion(report.Journal.Id), ["schemaVersion"] = 1L
            }, SetOptions.MergeAll);

        var participation = AcceptedGameParticipationDocuments.Create(round.AccountId, gameId, completedAtUtc);
        transaction.Set(database.Collection("catalogPlayerActivity").Document(participation.DocOwnerKey)
            .Collection("games").Document(participation.GameId), participation.Fields, SetOptions.MergeAll);
    }

    public static ArcaneSpinReporting Capture(ArcanePreparedRound round, ArcaneFeatureSettlement features,
        decimal walletBalance, string walletSettlementId, DateTime completedAtUtc)
    {
        var output = round.Outcome;
        var audit = output.Audit;
        var wagerCents = output.IsFreeSpin ? 0 : checked(output.WagerPoints * output.PointValueInCents);
        var payoutCents = checked(output.Payout.TotalPoints * output.PointValueInCents);
        var engineOutcomeSha256 = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(output, RoundAuditCapture.Json));
        if (round.WagerCents != wagerCents || round.PayoutCents != payoutCents || walletBalance < 0 ||
            round.OutcomeSha256 != engineOutcomeSha256 ||
            round.Request.UseFreeSpin != output.IsFreeSpin || completedAtUtc < round.StartedAtUtc ||
            features is not { Tome: not null, Potions: not null } || round.Features is not { Tome: not null, Potions: not null })
            throw new InvalidOperationException("The Arcane reporting amounts or state differ from the accepted spin.");

        var spinId = output.SpinId.ToString("N");
        var result = ArcaneSpinResponse.CaptureOutcome(round);
        var potion = ArcanePotions.ForSpin(round.Features.Potions, output.IsFreeSpin);
        // This profile projection mirrors the legacy slot reporter's capture contract. The
        // game, strips and paytable alongside it are the engine version's frozen rules.
        var profile = ArcaneArchivesEngine.Profile;
        var featureProfile = new
        {
            profile.GameId, profile.ScatterRequiredSymbols, profile.ScatterAwardedSpins,
            profile.CollectionTarget, profile.CollectionAwardedSpins, profile.UsesCollections,
            profile.UsesEnergy, profile.UsesDirectValueTokens, profile.ScatterFeatureMode,
            profile.CollectionFeatureMode, profile.BaseReelSetId, profile.TargetHitRate,
            profile.PaylinePatternIds, profile.PayoutMultiplier
        };
        var engineAudit = new
        {
            audit.EngineVersion, audit.GeneratedAtUtc, audit.BaseGame, audit.EffectiveGame, audit.ReelSet,
            audit.SymbolSet, audit.Paytable, FeatureProfile = featureProfile, audit.PityMissesBefore,
            audit.EnergyBalanceBefore, FreeSpinFeatureMode = audit.FeatureMode, audit.GenerationAttempts,
            RawGeneratedReels = audit.ReelsBeforeFrequency, ReelsAfterFreeGameFrequency = audit.ReelsAfterFrequency,
            IsDerivedFreeSpin = output.IsFreeSpin
        };
        var rulesSha256 = Hash(new { audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet,
            audit.Paytable, FeatureProfile = featureProfile });
        var evidenceAudit = JsonSerializer.SerializeToNode(engineAudit, RoundAuditCapture.Json)!.AsObject();
        if (potion is not null) evidenceAudit["arcanePotionApplied"] = JsonSerializer.SerializeToNode(potion, RoundAuditCapture.Json);

        var evidence = new
        {
            result, admissionId = round.AdmissionId, round.StartedAtUtc,
            RequestId = round.Observation?.RequestId, engineAudit = evidenceAudit, rulesSha256,
            settledPayout = output.Payout, isFreeSpin = output.IsFreeSpin, isNeonDiscoPlay = false,
            activeFreeSpinFeatureMode = round.AdmittedFreeSpinFeatureMode,
            neonDiscoSpinsBefore = 0, neonDiscoSpinsRemaining = 0, admittedMathModelId = (string?)null,
            midnightBonusEvidence = (object?)null, payoutMultiplier = 1m, piratesGemEvidence = (object?)null,
            outcomeSchemaVersion = OutcomeSchemaVersion,
            engineOutcomeSha256, walletSettlementId,
            arcaneTome = new { before = round.Features.Tome, progress = features.Tome },
            arcanePotions = new { before = round.Features.Potions, after = features.Potions,
                wagerTracked = round.Features.TracksIngredientWagers }
        };
        using var observation = round.Observation is { } observed ? GameSecurityContext.Push(observed) : null;
        var settlement = SinglePlayerRoundRecord.Create(ArcaneFeatureEndpoints.AuditGameId, spinId, round.AccountId,
            round.WagerCents, round.PayoutCents, round.StartedAtUtc, completedAtUtc, ResultCollection, spinId,
            evidence, EvidenceKind, settlementVerified: true, outcomeAuditorVersion: AuditorVersion);
        var journal = GameSecurityEventDocuments.Capture(ArcaneFeatureEndpoints.AuditGameId, spinId, 1, completedAtUtc,
            evidence, [round.AccountId], "round-settled");

        var wagerRand = (double)(round.WagerCents / 100m);
        var payoutRand = (double)(round.PayoutCents / 100m);
        var netRand = (double)(checked(round.PayoutCents - round.WagerCents) / 100m);
        var isWin = output.Payout.TotalPoints > 0;
        var createdAt = Timestamp.FromDateTime(completedAtUtc);
        var fields = new Dictionary<string, object>
        {
            ["spinId"] = spinId,
            ["admissionId"] = round.AdmissionId.ToString("N"),
            ["requestId"] = round.Observation?.RequestId ?? string.Empty,
            ["userId"] = round.AccountId,
            ["gameId"] = output.GameId,
            ["reelSetId"] = output.ReelSetId,
            ["symbolSetId"] = output.SymbolSetId,
            ["paytableId"] = output.PaytableId,
            ["mathModelId"] = string.Empty,
            ["reelStops"] = output.ReelStops.Select(stop => (long)stop).ToArray(),
            ["visibleSymbolWindow"] = SlotSpinResultFields.VisibleSymbolWindow(output.Reels),
            ["payoutBreakdown"] = SlotSpinResultFields.PayoutBreakdown(output.Payout),
            ["wageredSlotsCredits"] = wagerRand,
            ["payoutWagerPoints"] = output.WagerPoints,
            ["wonSlotsCredits"] = payoutRand,
            ["netSlotsCredits"] = netRand,
            ["isFreeSpin"] = output.IsFreeSpin,
            ["isNeonDiscoPlay"] = false,
            ["neonDiscoSpinsRemaining"] = 0,
            ["freeSpinsAwarded"] = output.FreeSpinsAwarded,
            ["specialPointsAwarded"] = output.SpecialPointsAwarded,
            ["energyAwarded"] = output.EnergyAwarded,
            ["energyMultiplierApplied"] = false,
            ["payoutMultiplier"] = 1d,
            ["monkeyPawCount"] = result.MonkeyPawCount,
            ["moneyGrabPoints"] = result.MoneyGrabPoints,
            ["bananaBonusPoints"] = result.BananaBonusPoints,
            ["sealsAwarded"] = output.CollectionsAwarded.ToDictionary(pair => pair.Key, pair => (object)(long)pair.Value,
                StringComparer.Ordinal),
            ["sealFreeSpinsAwarded"] = 0,
            ["sealFreeSpinFeatureMode"] = string.Empty,
            ["specialBoostApplied"] = output.SpecialBoostApplied,
            ["consecutiveFiveMisses"] = output.ConsecutiveFiveMisses,
            ["fiveMatchPityTriggered"] = output.FiveMatchPityTriggered,
            ["pityMissesBefore"] = round.PityMissesBefore,
            ["outcomeSchemaVersion"] = OutcomeSchemaVersion,
            ["engineVersion"] = audit.EngineVersion,
            ["rulesSha256"] = rulesSha256,
            ["admittedAtUtc"] = Timestamp.FromDateTime(round.StartedAtUtc),
            ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(audit.GeneratedAtUtc),
            ["result"] = isWin ? "win" : "loss",
            ["createdAt"] = createdAt,
            ["walletRoundSettlementId"] = walletSettlementId,
            ["engineOutcomeSha256"] = engineOutcomeSha256,
            ["arcaneWandSettlementJson"] = FrozenSettlement(round, features, walletBalance)
        };
        var stats = new Dictionary<string, object>
        {
            ["userId"] = round.AccountId,
            ["spinsPlayed"] = FieldValue.Increment(1),
            ["wins"] = FieldValue.Increment(isWin ? 1 : 0),
            ["losses"] = FieldValue.Increment(isWin ? 0 : 1),
            ["creditsWagered"] = FieldValue.Increment(wagerRand),
            ["creditsWon"] = FieldValue.Increment(payoutRand),
            ["netCredits"] = FieldValue.Increment(netRand),
            ["updatedAt"] = createdAt
        };
        return new(fields, stats, settlement, journal);
    }

    private static string FrozenSettlement(ArcanePreparedRound round, ArcaneFeatureSettlement features, decimal walletBalance) =>
        JsonSerializer.Serialize(new
        {
            SlotsCreditsBalance = walletBalance, round.FreeSpinsRemaining, SpecialPointsBalance = 0, EnergyBalance = 0L,
            round.Outcome.Payout, EnergyMultiplierApplied = false, PayoutMultiplier = 1m, round.FreeSpinWagerPoints,
            SealCollections = Array.Empty<object>(), round.FreeSpinFeatureMode, SealFreeSpinsAwarded = 0,
            BonusRound = (object?)null, QueuedBonusRounds = Array.Empty<object>(), AwardedBonusRounds = Array.Empty<object>(),
            ArcaneTome = features.Tome!.State, ArcanePotions = features.Potions, ArcaneIngredientsAwarded = features.Tome.IngredientsAwarded,
            NeonDiscoSpinsRemaining = 0
        });

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}

public sealed record ArcaneSpinReporting(Dictionary<string, object> Result, Dictionary<string, object> Statistics,
    SinglePlayerRoundRecord Round, GameSecurityEventDocument Journal);
