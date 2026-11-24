using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Games;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class ArcanePlatformReportingTests(MidnightVaultPaidEmulatorFixture emulator)
{
    [Theory]
    [InlineData(ArcaneArchivesEngine.PreviousPotionEngineVersion)]
    [InlineData(ArcaneArchivesEngine.PreviousExactPaylineEngineVersion)]
    [InlineData(ArcaneArchivesEngine.PreviousBalancedPotionEngineVersion)]
    [InlineData(ArcaneArchivesEngine.EngineVersion)]
    public void FrozenArcaneMathAndPlatformAuditReplayTheirOwnVersion(string version)
    {
        var (round, features) = CreateRound(version);
        var potion = ArcanePotions.ForSpin(round.Features.Potions!, false);
        void Replay(ArcanePreparedRound candidate) => ArcaneNativeReplay.Validate(candidate.Outcome, candidate.AccountId,
            candidate.Request.WagerPoints, false, version, candidate.Features.Tome!, potion, candidate.PityMissesBefore);

        Replay(round);
        var changed = round.Outcome.Payout with { TotalPoints = round.Outcome.Payout.TotalPoints + 1 };
        Assert.Throws<InvalidOperationException>(() => Replay(round with { Outcome = round.Outcome with { Payout = changed } }));
        Assert.Throws<InvalidOperationException>(() => Replay(round with { Request = round.Request with { WagerPoints = 16 } }));
        Assert.Throws<InvalidOperationException>(() => ArcaneSpinReport.Capture(round with { OutcomeSha256 = new string('0', 64) },
            features, 99m, "wallet-receipt", round.StartedAtUtc.AddSeconds(1)));

        var report = ArcaneSpinReport.Capture(round, features, 99m, "wallet-receipt", round.StartedAtUtc.AddSeconds(1));
        Assert.Equal(round.OutcomeSha256, report.Result["engineOutcomeSha256"]);
        Assert.Equal("wallet-receipt", report.Result["walletRoundSettlementId"]);
        Assert.Equal("original-arcane-request", report.Result["requestId"]);
        Assert.Equal(round.Observation, report.Round.Audit.Request);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(report.Round.Audit.Summary, report.Round.Audit.PayloadJson));
        var tampered = JsonNode.Parse(report.Round.Audit.PayloadJson)!;
        tampered["data"]!["settledPayout"]!["totalPoints"] = changed.TotalPoints;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(report.Round.Audit.Summary, tampered.ToJsonString()),
            finding => finding.Code == "slot-settlement-mismatch");

        var response = ArcaneSpinResponse.From(round, features, 99m);
        Assert.Equal(round.Outcome.SpinId, response.SpinId);
        Assert.Equal(round.Outcome.Payout, response.Payout);
        Assert.Equal(99m, response.SlotsCreditsBalance);
        Assert.True(ArcaneWandCollector.SameCollection(
            ArcaneWandCollector.Calculate(round.Outcome.Reels, round.Outcome.WagerPoints, potion), response.ArcaneWandCollection));
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(ArcaneTome.ToView(features.Tome!.State), RoundAuditCapture.Json),
            JsonSerializer.SerializeToNode(response.ArcaneTome, RoundAuditCapture.Json)));
    }

    [Fact]
    public async Task WorkerReportRemainsVisibleToExistingHistoryAndStatisticsReaders()
    {
        var database = emulator.Database();
        var (round, features) = CreateRound(ArcaneArchivesEngine.EngineVersion);
        var completed = round.StartedAtUtc.AddSeconds(1);
        await database.RunTransactionAsync(transaction =>
        {
            ArcaneSpinReport.Write(transaction, database, round, features, 99m, "wallet-receipt", completed);
            return Task.FromResult(true);
        });

        var history = Assert.Single(await new FirestoreAccountStore(database).GetSlotSpinHistoryAsync(round.AccountId, 10, default));
        Assert.Equal(round.Outcome.SpinId.ToString("N"), history.SpinId);
        Assert.Equal(round.WagerCents / 100m, history.WageredSlotsCredits);
        Assert.Equal(round.PayoutCents / 100m, history.WonSlotsCredits);
        Assert.Equal((round.PayoutCents - round.WagerCents) / 100m, history.NetSlotsCredits);

        var stats = await database.Collection("userSlotStatistics").Document(round.AccountId).GetSnapshotAsync();
        Assert.Equal(1L, stats.GetValue<long>("spinsPlayed"));
        Assert.Equal((double)(round.PayoutCents / 100m), stats.GetValue<double>("creditsWon"));
        var report = ArcaneSpinReport.Capture(round, features, 99m, "wallet-receipt", completed);
        var auditId = report.Round.Audit.Summary.Id;
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(auditId).GetSnapshotAsync();
        Assert.Equal(report.Round.Audit.PayloadJson, evidence.GetValue<string>("payloadJson"));
        Assert.True((await database.Collection(GameSecurityEventDocuments.CollectionName).Document(report.Journal.Id)
            .GetSnapshotAsync()).Exists);
    }

    [Fact]
    public void WorkerReportReplaysLastLegacyEntitlementAtItsSavedWager()
    {
        var (round, features) = CreateRound(ArcaneArchivesEngine.EngineVersion, useFreeSpin: true);
        ArcaneNativeReplay.Validate(round.Outcome, round.AccountId, 13, true, round.Outcome.Audit.EngineVersion,
            round.Features.Tome!, potion: null, round.PityMissesBefore);
        var report = ArcaneSpinReport.Capture(round, features, 99m, "wallet-free-play-receipt", round.StartedAtUtc.AddSeconds(1));
        Assert.Equal(0L, Assert.Single(report.Round.Audit.Summary.Participants).WagerCents);
        Assert.Equal(13L, report.Result["payoutWagerPoints"]);
        var response = ArcaneSpinResponse.From(round, features, 99m);
        Assert.Equal(0, response.FreeSpinsRemaining);
        Assert.Null(response.FreeSpinWagerPoints);
        using var evidence = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = evidence.RootElement.GetProperty("data");
        Assert.Equal("sync-paw-rand", data.GetProperty("activeFreeSpinFeatureMode").GetString());
        Assert.True(data.GetProperty("engineAudit").GetProperty("isDerivedFreeSpin").GetBoolean());
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(report.Round.Audit.Summary, report.Round.Audit.PayloadJson));
    }

    private static (ArcanePreparedRound Round, ArcaneFeatureSettlement Features) CreateRound(string version, bool useFreeSpin = false)
    {
        const long requestedWager = 12;
        var admittedWager = useFreeSpin ? 13L : requestedWager;
        const int pityBefore = 4;
        var accountId = "arcane-reporting-" + Guid.NewGuid().ToString("N");
        var operation = new ArcaneRoundRequest("test-arcane-reporting-operation", requestedWager, useFreeSpin);
        var tome = ArcaneTome.SelectPage(ArcaneTome.CompleteSpin(ArcaneTome.Empty, [[ArcaneTome.GrimoireSymbolId]],
            false).State, "herbalism", true);
        var ingredients = Enumerable.Repeat("PHOENIX_FEATHER", ArcanePotions.Majority).ToArray();
        var brewed = ArcanePotions.CompleteSpin(ArcanePotions.Empty, ingredients, false, requestedWager);
        var potions = ArcanePotions.Use(brewed, Assert.Single(brewed.Bottles).Id);
        var potion = ArcanePotions.ForSpin(potions, useFreeSpin);
        var started = DateTime.UtcNow;
        var admissionId = Guid.NewGuid();
        var admitted = new PaidSlotOutcomeRequest(admissionId, ArcaneArchivesEngine.GameId, accountId,
            admittedWager, useFreeSpin, false, 0, null, pityBefore, null);
        var options = new PaidArcaneSpinOptions(version, TomeActive: true, potion is null ? null :
            new PaidArcanePotion(potion.Id, potion.IngredientId, potion.Potency) { VariantId = potion.VariantId });
        var resolved = new ArcanePaidSlotOutcomes(new FixtureRandom()).Resolve(options.WriteTo(admitted));
        var outcome = Assert.IsType<ClassicSlotSpinOutcome>(resolved.Outcome.Deserialize<ClassicSlotSpinOutcome>(RoundAuditCapture.Json));
        var hash = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(outcome, RoundAuditCapture.Json));
        var round = new ArcanePreparedRound(admissionId, accountId, operation, new(tome, potions, true), outcome,
            hash, useFreeSpin ? 0 : checked((long)(admittedWager * outcome.PointValueInCents)),
            checked((long)(outcome.Payout.TotalPoints * outcome.PointValueInCents)), started)
        {
            PityMissesBefore = pityBefore,
            AdmittedFreeSpinFeatureMode = useFreeSpin ? "sync-paw-rand" : null,
            Observation = new("original-arcane-request", accountId, null, null, started, "POST",
                "/api/slots/arcane/prepared-spins", ArcaneArchivesEngine.GameId, null, operation.OperationId, null,
                null, "empty", "direct")
        };
        var features = new ArcaneFeatureSettlement(
            ArcaneTome.CompleteSpin(tome, outcome.Reels, useFreeSpin),
            ArcanePotions.CompleteSpin(potions, ArcaneTome.OrderedIngredientUnits(tome, outcome.Reels, useFreeSpin),
                useFreeSpin, admittedWager, allowRareRecipes: ArcaneArchivesEngine.SupportsRarePotions(version)));
        return (round, features);
    }

    private sealed class FixtureRandom : IGameRandomSource
    {
        private readonly Random random = new(314);
        public int Next(int exclusiveUpperBound) => random.Next(exclusiveUpperBound);
    }
}
