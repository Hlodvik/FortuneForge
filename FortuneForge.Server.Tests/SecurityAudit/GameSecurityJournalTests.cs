using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class GameSecurityJournalTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Retry_is_identical_but_a_changed_engine_state_never_overwrites_original_evidence()
    {
        var first = GameSecurityEventDocuments.Capture("mines", "round", 2, Now, new { revealed = new[] { 1, 2 } }, ["alice"]);
        var retry = GameSecurityEventDocuments.Capture("mines", "round", 2, Now, new { revealed = new[] { 1, 2 } }, ["alice"]);
        var changed = GameSecurityEventDocuments.Capture("mines", "round", 2, Now, new { revealed = new[] { 1, 3 } }, ["alice"]);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(first.Fields["payloadJson"], retry.Fields["payloadJson"]);
        Assert.NotEqual(first.Id, changed.Id);
    }

    [Fact]
    public void Private_state_and_trusted_request_are_frozen_with_all_participants()
    {
        var state = new List<string> { "AS", "KH" };
        var request = new GameRequestObservation("server-request", "alice", "session-hash", "203.0.113.1", Now,
            "POST", "/api/cards/texas-holdem/credit/matches/table/actions", "texas-holdem", "table", "cmd", 3,
            "{\"type\":\"raise\",\"raiseTo\":500}", "sanitized", "connection");
        GameSecurityEventDocument document;
        using (GameSecurityContext.Push(request))
            document = GameSecurityEventDocuments.Capture("texas-holdem", "table-hand-1", 4, Now, state, ["alice", "bob"]);
        state.Clear();
        using var payload = JsonDocument.Parse((string)document.Fields["payloadJson"]);
        Assert.Equal(2, payload.RootElement.GetProperty("state").GetArrayLength());
        Assert.Equal("203.0.113.1", payload.RootElement.GetProperty("request").GetProperty("ipAddress").GetString());
        Assert.Equal(new[] { "alice", "bob" }, payload.RootElement.GetProperty("participants").EnumerateArray().Select(value => value.GetString()));
        Assert.Null(GameSecurityContext.Current);
    }

    [Fact]
    public void Invalid_timestamps_or_oversized_state_fail_before_the_financial_transaction_can_commit()
    {
        Assert.Throws<ArgumentException>(() => GameSecurityEventDocuments.Capture("mines", "round", 1,
            DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), new { }, ["alice"]));
        Assert.Throws<InvalidOperationException>(() => GameSecurityEventDocuments.Capture("mines", "round", 1,
            Now, new { data = new string('x', 800_001) }, ["alice"]));
    }
}
