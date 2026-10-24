using System.Reflection;
using System.Text.Json;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.Cards.History;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Games.AndarBahar;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class AndarBaharHistoryIntegrationTests
{
    [Fact]
    public async Task Paid_wins_and_losses_are_visible_in_shared_and_admin_history_with_linked_exact_ledger_money()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-andar-bahar-history-" + Guid.NewGuid().ToString("N"),
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        await database.Collection("users").Document("player").SetAsync(new Dictionary<string, object>
            { ["playerName"] = "Player", ["role"] = "player" });
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 100L, ["availableFractionalCents"] = 13L });
        var joker = new PlayingCard(CardRank.Ace, CardSuit.Spades);
        var match = new PlayingCard(CardRank.Ace, CardSuit.Hearts);
        var deck = new[] { joker, match }.Concat(StandardDeck.Create().Where(card => card != joker && card != match)).ToArray();
        var service = new AndarBaharService(new AndarBaharFirestoreStore(database), TimeProvider.System, () => deck);
        var win = await service.StartAsync("player", new("andar", .50m), "andar_shared_win_00001");
        var loss = await service.StartAsync("player", new("bahar", .50m), "andar_shared_loss_0001");
        Assert.Equal(100.08m, loss.Balance);
        var results = (await database.Collection("cardGameResults").GetSnapshotAsync()).Documents;
        Assert.Equal(2, results.Count);
        var project = typeof(CardRoomHistoryController).GetMethod("ToPublicItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var summaries = results.Select(result => Assert.IsType<CardRoomHistoryItemResponse>(project.Invoke(null, [result]))).ToArray();
        Assert.All(summaries, result =>
        {
            Assert.Equal("andar-bahar", result.Game);
            Assert.Equal("credit", result.Mode);
            Assert.Equal(.50m, result.WagerCredits);
            Assert.True(result.Unseen);
            Assert.False(result.RequiresClaim);
        });
        Assert.Contains(summaries, result => result.MatchId == win.RoundId && result.WinningsCredits == .95m && result.NetCredits == .45m);
        Assert.Contains(summaries, result => result.MatchId == loss.RoundId && result.WinningsCredits == 0m && result.NetCredits == -.50m);
        var json = JsonSerializer.Serialize(summaries, JsonSerializerOptions.Web);
        foreach (var secret in new[] { "userId", "joker", "deals", "recordJson", "idempotencyKey", "balance", "deck" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);

        var admin = new FirestoreAdminPlayersStore(database, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "andar-bahar-history-test-signing-key-long-enough", MaximumDocumentsPerCollection = 1000 }));
        var first = await admin.MatchesAsync("player", 1, null, default);
        Assert.Single(first.Items);
        Assert.NotNull(first.NextCursor);
        var second = await admin.MatchesAsync("player", 1, first.NextCursor, default);
        var matches = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(2, matches.Length);
        Assert.All(matches, result => Assert.Equal("andar-bahar", result.Game));
        Assert.Contains(matches, result => result.MatchId == win.RoundId && result.PaidCredits == .95m && result.NetCredits == .45m);
        Assert.Contains(matches, result => result.MatchId == loss.RoundId && result.PaidCredits == 0m && result.NetCredits == -.50m);
        Assert.Empty((await admin.MatchesAsync("other", 25, null, default)).Items);
        var walletEvents = (await admin.GamingAsync("player", 25, null, default)).Items;
        Assert.Equal(3, walletEvents.Count);
        Assert.All(walletEvents, value =>
        {
            Assert.Equal("andar-bahar", value.Game);
            Assert.Contains(value.MatchId, new[] { win.RoundId, loss.RoundId });
        });
        var detail = (await admin.DetailAsync("player", default))!;
        Assert.Equal(1m, detail.Summary.GamingWageredCredits);
        Assert.Equal(.95m, detail.Summary.GamingPaidCredits);
        Assert.Equal(-.05m, detail.Summary.GamingNetCredits);

        var winHistory = results.Single(result => result.GetValue<string>("matchId") == win.RoundId);
        var seen = Timestamp.FromDateTime(new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
        await winHistory.Reference.UpdateAsync("seenAt", seen);
        await service.StartAsync("player", new("andar", .50m), "andar_shared_win_00001");
        var updated = await winHistory.Reference.GetSnapshotAsync();
        Assert.Equal(seen, updated.GetValue<Timestamp>("seenAt"));
        Assert.False(Assert.IsType<CardRoomHistoryItemResponse>(project.Invoke(null, [updated])).Unseen);
        Assert.Equal(2, (await database.Collection("cardGameResults").GetSnapshotAsync()).Count);
        Assert.Equal(3, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }
}
