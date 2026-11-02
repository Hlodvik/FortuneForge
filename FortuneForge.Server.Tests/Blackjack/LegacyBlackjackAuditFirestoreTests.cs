using System.Text.Json;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Blackjack;

[Collection("Blackjack table Firestore emulator")]
public sealed class LegacyBlackjackAuditFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Theory]
    [InlineData("natural", 100, 250)]
    [InlineData("double", 200, 400)]
    [InlineData("bust", 100, 0)]
    public async Task Terminal_settlement_records_one_human_participation_and_retains_private_evidence(string scenario, long wager, long payout)
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = "demo-blackjack-audit-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
        }.Build();
        var store = new FirestoreBlackjackStore(db); const string user = "alice";
        var start = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        await db.Collection("userBalances").Document(user + "_slotsCredits").SetAsync(new Dictionary<string, object>
        { ["available"] = 100L, ["availableFractionalCents"] = 0L });
        var deck = scenario switch
        {
            "natural" => BlackjackRulesTests.Deck("A|spades", "9|clubs", "K|hearts", "7|diamonds"),
            "double" => BlackjackRulesTests.Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "10|clubs", "6|clubs"),
            _ => BlackjackRulesTests.Deck("10|spades", "9|clubs", "9|hearts", "7|diamonds", "K|clubs")
        };
        var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.StartAsync(user, "start-key", 100, deck, start, default)));
        var game = starts[0].Game;
        if (scenario != "natural")
        {
            Assert.Empty((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
            var action = scenario == "double" ? BlackjackActions.Double : BlackjackActions.Hit;
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.ActAsync(user, game.GameId, "action-key", game.Version, action, start.AddSeconds(5), default)));
        }
        var outbox = Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        Assert.Equal(wager, outbox.GetValue<long>("wagerCents")); Assert.Equal(payout, outbox.GetValue<long>("payoutCents"));
        Assert.Equal(user, outbox.GetValue<string>("userId")); Assert.Equal("paid", outbox.GetValue<string>("mode"));
        var audit = Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Equal("incomplete", audit.GetValue<string>("auditStatus"));
        var evidence = Assert.Single((await db.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        using var payload = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        Assert.Equal(52, payload.RootElement.GetProperty("data").GetProperty("game").GetProperty("deck").GetArrayLength());
        var recovered = await store.GetAsync(user, game.GameId, default);
        Assert.Equal(10000 - wager + payout, recovered!.BalanceCents);
        Assert.Equal(BlackjackStatuses.Completed, recovered.Game.Status);
    }
}
