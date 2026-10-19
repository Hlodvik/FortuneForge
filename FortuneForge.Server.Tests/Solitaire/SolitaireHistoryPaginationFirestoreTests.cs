using FortuneForge.Server.Cards.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class SolitaireHistoryPaginationFirestoreTests
{
    private static readonly DateTime Start = new(2026, 8, 15, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PagesNewestFirst_AndMigratesMissingLegacyTimestamp()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-solitaire-history-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
        var store = new FirestoreCompetitiveSolitaireStore(database,
            new CompetitiveSolitaireOptions { AllowSingleHumanBotFill = false });
        await database.Collection("userBalances").Document("alice_slotsCredits").SetAsync(
            new Dictionary<string, object>
            {
                ["available"] = 100L,
                ["availableFractionalCents"] = 0L,
                ["version"] = 1L,
                ["updatedAt"] = Timestamp.FromDateTime(Start)
            });

        var expected = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            var now = Start.AddHours(index * 5);
            var session = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
                "alice", "Alice", 4, 500, 3, $"join-alice-{index}", 1001, now, default)).Session);
            expected.Add(session.MatchId);
            _ = await store.CommandAsync("alice", session.MatchId,
                new SolitaireCommandRequest(SolitaireCommandTypes.Submit, session.Version,
                    null, null, null, null),
                $"submit-alice-{index}", now.AddMinutes(1), default);
        }

        var legacyPlayer = Assert.Single((await database.Collection("solitaireMatchPlayers")
            .WhereEqualTo("matchId", expected[0]).GetSnapshotAsync()).Documents);
        await legacyPlayer.Reference.UpdateAsync("startedAt", FieldValue.Delete);

        var actual = new List<string>();
        string? cursor = null;
        do
        {
            var page = await store.GetHistoryPageAsync("alice", 2, cursor,
                Start.AddHours(30), default);
            Assert.InRange(page.Items.Count, 1, 2);
            actual.AddRange(page.Items.Select(item => item.MatchId));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(expected.AsEnumerable().Reverse(), actual);
        Assert.Equal(actual.Count, actual.Distinct(StringComparer.Ordinal).Count());
        legacyPlayer = await legacyPlayer.Reference.GetSnapshotAsync();
        Assert.Equal(Timestamp.FromDateTime(Start), legacyPlayer.GetValue<Timestamp>("startedAt"));
    }

    [Fact]
    public async Task CursorCannotBeUsedForAnotherAccount()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-solitaire-history-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
        var store = new FirestoreCompetitiveSolitaireStore(database,
            new CompetitiveSolitaireOptions { AllowSingleHumanBotFill = false });
        foreach (var user in new[] { "alice", "bob" })
        {
            await database.Collection("userBalances").Document($"{user}_slotsCredits").SetAsync(
                new Dictionary<string, object>
                {
                    ["available"] = 100L,
                    ["version"] = 1L,
                    ["updatedAt"] = Timestamp.FromDateTime(Start)
                });
        }
        var alice = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            "alice", "Alice", 4, 500, 3, "join-alice-history", 1001, Start, default)).Session);
        var player = Assert.Single((await database.Collection("solitaireMatchPlayers")
            .WhereEqualTo("matchId", alice.MatchId).GetSnapshotAsync()).Documents);

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetHistoryPageAsync(
            "bob", 2, player.Id, Start, default));
    }
}
