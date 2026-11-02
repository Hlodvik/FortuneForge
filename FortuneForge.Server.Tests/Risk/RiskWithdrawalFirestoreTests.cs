using System.Text.Json;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Storage;
using FortuneForge.Server.Risk;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Risk;

[Collection("Blackjack table Firestore emulator")]
public sealed class RiskWithdrawalFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task Withdrawal_reservation_enforces_durable_state_before_any_wallet_or_provider_record_is_created()
    {
        var db = new FirestoreDbBuilder { ProjectId = "demo-risk-withdrawal-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
        var now = DateTime.UtcNow;
        await db.Collection("users").Document("alice").SetAsync(new { role = "player" });
        var balance = db.Collection("userBalances").Document("alice_slotsCredits");
        await balance.SetAsync(new { available = 50000L, version = 1L });
        var state = new PlayerRiskState("alice", false, true, [], new Dictionary<string, RiskLevelCounts>(), 1, now, "test", "test");
        var stateReference = FirestoreRiskIncidentStore.StateReference(db, "alice");
        await stateReference.SetAsync(new { stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        var withdrawal = new StoredPaymentWithdrawal("withdrawal-1", "", null, "alice", "idempotency-one", "merchantgateway", false,
            new("ZA", "South Africa", "ZAR", "en-ZA", "", "", 1, 1000, 100, [], []), 10, 1000, 1000, "received", now, now, null,
            new("Alice", "Test", "alice@example.test", "alice", "test"), new("Alice", "TestBank", "1234", "100", "savings"), "test");
        var store = new FirestorePaymentStore(db);
        var denied = await store.CreateWithdrawalReservationAsync(withdrawal, default);
        Assert.Equal(PaymentError.WithdrawalsRestricted, denied.Error);
        Assert.Equal(50000, (await balance.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await db.Collection("slotCreditWithdrawals").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await stateReference.DeleteAsync();
        Assert.NotNull((await store.CreateWithdrawalReservationAsync(withdrawal, default)).Value);
        Assert.Equal(49000, (await balance.GetSnapshotAsync()).GetValue<long>("available"));
    }
}
