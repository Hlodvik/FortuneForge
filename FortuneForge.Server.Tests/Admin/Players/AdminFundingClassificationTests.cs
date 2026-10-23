using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Players;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class AdminFundingClassificationTests
{
    [Fact]
    public Task Existing_operations_emulator_contract_still_preserves_all_supported_financial_sources() =>
        new FortuneForge.Server.Tests.Admin.Operations.AdminOperationsFirestoreEmulatorTests()
            .FirestoreStore_ReadsOnlySanitizedOperationalFieldsAndWritesSafeAudit();

    [Fact]
    public async Task Overview_separates_real_and_simulated_funding_and_never_assumes_unknown_classification_is_real()
    {
        var db = Database();
        var now = DateTime.UtcNow;
        await Funding(db, "slotCreditPurchases", "real", 10, false, now);
        await Funding(db, "slotCreditPurchases", "mock", 1000, true, now);
        await Funding(db, "slotCreditPurchases", "unknown", 2000, null, now);
        await Funding(db, "slotCreditWithdrawals", "real", 1, false, now);
        await Funding(db, "slotCreditWithdrawals", "mock", 100, true, now);
        var options = Options.Create(new AdminOperationsOptions { CursorSigningKey = "admin-funding-test-signing-key-long-enough" });
        var service = new AdminOperationsService(new FirestoreAdminOperationsStore(db, options), options);
        var overview = await service.OverviewAsync(new(now.AddHours(-1), now.AddHours(1)), default);
        Assert.Equal(10m, overview.Funding.CompletedPurchaseCredits);
        Assert.Equal(1, overview.Funding.CompletedPurchases);
        Assert.Equal(1m, overview.Funding.CompletedWithdrawalCredits);
        Assert.Equal(1, overview.Funding.CompletedWithdrawals);
        Assert.Equal(1000m, overview.Funding.CompletedMockPurchaseCredits);
        Assert.Equal(1, overview.Funding.CompletedMockPurchases);
        Assert.Equal(100m, overview.Funding.CompletedMockWithdrawalCredits);
        Assert.Equal(1, overview.Funding.CompletedMockWithdrawals);
        Assert.Equal(0m, overview.HouseGamingNetCredits);
        Assert.False(overview.Complete);
        Assert.Contains(overview.Limitations, item => item.Contains("real/mock classification"));
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Player_summary_keeps_unclassified_purchases_unknown_instead_of_treating_them_as_real()
    {
        var db = Database(); var now = DateTime.UtcNow;
        await db.Collection("users").Document("alice").SetAsync(new Dictionary<string, object> { ["playerName"] = "Alice" });
        await Funding(db, "slotCreditPurchases", "unknown", 2000, null, now);
        var store = new FirestoreAdminPlayersStore(db, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "admin-funding-test-signing-key-long-enough" }));
        var detail = (await store.DetailAsync("alice", default))!;
        Assert.Equal(0m, detail.Summary.PurchaseCredits);
        Assert.Equal(0m, detail.Summary.MockPurchaseCredits);
        Assert.False(detail.Summary.Complete);
        var page = await store.PurchasesAsync("alice", 25, null, default);
        Assert.False(Assert.Single(page.Items).MockClassificationKnown);
        Assert.False(page.Complete);
    }

    private static FirestoreDb Database() => new FirestoreDbBuilder
        { ProjectId = "demo-admin-funding-" + Guid.NewGuid().ToString("N"), EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
    private static Task Funding(FirestoreDb db, string collection, string id, long credits, bool? mock, DateTime now)
    {
        var fields = new Dictionary<string, object> { ["userId"] = "alice", ["status"] = "completed",
            ["credits"] = credits, ["creditsDebited"] = credits, ["statusUpdatedAt"] = Timestamp.FromDateTime(now) };
        if (mock is not null) fields["isMock"] = mock.Value;
        return db.Collection(collection).Document(id).SetAsync(fields);
    }
}
