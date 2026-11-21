using System.Collections.Immutable;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class MinesSequenceReplayAuditTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Database_replays_every_accepted_mines_turn_and_detects_missing_or_illegal_intermediate_turns(bool firestoreTimestamp)
    {
        var engine = new MinesEngine(new FixedBoard());
        var created = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).AddTicks(7);
        var first = new MinesStoredRound(new string('a', 64), "player-1", engine.Start(100, 3), 1, created, created);
        var second = first with { State = engine.Reveal(first.State, 3), Version = 2, UpdatedAt = created.AddSeconds(1) };
        var third = second with { State = engine.CashOut(second.State), Version = 3, UpdatedAt = created.AddSeconds(2) };
        var capture = SinglePlayerRoundRecord.Create("mines", third.RoundId, third.UserId,
            third.State.WagerCents, third.State.PayoutCents, created.UtcDateTime, third.UpdatedAt.UtcDateTime,
            "minesRounds", third.RoundId, third, "mines-private-board-and-reveals-v1",
            MinesOutcomeAudit.Verify(third, third.State.PayoutCents), MinesOutcomeAudit.Version).Audit;
        var summary = firestoreTimestamp ? capture.Summary with
        {
            StartedAtUtc = capture.Summary.StartedAtUtc.AddTicks(-7),
            CompletedAtUtc = capture.Summary.CompletedAtUtc.AddTicks(-7),
        } : capture.Summary;
        var events = new[] { Event(first), Event(second), Event(third) };

        Assert.Empty(MinesSequenceReplayAudit.Inspect(summary, capture.PayloadJson, events));
        var changedSummary = summary with { CompletedAtUtc = summary.CompletedAtUtc.AddTicks(TimeSpan.TicksPerMicrosecond) };
        Assert.Contains(MinesSequenceReplayAudit.Inspect(changedSummary, capture.PayloadJson, events),
            finding => finding.Code == "mines-terminal-replay-mismatch");
        Assert.Contains(MinesSequenceReplayAudit.Inspect(summary, capture.PayloadJson,
            [events[0], events[2]]), finding => finding.Code == "mines-action-sequence-incomplete");

        var illegalSecond = second with { State = first.State };
        Assert.Contains(MinesSequenceReplayAudit.Inspect(summary, capture.PayloadJson,
            [events[0], Event(illegalSecond), events[2]]), finding => finding.Code == "mines-action-sequence-mismatch");
    }

    private static string Event(MinesStoredRound round) =>
        (string)GameSecurityEventDocuments.Capture("mines", round.RoundId, round.Version,
            round.UpdatedAt.UtcDateTime, round, [round.UserId]).Fields["payloadJson"];

    private sealed class FixedBoard : IMinesBoardGenerator
    {
        public ImmutableArray<int> Generate(int mineCount) => [0, 1, 2];
    }
}
