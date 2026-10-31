using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class RoundFindingReconciliationTests
{
    [Fact]
    public void Clean_practice_database_replay_resolves_capture_time_pending_warning()
    {
        var summary = Summary("2048", PracticeOutcomeAudit.TwentyFortyEightVersion,
            new RoundAuditFinding("independent-replay-pending", "warning", "Waiting for database replay."));

        var (findings, status) = SecurityAuditInvestigationService.ReconcileRoundFindings(summary, []);

        Assert.Empty(findings);
        Assert.Equal("passed", status);
    }

    [Fact]
    public void Failed_database_replay_preserves_pending_warning_and_flags_round()
    {
        var summary = Summary("2048", PracticeOutcomeAudit.TwentyFortyEightVersion,
            new RoundAuditFinding("independent-replay-pending", "warning", "Waiting for database replay."));

        var (findings, status) = SecurityAuditInvestigationService.ReconcileRoundFindings(summary,
            [new("practice-outcome-evidence-invalid", "error", "Invalid outcome evidence.")]);

        Assert.Contains(findings, finding => finding.Code == "independent-replay-pending");
        Assert.Contains(findings, finding => finding.Code == "practice-outcome-evidence-invalid");
        Assert.Equal("flagged", status);
    }

    [Fact]
    public void Clean_arcade_database_replay_retains_separate_prize_settlement_warning()
    {
        var summary = Summary("flappy", "arcade-server-replay-v1",
            new RoundAuditFinding("arcade-replay-second-pass-pending", "warning", "Waiting for database replay."),
            new RoundAuditFinding("competition-prize-settlement-separate", "warning", "Prize settlement is separate."));

        var (findings, status) = SecurityAuditInvestigationService.ReconcileRoundFindings(summary, []);

        Assert.DoesNotContain(findings, finding => finding.Code == "arcade-replay-second-pass-pending");
        Assert.Contains(findings, finding => finding.Code == "competition-prize-settlement-separate");
        Assert.Equal("incomplete", status);
    }

    [Fact]
    public void Clean_paid_card_replay_replaces_pending_outcome_with_wallet_coverage_warning()
    {
        var summary = Summary("hi-lo", "server-settlement-receipt-v1",
            new RoundAuditFinding("outcome-replay-pending", "warning", "Waiting for database replay.")) with
        { EvidenceKind = "hi-lo-revealed-cards-and-choices-v1" };

        var (findings, status) = SecurityAuditInvestigationService.ReconcileRoundFindings(summary,
            [new("hi-lo-wallet-ledger-linkage-pending", "warning", "Action ledger links are unavailable.")]);

        Assert.DoesNotContain(findings, finding => finding.Code == "outcome-replay-pending");
        Assert.Contains(findings, finding => finding.Code == "hi-lo-wallet-ledger-linkage-pending");
        Assert.Equal("incomplete", status);
    }

    [Fact]
    public void Clean_free_hearts_replay_keeps_non_atomic_storage_coverage_visible()
    {
        var summary = Summary("hearts", "free-engine-receipt-v1",
            new RoundAuditFinding("independent-replay-pending", "warning", "Waiting for database replay.")) with
        { EvidenceKind = "hearts-private-hands-and-tricks-v1" };

        var (findings, status) = SecurityAuditInvestigationService.ReconcileRoundFindings(summary,
            [new("hearts-free-journal-non-atomic", "warning", "In-memory state and journal writes are separate.")]);

        Assert.DoesNotContain(findings, finding => finding.Code == "independent-replay-pending");
        Assert.Contains(findings, finding => finding.Code == "hearts-free-journal-non-atomic");
        Assert.Equal("incomplete", status);
    }

    [Fact]
    public void Clean_liars_dice_replay_resolves_pending_but_keeps_non_atomic_warning()
    {
        var summary = Summary("liars-dice", "free-engine-receipt-v1",
            new RoundAuditFinding("independent-replay-pending", "warning", "Waiting for database replay.")) with
        { EvidenceKind = "liars-dice-private-hands-and-actions-v2" };

        var (findings, status) = SecurityAuditInvestigationService.ReconcileRoundFindings(summary,
            [new("liars-dice-in-memory-state-journal-non-atomic", "warning", "Separate writes.")]);

        Assert.DoesNotContain(findings, finding => finding.Code == "independent-replay-pending");
        Assert.Equal("incomplete", status);
    }

    private static RoundAuditSummary Summary(string gameId, string version, params RoundAuditFinding[] findings) =>
        new("audit", gameId, "round", "table", 1, DateTime.UtcNow, DateTime.UtcNow,
            "incomplete", findings, [], true, null, version, null, "digest", "kind");
}
