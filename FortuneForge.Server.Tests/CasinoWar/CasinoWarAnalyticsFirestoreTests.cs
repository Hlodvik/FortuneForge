using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.CasinoWar;

[Collection("Blackjack table Firestore emulator")]
public sealed class CasinoWarAnalyticsFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private FirestoreDb Database() => new FirestoreDbBuilder { ProjectId = $"demo-war-analytics-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint, ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
    private static IReadOnlyList<PlayingCard> Deck(params string[] opening) => opening.Concat(new[] { "2|spades", "3|hearts", "4|diamonds", "5|clubs", "6|spades", "7|hearts", "8|diamonds", "9|clubs" }).Take(10).Select(CardCode.Parse).ToArray();
    private static Task Seed(FirestoreDb db) => db.Collection("userBalances").Document("alice_slotsCredits").SetAsync(new { available = 100L, availableFractionalCents = 0L });

    [Fact]
    public async Task Completed_opening_can_reload_and_retry_with_one_audit_and_receipt()
    {
        var db = Database(); await Seed(db); var store = new CasinoWarFirestoreStore(db); var deck = Deck("A|spades", "2|hearts");
        var first = await store.StartAsync("alice", "opening", 100, 0, deck, Now, default);
        Assert.Equal(CasinoWarRoundPhase.Completed, first.Round.Round.Phase);
        var replay = await store.StartAsync("alice", "opening", 100, 0, deck, Now, default);
        var loaded = await store.GetAsync("alice", first.Round.RoundId, default);
        Assert.NotNull(loaded); Assert.Equal(first.BalanceCents, replay.BalanceCents); Assert.Equal(first.Round.RoundId, loaded.Round.RoundId);
        var receipt = Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal(100, receipt.GetValue<long>("wagerCents")); Assert.Equal(200, receipt.GetValue<long>("payoutCents"));
        Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Tie_decision_receipt_includes_both_stakes_and_paid_sidebet_once()
    {
        var db = Database(); await Seed(db); var store = new CasinoWarFirestoreStore(db); var deck = Deck("A|spades", "A|hearts");
        var first = await store.StartAsync("alice", "opening", 100, 100, deck, Now, default);
        Assert.Empty((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        var decided = await store.DecideAsync("alice", first.Round.RoundId, "war", CasinoWarTieDecision.GoToWar, Now.AddSeconds(5), default);
        var again = await store.DecideAsync("alice", first.Round.RoundId, "war", CasinoWarTieDecision.GoToWar, Now.AddSeconds(6), default);
        var openingReplay = await store.StartAsync("alice", "opening", 100, 100, deck, Now.AddSeconds(7), default);
        Assert.Equal(decided.BalanceCents, again.BalanceCents); Assert.Equal(decided.Round.RoundId, openingReplay.Round.RoundId);
        var receipt = Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal(300, receipt.GetValue<long>("wagerCents"));
        var expectedReturn = checked((long)((decided.Round.Round.Settlement!.TotalReturn + decided.Round.TieSettlement!.TotalReturn) * 100));
        Assert.Equal(expectedReturn, receipt.GetValue<long>("payoutCents"));
        Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
    }
}
