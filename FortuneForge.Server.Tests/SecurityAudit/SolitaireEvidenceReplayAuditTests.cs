using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Solitaire;
using FortuneForge.Server.Cards.Solitaire;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class SolitaireEvidenceReplayAuditTests
{
    [Fact]
    public void Paid_match_replays_seed_board_human_draw_and_final_ranking_then_detects_changed_command()
    {
        var at = new DateTime(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);
        const string matchId = "solitaire-audit-match";
        var game = SolitaireEngine.CreateGame(42, 3);
        var initial = new SolitairePlayerState(matchId, "alice", "Alice", 1,
            SolitairePlayerStatuses.Playing, game, 1, null, null, 0, false)
        { StartedAtUtc = at, DeadlineAtUtc = at.AddMinutes(10) };
        var command = new SolitaireCommandRequest(SolitaireCommandTypes.Draw, 1, null, null, null, null);
        var drawn = initial with { Game = SolitaireEngine.Apply(game, command), Version = 2,
            UndoHistory = [game] };
        var finished = drawn with { Game = drawn.Game with { Message = "Time expired" },
            Version = 3, Status = SolitairePlayerStatuses.Finished,
            ElapsedMilliseconds = 600_000, CompletedAtUtc = at.AddMinutes(10) };
        var match = new SolitaireMatch(matchId, 4, 500, 500, 450, 50, 42,
            at, at.AddMinutes(10), "playing", ["alice"], ["Alice"], ["ticket-1"], [at], null, null)
        { FinancialModel = "house-funded-bot-pool-v1", HumanEntryCents = 500, DrawCount = 3 };
        var settledMatch = match with { Status = "settled", CompletedAtUtc = at.AddMinutes(10),
            WinnerUserId = "alice" };
        var paid = finished with { PayoutCents = 450 };
        var capture = CompetitiveSolitaireRoundAudit.Capture(settledMatch, [paid], at.AddMinutes(10));

        var entry = Event(match, [initial], at, "human-entry");
        GameSecurityEventDocument action;
        using (GameSecurityContext.Push(new GameRequestObservation("request-1", "alice", "session", "203.0.113.1",
            at, "POST", $"/api/solitaire/matches/{matchId}/commands", "solitaire", matchId,
            "command-1", 1, JsonSerializer.Serialize(command, RoundAuditCapture.Json), "sanitized", "connection")))
            action = GameSecurityEventDocuments.Capture("solitaire", matchId, 2, at.AddSeconds(1),
                new { match, players = new[] { drawn }, versionScope = "sum-of-seat-versions" }, ["alice"], "human-command");
        var timeout = Event(match, [finished], at.AddMinutes(10), "managed-or-timeout-progression");
        var settlement = Event(settledMatch, [paid], at.AddMinutes(10), "settlement");
        var events = new[] { Json(entry), Json(action), Json(timeout), Json(settlement) };

        Assert.Empty(SolitaireEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events));

        var changed = JsonNode.Parse(Json(action))!;
        changed["request"]!["requestJson"] = JsonSerializer.Serialize(command with
        { Type = SolitaireCommandTypes.Flip, Column = 0 }, RoundAuditCapture.Json);
        Assert.Contains(SolitaireEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson,
            [events[0], changed.ToJsonString(), events[2], events[3]]),
            finding => finding.Code == "solitaire-command-replay-mismatch");

        var wrongActor = JsonNode.Parse(Json(action))!;
        wrongActor["request"]!["userId"] = "bob";
        Assert.Contains(SolitaireEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson,
            [events[0], wrongActor.ToJsonString(), events[2], events[3]]),
            finding => finding.Code == "solitaire-command-actor-mismatch" && finding.Severity == "error");
    }

    private static GameSecurityEventDocument Event(SolitaireMatch match,
        SolitairePlayerState[] players, DateTime at, string kind) =>
        GameSecurityEventDocuments.Capture("solitaire", match.MatchId,
            players.Sum(player => (long)player.Version), at,
            new { match, players, versionScope = "sum-of-seat-versions" },
            players.Where(player => player.IsAccountBacked).Select(player => player.UserId), kind);

    private static string Json(GameSecurityEventDocument value) => (string)value.Fields["payloadJson"];
}
