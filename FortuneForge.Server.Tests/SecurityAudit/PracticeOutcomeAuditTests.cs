using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.Roulette;
using FortuneForge.Games.TwentyFortyEight;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class PracticeOutcomeAuditTests
{
    [Fact]
    public void Database_recheck_validates_2048_terminal_board_and_rejects_altered_evidence()
    {
        var tiles = new int[16]; tiles[0] = 2048;
        var state = new TwentyFortyEightState(4, 7, 13, tiles.ToImmutableArray(), 2048, 1, TwentyFortyEightPhase.Won, 2048);
        var audit = Capture("2048", new { state }, PracticeOutcomeAudit.TwentyFortyEightVersion,
            PracticeOutcomeAudit.Check(state));
        Assert.Equal("passed", audit.Summary.AuditStatus);
        Assert.DoesNotContain(PracticeRoundReplayAudit.Inspect(audit.Summary, audit.PayloadJson), finding => finding.Severity == "error");
        var altered = JsonNode.Parse(audit.PayloadJson)!;
        altered["state"]!["highestTile"] = 4;
        Assert.Contains(PracticeRoundReplayAudit.Inspect(audit.Summary, altered.ToJsonString()), finding => finding.Code == "2048-terminal-mismatch");
    }

    [Fact]
    public void Database_recheck_validates_drop_merge_loss_condition()
    {
        var tiles = new int[49]; tiles[0] = 2;
        var state = new DropMergeState(7, 7, 3, 9, tiles.ToImmutableArray(), 2, 4, 0, 1,
            0, 0, 0, 0, 2, 1024, DropMergePhase.Lost, 2);
        var audit = Capture("drop-merge", new { state, dropColumn = 0, dropRow = -1 },
            PracticeOutcomeAudit.DropMergeVersion, PracticeOutcomeAudit.Check(state, 0, -1));
        Assert.Equal("passed", audit.Summary.AuditStatus);
        Assert.DoesNotContain(PracticeRoundReplayAudit.Inspect(audit.Summary, audit.PayloadJson), finding => finding.Severity == "error");
        var altered = JsonNode.Parse(audit.PayloadJson)!;
        altered["dropColumn"] = 1;
        Assert.Contains(PracticeRoundReplayAudit.Inspect(audit.Summary, altered.ToJsonString()), finding => finding.Code == "drop-merge-terminal-mismatch");
    }

    [Fact]
    public void Database_recheck_recomputes_craps_pass_line_and_side_bets_from_ordered_dice()
    {
        var first = new DicePair(new DieValue(2), new DieValue(2));
        var last = new DicePair(new DieValue(3), new DieValue(4));
        var state = new CrapsPassLineState(new("alice", 2m), CrapsPassLinePhase.Resolved, 4,
            ImmutableArray.Create(first, last), new(last, CrapsRollResult.SevenOut, true, 0m));
        PracticeCrapsExtraBet[] extras = [new("field", 2m, true, true, 4m), new("odds", 1m, true, false, 0m)];
        var audit = Capture("craps", new { state, extraBets = extras }, PracticeOutcomeAudit.CrapsVersion,
            PracticeOutcomeAudit.Check(state, extras));
        Assert.Equal("passed", audit.Summary.AuditStatus);
        Assert.DoesNotContain(PracticeRoundReplayAudit.Inspect(audit.Summary, audit.PayloadJson), finding => finding.Severity == "error");
        var altered = JsonNode.Parse(audit.PayloadJson)!;
        altered["extraBets"]![0]!["totalReturn"] = 99;
        Assert.Contains(PracticeRoundReplayAudit.Inspect(audit.Summary, altered.ToJsonString()), finding => finding.Code == "craps-outcome-mismatch");
    }

    [Fact]
    public void Database_recheck_recomputes_roulette_returns_and_flags_a_changed_payout()
    {
        var state = new RouletteRoundState("round", RouletteRoundPhase.Settled,
            [new RouletteBet("alice", RouletteBetKind.Red, 10m),
             new RouletteBet("bob", RouletteBetKind.Straight, 5m, 7)],
            new RoulettePocket(7),
            [new RouletteSettlement("alice", RouletteBetKind.Red, 10m, true, 20m),
             new RouletteSettlement("bob", RouletteBetKind.Straight, 5m, true, 180m)]);
        var normalized = new { state.RoundId, state.Phase,
            bets = state.Bets.Select(bet => new { bet.PlayerId, bet.Kind, bet.Stake, bet.Number,
                numbers = bet.Numbers.IsDefault ? Array.Empty<int>() : bet.Numbers.ToArray() }).ToArray(),
            state.WinningPocket, state.Settlements };
        var audit = Capture("roulette", new { state = normalized }, PracticeOutcomeAudit.RouletteVersion,
            PracticeOutcomeAudit.Check(state));
        Assert.Equal("passed", audit.Summary.AuditStatus);
        Assert.DoesNotContain(PracticeRoundReplayAudit.Inspect(audit.Summary, audit.PayloadJson), finding => finding.Severity == "error");
        var altered = JsonNode.Parse(audit.PayloadJson)!;
        altered["state"]!["settlements"]![1]!["totalReturn"] = 179;
        Assert.Contains(PracticeRoundReplayAudit.Inspect(audit.Summary, altered.ToJsonString()), finding => finding.Code == "roulette-outcome-mismatch");
    }

    [Fact]
    public void Roulette_outcome_check_covers_all_pockets_and_major_bet_families()
    {
        var bets = new[]
        {
            new RouletteBet("alice", RouletteBetKind.Red, 1m),
            new RouletteBet("alice", RouletteBetKind.Black, 1m),
            new RouletteBet("alice", RouletteBetKind.Even, 1m),
            new RouletteBet("alice", RouletteBetKind.Odd, 1m),
            new RouletteBet("alice", RouletteBetKind.Low, 1m),
            new RouletteBet("alice", RouletteBetKind.High, 1m),
            new RouletteBet("alice", RouletteBetKind.Column, 1m, 1),
            new RouletteBet("alice", RouletteBetKind.Dozen, 1m, 2),
            new RouletteBet("alice", RouletteBetKind.Straight, 1m, 0),
            new RouletteBet("alice", RouletteBetKind.Split, 1m, null, [0, 1]),
            new RouletteBet("alice", RouletteBetKind.Street, 1m, null, [0, 1, 2]),
            new RouletteBet("alice", RouletteBetKind.Corner, 1m, null, [1, 2, 4, 5]),
            new RouletteBet("alice", RouletteBetKind.SixLine, 1m, null, [1, 2, 3, 4, 5, 6]),
        };
        var open = bets.Aggregate(RouletteEngine.OpenRound("round"), RouletteEngine.PlaceBet);
        for (var pocket = 0; pocket <= 36; pocket++)
        {
            var settled = RouletteEngine.Spin(open, new RoulettePocket(pocket)).State;
            Assert.DoesNotContain(PracticeOutcomeAudit.Check(settled), finding => finding.Severity == "error");
        }
    }

    [Fact]
    public void Craps_outcome_check_accepts_every_terminal_one_and_two_roll_dice_pair()
    {
        var dice = Enumerable.Range(1, 6).SelectMany(first => Enumerable.Range(1, 6)
            .Select(second => new DicePair(new DieValue(first), new DieValue(second)))).ToArray();
        foreach (var first in dice)
        {
            var once = CrapsEngine.Roll(CrapsEngine.StartPassLine(new CrapsPassLineBet("alice", 2m)), first).State;
            if (once.Phase == CrapsPassLinePhase.Resolved)
                Assert.DoesNotContain(PracticeOutcomeAudit.Check(once, []), finding => finding.Severity == "error");
            else foreach (var second in dice)
            {
                var twice = CrapsEngine.Roll(once, second).State;
                if (twice.Phase == CrapsPassLinePhase.Resolved)
                    Assert.DoesNotContain(PracticeOutcomeAudit.Check(twice, []), finding => finding.Severity == "error");
            }
        }
    }

    private static RoundAuditCapture Capture(string gameId, object payload, string auditorVersion,
        IReadOnlyList<RoundAuditFinding> findings) => RoundAuditCapture.Create(gameId, "instance-1", "table-1", 1,
        new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 26, 0, 1, 0, DateTimeKind.Utc), payload,
        gameId + "-private-state-v1", auditorVersion,
        [new RoundAuditParticipant("alice", "alice", true, 0, 0, 0)], findings);
}
