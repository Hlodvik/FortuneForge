using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Operations;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class AdminHoldemBuyInAccountingTests(CreditHoldemFirestoreEmulatorFixture fixture)
    : IClassFixture<CreditHoldemFirestoreEmulatorFixture>
{
    [Fact]
    public async Task RecognizesSignedHouseFundedHandResultsAndRejectsMoneyCreation()
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = $"demo-holdem-admin-{Guid.NewGuid():N}",
            Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = EmulatorDetection.None
        }.Build();
        var now = DateTime.UtcNow;
        Dictionary<string, object> Revenue(long wager, long paid, long houseChips, long net) => new()
        {
            ["currencyId"] = "slotsCredits", ["financialClassification"] = "escrow-house-backed-v1",
            ["humanWagerCents"] = wager, ["humanPayoutCents"] = paid, ["houseNetCents"] = net,
            ["botFinancialContributionCents"] = houseChips, ["humanPlayerCount"] = 1L,
            ["recognizedAt"] = Timestamp.FromDateTime(now)
        };
        await db.Collection("creditHoldemMatchRevenue").Document("house-funds-win")
            .SetAsync(Revenue(100, 300, 200, -200));
        await db.Collection("creditHoldemMatchRevenue").Document("house-wins")
            .SetAsync(Revenue(100, 0, 200, 100));
        await db.Collection("creditHoldemMatchRevenue").Document("impossible-award")
            .SetAsync(Revenue(100, 301, 200, -201));
        await db.Collection("creditHoldemMatchRevenue").Document("incorrect-net")
            .SetAsync(Revenue(100, 300, 200, 0));
        var store = new FirestoreAdminOperationsStore(db, Options.Create(new AdminOperationsOptions
        {
            MaximumDocumentsPerCollection = 100, MaximumRangeDays = 31,
            CursorSigningKey = "test-only-holdem-buy-in-admin-audit-key"
        }));
        var snapshot = await store.ReadAsync(new(now.AddMinutes(-1), now.AddMinutes(1)), default);
        var hands = snapshot.Financial.Where(row => row.Game == "texas-holdem").ToArray();
        Assert.Equal(2, hands.Length);
        Assert.Contains(hands, row => row.HouseNetCredits == -2m && row.PaidCredits == 3m && row.HouseFundedEntryCredits == 2m);
        Assert.Contains(hands, row => row.HouseNetCredits == 1m && row.PaidCredits == 0m);
        var finding = Assert.Single(snapshot.SourceFindings, row => row.Id == "credit-holdem-revenue-contract");
        Assert.Equal(4, finding.RecordsChecked);
        Assert.Equal(2, finding.Findings);
    }
}
