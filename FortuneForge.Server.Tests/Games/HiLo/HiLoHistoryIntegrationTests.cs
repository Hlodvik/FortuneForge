using System.Reflection;
using System.Text.Json;
using FortuneForge.Games.Cards;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.Cards.History;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Games.HiLo;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class HiLoHistoryIntegrationTests
{
    [Fact]
    public async Task Actual_paid_settlement_is_readable_in_shared_and_admin_history_with_exact_money_but_no_private_cards()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-hi-lo-history-" + Guid.NewGuid().ToString("N"),
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        await database.Collection("users").Document("player").SetAsync(new Dictionary<string, object>
            { ["playerName"] = "Player", ["role"] = "player" });
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 100L, ["availableFractionalCents"] = 0L });
        var store = new HiLoFirestoreStore(database);
        var start = await new HiLoService(store, draw: () => new(CardRank.Seven, CardSuit.Clubs))
            .StartAsync("player", new(Guid.NewGuid().ToString("N"), 100));
        var win = await new HiLoService(store, draw: () => new(CardRank.King, CardSuit.Hearts))
            .ActAsync("player", start.Round!.RoundId, new(Guid.NewGuid().ToString("N"), 0, "higher"));
        var cashout = new HiLoActionRequest(Guid.NewGuid().ToString("N"), win.Round!.Revision, "cashout");
        await new HiLoService(store).ActAsync("player", start.Round.RoundId, cashout);
        var result = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        var project = typeof(CardRoomHistoryController).GetMethod("ToPublicItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var publicResult = Assert.IsType<CardRoomHistoryItemResponse>(project.Invoke(null, [result]));
        Assert.Equal("hi-lo", publicResult.Game);
        Assert.Equal(1m, publicResult.WagerCredits);
        Assert.Equal(2.14m, publicResult.WinningsCredits);
        Assert.Equal(1.14m, publicResult.NetCredits);
        Assert.True(publicResult.Unseen);
        Assert.False(publicResult.RequiresClaim);
        var json = JsonSerializer.Serialize(publicResult, JsonSerializerOptions.Web);
        foreach (var secret in new[] { "userId", "currentCard", "history", "roundJson", "operationId", "balance" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);

        var admin = new FirestoreAdminPlayersStore(database, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "hi-lo-admin-test-signing-key-long-enough", MaximumDocumentsPerCollection = 1000 }));
        var match = Assert.Single((await admin.MatchesAsync("player", 25, null, default)).Items);
        Assert.Equal("hi-lo", match.Game);
        Assert.Equal(1m, match.WageredCredits);
        Assert.Equal(2.14m, match.PaidCredits);
        var walletEvents = (await admin.GamingAsync("player", 25, null, default)).Items;
        Assert.Equal(2, walletEvents.Count);
        Assert.All(walletEvents, value => Assert.Equal("hi-lo", value.Game));
        var detail = (await admin.DetailAsync("player", default))!;
        Assert.Equal(1m, detail.Summary.GamingWageredCredits);
        Assert.Equal(2.14m, detail.Summary.GamingPaidCredits);
        Assert.Equal(1.14m, detail.Summary.GamingNetCredits);

        var seen = Timestamp.FromDateTime(new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc));
        await result.Reference.UpdateAsync("seenAt", seen);
        await new HiLoService(store).ActAsync("player", start.Round.RoundId, cashout);
        Assert.Equal(seen, (await result.Reference.GetSnapshotAsync()).GetValue<Timestamp>("seenAt"));
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
    }
}
