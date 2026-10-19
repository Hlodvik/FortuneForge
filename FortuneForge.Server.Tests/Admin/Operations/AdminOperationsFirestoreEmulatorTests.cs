using FortuneForge.Server.Admin.Operations;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Operations;

public sealed class AdminOperationsFirestoreEmulatorTests
{
    [Fact]
    public async Task FirestoreStore_ReadsOnlySanitizedOperationalFieldsAndWritesSafeAudit()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
        {
            return;
        }

        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-fortuneforge-admin-tests",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
        var suffix = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var documents = new[]
        {
            database.Collection("slotSpinResults").Document($"admin-slot-{suffix}"),
            database.Collection("blackjackGames").Document($"admin-blackjack-{suffix}"),
            database.Collection("solitaireMatchRevenue").Document($"admin-solitaire-revenue-{suffix}"),
            database.Collection("slotCreditPurchases").Document($"admin-purchase-{suffix}"),
            database.Collection("slotCreditWithdrawals").Document($"admin-withdrawal-{suffix}"),
            database.Collection("solitaireQueuePartitions").Document($"admin-queue-{suffix}"),
            database.Collection("solitaireMatches").Document($"admin-match-{suffix}"),
            database.Collection("managedPlayerAssignments").Document($"admin-managed-assignment-{suffix}"),
            database.Collection("creditHoldemMatchRevenue").Document($"admin-holdem-valid-{suffix}"),
            database.Collection("creditHoldemMatchRevenue").Document($"admin-holdem-formula-poison-{suffix}"),
            database.Collection("creditHoldemMatchRevenue").Document($"admin-holdem-bot-poison-{suffix}"),
            database.Collection("solitaireMatchRevenue").Document($"admin-solitaire-poison-{suffix}"),
            database.Collection("blackjackTableRoundRevenue").Document($"admin-blackjack-table-win-{suffix}"),
            database.Collection("blackjackTableRoundRevenue").Document($"admin-blackjack-table-loss-{suffix}"),
            database.Collection("blackjackTableRoundRevenue").Document($"admin-blackjack-table-poison-{suffix}"),
            database.Collection("solitaireMatchRevenue").Document($"admin-solitaire-house-human-win-{suffix}"),
            database.Collection("solitaireMatchRevenue").Document($"admin-solitaire-house-bot-win-{suffix}"),
            database.Collection("solitaireMatchRevenue").Document($"admin-solitaire-house-poison-{suffix}"),
            database.Collection("solitaireMatches").Document($"admin-solitaire-house-match-{suffix}"),
            database.Collection("solitaireMatchRevenue").Document($"admin-solitaire-house-all-human-{suffix}")
        };

        await Task.WhenAll(
            documents[0].SetAsync(new Dictionary<string, object>
            {
                ["createdAt"] = Timestamp.FromDateTime(now.AddMinutes(-8)),
                ["wageredSlotsCredits"] = 10d,
                ["wonSlotsCredits"] = 6d,
                ["userId"] = "must-not-leak"
            }),
            documents[1].SetAsync(new Dictionary<string, object>
            {
                ["createdAt"] = Timestamp.FromDateTime(now.AddMinutes(-7)),
                ["updatedAt"] = Timestamp.FromDateTime(now.AddMinutes(-6)),
                ["status"] = "completed",
                ["totalWagerCents"] = 500L,
                ["payoutCents"] = 750L,
                ["deck"] = new[] { "secret-card" }
            }),
            documents[2].SetAsync(new Dictionary<string, object>
            {
                ["recognizedAt"] = Timestamp.FromDateTime(now.AddMinutes(-5)),
                ["grossPoolCents"] = 2_000L,
                ["winnerPayoutCents"] = 1_800L,
                ["platformFeeCents"] = 200L,
                ["currencyId"] = "slotsCredits",
                ["financialClassification"] = "real-human-pool-v1",
                ["botFinancialContributionCents"] = 0L,
                ["humanPlayerCount"] = 4L,
                ["winnerUserId"] = "must-not-leak"
            }),
            documents[3].SetAsync(new Dictionary<string, object>
            {
                ["statusUpdatedAt"] = Timestamp.FromDateTime(now.AddMinutes(-4)),
                ["status"] = "completed",
                ["credits"] = 100L,
                ["customerEmail"] = "must-not-leak@example.invalid"
            }),
            documents[4].SetAsync(new Dictionary<string, object>
            {
                ["statusUpdatedAt"] = Timestamp.FromDateTime(now.AddMinutes(-3)),
                ["status"] = "completed",
                ["creditsDebited"] = 25L,
                ["bankAccountNumber"] = "must-not-leak"
            }),
            documents[5].SetAsync(new Dictionary<string, object>
            {
                ["updatedAt"] = Timestamp.FromDateTime(now.AddMinutes(-2)),
                ["playerCount"] = 4L,
                ["buyInCents"] = 500L,
                ["ticketIds"] = new[] { "private-ticket-a", "private-ticket-b" }
            }),
            documents[6].SetAsync(new Dictionary<string, object>
            {
                ["startedAt"] = Timestamp.FromDateTime(now.AddMinutes(-5)),
                ["completedAt"] = Timestamp.FromDateTime(now.AddMinutes(-1)),
                ["status"] = "settled",
                ["playerCount"] = 4L,
                ["prizePoolCents"] = 2_000L,
                ["winnerPayoutCents"] = 1_800L,
                ["platformFeeCents"] = 200L,
                ["dealSeed"] = 123L,
                ["playerIds"] = new[] { "private-player" }
            }),
            documents[7].SetAsync(new Dictionary<string, object>
            {
                ["gameId"] = "blackjack",
                ["profileId"] = "must-not-leak",
                ["assignmentId"] = "must-not-leak",
                ["reservedAt"] = Timestamp.FromDateTime(now.AddMinutes(-1)),
                ["heartbeatAt"] = Timestamp.FromDateTime(now.AddMinutes(-1)),
                ["leaseUntil"] = Timestamp.FromDateTime(now.AddMinutes(29)),
                ["schemaVersion"] = 1L
            }),
            documents[8].SetAsync(CreditHoldemRevenue(now.AddSeconds(-45), 4_000L, 3_600L, 400L, 0L)),
            documents[9].SetAsync(CreditHoldemRevenue(now.AddSeconds(-40), 4_000L, 3_600L, 350L, 0L)),
            documents[10].SetAsync(CreditHoldemRevenue(now.AddSeconds(-35), 4_000L, 3_600L, 400L, 50L)),
            documents[11].SetAsync(SolitaireRevenue(now.AddSeconds(-30), 2_000L, 1_800L, 150L, 0L)),
            documents[12].SetAsync(BlackjackTableRevenue(now.AddSeconds(-25), 1_000L, 800L, 200L, 0L)),
            documents[13].SetAsync(BlackjackTableRevenue(now.AddSeconds(-20), 1_000L, 1_500L, -500L, 0L)),
            documents[14].SetAsync(BlackjackTableRevenue(now.AddSeconds(-15), 1_000L, 800L, 200L, 25L)),
            documents[15].SetAsync(HouseFundedSolitaireRevenue(now.AddSeconds(-14), 100L, 300L, 360L, 0L, 40L, -260L, 1L, 3L)),
            documents[16].SetAsync(HouseFundedSolitaireRevenue(now.AddSeconds(-13), 200L, 200L, 0L, 360L, 40L, 200L, 2L, 2L)),
            documents[17].SetAsync(HouseFundedSolitaireRevenue(now.AddSeconds(-12), 100L, 300L, 360L, 0L, 40L, 0L, 1L, 3L)),
            documents[18].SetAsync(new Dictionary<string, object>
            {
                ["startedAt"] = Timestamp.FromDateTime(now.AddSeconds(-14)),
                ["completedAt"] = Timestamp.FromDateTime(now.AddSeconds(-11)),
                ["status"] = "settled",
                ["financialModel"] = "house-funded-bot-pool-v1",
                ["playerCount"] = 4L,
                ["humanEntryCents"] = 100L,
                ["houseFundedEntryCents"] = 300L,
                ["prizePoolCents"] = 400L,
                ["winnerPayoutCents"] = 360L,
                ["humanPayoutCents"] = 360L,
                ["houseNetCents"] = -260L,
                ["platformFeeCents"] = 40L,
                ["dealSeed"] = 314L
            }),
            documents[19].SetAsync(HouseFundedSolitaireRevenue(now.AddSeconds(-10), 200L, 0L, 180L, 0L, 20L, 20L, 2L, 0L))
            );

        try
        {
            var store = new FirestoreAdminOperationsStore(database, Options.Create(new AdminOperationsOptions
            {
                MaximumDocumentsPerCollection = 100,
                MaximumRangeDays = 31,
                CursorSigningKey = "test-only-admin-cursor-signing-key-0001"
            }));
            var snapshot = await store.ReadAsync(
                new AdminOperationsRange(now.AddHours(-1), now.AddHours(1)),
                CancellationToken.None);

            Assert.Contains(snapshot.Financial, record => record.Id == FirestoreAdminOperationsStore.OpaqueId("slot", documents[0].Id));
            Assert.Contains(snapshot.Financial, record => record.Game == "blackjack" && record.HouseNetCredits == -2.5m);
            Assert.Contains(snapshot.Financial, record => record.Game == "solitaire" && record.PlatformFeeCredits == 2m);
            var holdem = Assert.Single(snapshot.Financial, record => record.Game == "texas-holdem");
            Assert.Equal(4m, holdem.PlatformFeeCredits);
            Assert.Equal(FirestoreAdminOperationsStore.OpaqueId("credit-holdem", documents[8].Id), holdem.Id);
            var holdemFinding = Assert.Single(snapshot.SourceFindings, finding =>
                finding.Id == "credit-holdem-revenue-contract");
            Assert.Equal(3, holdemFinding.RecordsChecked);
            Assert.Equal(2, holdemFinding.Findings);
            Assert.False(snapshot.Complete);
            Assert.Contains(snapshot.Limitations, limitation =>
                limitation.Contains("2 credit Hold'em revenue", StringComparison.Ordinal));
            var solitaireFinding = Assert.Single(snapshot.SourceFindings, finding =>
                finding.Id == "solitaire-revenue-contract");
            Assert.Equal(6, solitaireFinding.RecordsChecked);
            Assert.Equal(2, solitaireFinding.Findings);
            Assert.Equal(4, snapshot.Financial.Count(record => record.Game == "solitaire"));
            Assert.Contains(snapshot.Financial, record =>
                record.Id == FirestoreAdminOperationsStore.OpaqueId("solitaire", documents[15].Id) &&
                record.HouseNetCredits == -2.6m && record.HouseFundedEntryCredits == 3m);
            Assert.Contains(snapshot.Financial, record =>
                record.Id == FirestoreAdminOperationsStore.OpaqueId("solitaire", documents[16].Id) &&
                record.HouseNetCredits == 2m && record.BotPrizeCredits == 3.6m);
            Assert.Contains(snapshot.Financial, record =>
                record.Id == FirestoreAdminOperationsStore.OpaqueId("solitaire", documents[19].Id) &&
                record.HouseNetCredits == 0.2m && record.HouseFundedEntryCredits == 0m);
            Assert.Contains(snapshot.Matches, match =>
                match.MatchId == FirestoreAdminOperationsStore.OpaqueId("solitaire-match", documents[18].Id) &&
                match.WageredCredits == 1m && match.PaidCredits == 3.6m && match.HouseNetCredits == -2.6m);
            var blackjackTableFinding = Assert.Single(snapshot.SourceFindings, finding =>
                finding.Id == "blackjack-table-revenue-contract");
            Assert.Equal(3, blackjackTableFinding.RecordsChecked);
            Assert.Equal(1, blackjackTableFinding.Findings);
            Assert.Contains(snapshot.Financial, record =>
                record.Id == FirestoreAdminOperationsStore.OpaqueId("blackjack-table", documents[13].Id) &&
                record.HouseNetCredits == -5m);
            Assert.Contains(snapshot.Matches, match =>
                match.MatchId == FirestoreAdminOperationsStore.OpaqueId("blackjack-table-match", documents[13].Id) &&
                match.HouseNetCredits == -5m);
            Assert.Contains(snapshot.Funding, record => record.Category == "purchase" && record.Credits == 100m);
            Assert.Contains(snapshot.Funding, record => record.Category == "withdrawal" && record.Credits == 25m);
            Assert.Contains(snapshot.BotLeases, record => record.Game == "blackjack" && !record.Completed);

            await store.AppendAuthorizedAccessAuditAsync(
                $"admin-{suffix}",
                "overview",
                now,
                CancellationToken.None);
            var audits = await database.Collection("adminOperationsAudit")
                .WhereEqualTo("operation", "overview")
                .GetSnapshotAsync();
            var audit = Assert.Single(audits.Documents, document =>
                document.TryGetValue<string>("actorHash", out var value) &&
                value == FirestoreAdminOperationsStore.OpaqueId("admin-actor", $"admin-{suffix}"));
            var fields = audit.ToDictionary().Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "actorHash", "occurredAt", "operation", "schemaVersion" }, fields);
        }
        finally
        {
            await Task.WhenAll(documents.Select(document => document.DeleteAsync()));
        }
    }

    private static Dictionary<string, object> CreditHoldemRevenue(
        DateTime recognizedAtUtc,
        long poolCents,
        long payoutCents,
        long feeCents,
        long botContributionCents) => new()
    {
        ["recognizedAt"] = Timestamp.FromDateTime(recognizedAtUtc),
        ["currencyId"] = "slotsCredits",
        ["financialClassification"] = "real-human-pool-v1",
        ["botFinancialContributionCents"] = botContributionCents,
        ["humanPrizePoolCents"] = poolCents,
        ["humanPayoutCents"] = payoutCents,
        ["platformFeeCents"] = feeCents,
        ["humanPlayerCount"] = 4L,
        ["playerIds"] = new[] { "must-not-leak" },
        ["privateBoard"] = "must-not-leak"
    };

    private static Dictionary<string, object> SolitaireRevenue(
        DateTime recognizedAtUtc,
        long poolCents,
        long payoutCents,
        long feeCents,
        long botContributionCents) => new()
    {
        ["recognizedAt"] = Timestamp.FromDateTime(recognizedAtUtc),
        ["currencyId"] = "slotsCredits",
        ["financialClassification"] = "real-human-pool-v1",
        ["botFinancialContributionCents"] = botContributionCents,
        ["grossPoolCents"] = poolCents,
        ["winnerPayoutCents"] = payoutCents,
        ["platformFeeCents"] = feeCents,
        ["humanPlayerCount"] = 4L,
        ["winnerUserId"] = "must-not-leak"
    };

    private static Dictionary<string, object> HouseFundedSolitaireRevenue(
        DateTime recognizedAtUtc,
        long humanEntryCents,
        long houseFundedEntryCents,
        long humanPayoutCents,
        long botPrizeCents,
        long platformFeeCents,
        long houseNetCents,
        long humanPlayerCount,
        long managedPlayerCount) => new()
    {
        ["recognizedAt"] = Timestamp.FromDateTime(recognizedAtUtc),
        ["currencyId"] = "slotsCredits",
        ["financialClassification"] = "house-funded-bot-pool-v1",
        ["botFinancialContributionCents"] = 0L,
        ["humanEntryCents"] = humanEntryCents,
        ["houseFundedEntryCents"] = houseFundedEntryCents,
        ["grossPoolCents"] = humanEntryCents + houseFundedEntryCents,
        ["winnerPayoutCents"] = humanPayoutCents + botPrizeCents,
        ["humanPayoutCents"] = humanPayoutCents,
        ["botPrizeCents"] = botPrizeCents,
        ["platformFeeCents"] = platformFeeCents,
        ["houseNetCents"] = houseNetCents,
        ["humanPlayerCount"] = humanPlayerCount,
        ["managedPlayerCount"] = managedPlayerCount,
        ["winnerUserId"] = "must-not-leak"
    };

    private static Dictionary<string, object> BlackjackTableRevenue(
        DateTime recognizedAtUtc,
        long wagerCents,
        long payoutCents,
        long houseNetCents,
        long botContributionCents) => new()
    {
        ["recognizedAt"] = Timestamp.FromDateTime(recognizedAtUtc),
        ["currencyId"] = "slotsCredits",
        ["financialClassification"] = "real-human-dealer-counterparty-v1",
        ["botFinancialContributionCents"] = botContributionCents,
        ["humanWagerCents"] = wagerCents,
        ["humanPayoutCents"] = payoutCents,
        ["houseNetCents"] = houseNetCents,
        ["humanPlayerCount"] = 2L,
        ["roundId"] = "must-not-leak",
        ["tableId"] = "must-not-leak"
    };
}
