using FortuneForge.Server.Cards.History;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.History;

[Collection("Blackjack table Firestore emulator")]
public sealed class CardRoomHistoryQueryTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(100)]
    public async Task Latest_results_beyond_first_hundred_are_indexed_owner_scoped_and_bounded(int limit)
    {
        var database = Database();
        var rows = Enumerable.Range(0, 165).Select(index => Row($"result_{index:D4}",
            Data("blackjack", "completedAt", Now.AddMinutes(index)))).ToList();
        var foreign = Data("blackjack", "completedAt", Now.AddYears(1));
        foreign["userId"] = "bob";
        rows.Add(Row("foreign", foreign));
        rows.Add(Row("unsupported", Data("unrecognized-game", "completedAt", Now.AddYears(1))));
        await Seed(database, rows);

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", limit);

        Assert.True(result.IsComplete);
        Assert.Equal(Enumerable.Range(0, 165).Reverse().Take(limit).Select(index => $"result_{index:D4}"),
            result.Items.Select(item => item.ResultId));
        Assert.InRange(result.DocumentsRead, limit, 164);
        Assert.IsType<OkObjectResult>(CardRoomHistoryController.HistoryResponse(result));
    }

    [Fact]
    public async Task Overridden_later_fields_cannot_bury_a_newer_fallback_result()
    {
        var database = Database();
        var rows = Enumerable.Range(0, 120).Select(index =>
        {
            var fields = Data("house-holdem", "completedAt", Now.AddDays(-30));
            fields["recognizedAt"] = Stamp(Now.AddDays(30));
            return Row($"override_{index:D4}", fields);
        }).ToList();
        rows.AddRange(Enumerable.Range(0, 39).Select(index => Row($"recent_{index:D4}",
            Data("texas-holdem", "completedAt", Now.AddMinutes(index)))));
        rows.Add(Row("legacy-fallback", Data("hi-lo", "recognizedAt", Now.AddDays(-1))));
        await Seed(database, rows);

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", 40);

        Assert.True(result.IsComplete);
        Assert.Equal(40, result.Items.Count);
        Assert.Equal("legacy-fallback", result.Items[^1].ResultId);
        Assert.DoesNotContain(result.Items, item => item.ResultId.StartsWith("override_", StringComparison.Ordinal));
        Assert.True(result.DocumentsRead > 128);
    }

    [Fact]
    public async Task Entire_timestamp_tie_is_considered_before_ordinal_result_id_order()
    {
        var database = Database();
        var ids = Enumerable.Range(0, 99).Select(index => $"result_{index:D4}")
            .Concat(["result_\uE000", "result_\U0001F000"]).ToArray();
        await Seed(database, ids.Select(id => Row(id, Data("three-card-poker", "completedAt", Now))));

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", 100);

        Assert.True(result.IsComplete);
        Assert.Equal(ids.Order(StringComparer.Ordinal).Take(100), result.Items.Select(item => item.ResultId));
        Assert.Contains(result.Items, item => item.ResultId == "result_\U0001F000");
        Assert.DoesNotContain(result.Items, item => item.ResultId == "result_\uE000");
    }

    [Fact]
    public async Task Missing_null_and_malformed_timestamp_fields_keep_the_supported_fallback_priority()
    {
        var database = Database();
        var completed = Data("blackjack", "completedAt", Now);
        completed["settledAt"] = Stamp(Now.AddDays(50));
        completed["recognizedAt"] = Stamp(Now.AddDays(60));
        completed["startedAt"] = Stamp(Now.AddDays(70));
        var settled = Data("andar-bahar", "settledAt", Now.AddDays(-1));
        settled["completedAt"] = "not-a-timestamp";
        var recognized = Data("mines", "recognizedAt", Now.AddDays(-2));
        recognized["completedAt"] = null!;
        recognized["settledAt"] = 123L;
        var started = Data("texas-holdem", "startedAt", Now.AddDays(-3));
        await Seed(database, [Row("completed", completed), Row("settled", settled),
            Row("recognized", recognized), Row("started", started)]);

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", 40);

        Assert.True(result.IsComplete);
        Assert.Equal(["completed", "settled", "recognized", "started"], result.Items.Select(item => item.ResultId));
        Assert.Equal(Now, result.Items[0].CompletedAtUtc);
        Assert.Equal(Now.AddDays(70), result.Items[0].StartedAtUtc);
        Assert.Null(result.Items[^1].CompletedAtUtc);
        Assert.Equal(Now.AddDays(-3), result.Items[^1].StartedAtUtc);
    }

    [Fact]
    public async Task More_than_hundred_undated_results_keep_unix_epoch_fallback_and_ordinal_ties()
    {
        var database = Database();
        var undatedIds = Enumerable.Range(0, 104).Select(index => $"undated_{index:D4}")
            .Concat(["undated_\uE000", "undated_\U0001F000"]).ToArray();
        var rows = undatedIds.Select(id => Row(id, new Dictionary<string, object>
        {
            ["userId"] = "alice", ["game"] = "hi-lo", ["completedAt"] = null!,
            ["settledAt"] = "invalid", ["recognizedAt"] = 0L
        })).ToList();
        rows.Add(Row("modern", Data("blackjack", "completedAt", Now)));
        rows.Add(Row("before-epoch", Data("blackjack", "startedAt", DateTime.UnixEpoch.AddDays(-1))));
        await Seed(database, rows);

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", 100);

        Assert.True(result.IsComplete);
        Assert.Equal(new[] { "modern" }.Concat(undatedIds.Order(StringComparer.Ordinal).Take(99)),
            result.Items.Select(item => item.ResultId));
        Assert.All(result.Items.Skip(1), item =>
        {
            Assert.Equal(DateTime.UnixEpoch, item.StartedAtUtc);
            Assert.Null(item.CompletedAtUtc);
        });
    }

    [Fact]
    public async Task Claimable_solitaire_preserves_completion_order_claim_state_and_exact_money_without_writes()
    {
        var database = Database();
        var claimable = Data("solitaire", "completedAt", Now.AddDays(-1));
        claimable["claimStatus"] = "unclaimed";
        claimable["settlementStatus"] = "claimable";
        claimable["claimableAt"] = Stamp(Now.AddDays(10));
        claimable["claimedAt"] = Stamp(Now.AddDays(11));
        claimable["seenAt"] = Stamp(Now.AddDays(12));
        claimable["buyInCents"] = 500L;
        claimable["payoutCents"] = 1_234L;
        claimable["netCents"] = 734L;
        claimable["score"] = 820L;
        claimable["moves"] = 74L;
        await Seed(database, [Row("claimable", claimable), Row("newer", Data("blackjack", "completedAt", Now))]);
        var reference = database.Collection("cardGameResults").Document("claimable");
        var before = await reference.GetSnapshotAsync();

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", 40);

        Assert.True(result.IsComplete);
        Assert.Equal(["newer", "claimable"], result.Items.Select(item => item.ResultId));
        var item = result.Items[1];
        Assert.True(item.RequiresClaim);
        Assert.True(item.Unseen);
        Assert.Equal(12.34m, item.WinningsCredits);
        Assert.Equal(5m, item.WagerCredits);
        Assert.Equal(7.34m, item.NetCredits);
        Assert.Equal(820, item.Score);
        Assert.Equal(74, item.Moves);
        Assert.Equal(Now.AddDays(-1), item.CompletedAtUtc);
        Assert.Equal(before.UpdateTime, (await reference.GetSnapshotAsync()).UpdateTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unprovable_dated_ties_or_undated_history_stop_at_budget_and_return_503(bool dated)
    {
        var database = Database();
        await Seed(database, Enumerable.Range(0, CardRoomHistoryController.HistoryDocumentBudget + 1)
            .Select(index => Row($"result_{index:D4}", dated
                ? Data("blackjack", "completedAt", Now)
                : new Dictionary<string, object> { ["userId"] = "alice", ["game"] = "blackjack" })));

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", 40);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Items);
        Assert.Equal(CardRoomHistoryController.HistoryDocumentBudget, result.DocumentsRead);
        var response = Assert.IsType<ObjectResult>(CardRoomHistoryController.HistoryResponse(result));
        Assert.Equal(503, response.StatusCode);
    }

    [Fact]
    public async Task Empty_owned_supported_history_is_a_complete_empty_page()
    {
        var database = Database();
        var foreign = Data("solitaire", "completedAt", Now);
        foreign["userId"] = "bob";
        await Seed(database, [Row("foreign", foreign), Row("unsupported", Data("other", "completedAt", Now))]);

        var result = await CardRoomHistoryController.ReadLatestHistoryAsync(database, "alice", 40);

        Assert.True(result.IsComplete);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.DocumentsRead);
        Assert.IsType<OkObjectResult>(CardRoomHistoryController.HistoryResponse(result));
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-card-history-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();

    private static Timestamp Stamp(DateTime value) => Timestamp.FromDateTime(value);
    private static Dictionary<string, object> Data(string game, string timestampField, DateTime value) => new()
    {
        ["userId"] = "alice", ["game"] = game, [timestampField] = Stamp(value),
        ["matchId"] = "match_0000000001", ["claimStatus"] = "completed", ["settlementStatus"] = "recorded"
    };
    private static (string Id, Dictionary<string, object> Fields) Row(string id, Dictionary<string, object> fields) => (id, fields);
    private static async Task Seed(FirestoreDb database, IEnumerable<(string Id, Dictionary<string, object> Fields)> rows)
    {
        foreach (var page in rows.Chunk(400))
        {
            var batch = database.StartBatch();
            foreach (var row in page) batch.Set(database.Collection("cardGameResults").Document(row.Id), row.Fields);
            await batch.CommitAsync();
        }
    }
}
