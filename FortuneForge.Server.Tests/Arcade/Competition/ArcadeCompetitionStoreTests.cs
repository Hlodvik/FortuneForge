using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionStoreTests
{
    [Fact]
    public void IdentityProducesAStableFirestoreSafeDocumentName()
    {
        var first = Identity();
        var second = Identity();

        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Matches("^[a-f0-9]{64}$", first.DocumentId);
        Assert.Equal(first.DocumentId, ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(first));
        Assert.Equal("arcadeCompetitions", ArcadeCompetitionFirestoreDocuments.CompetitionsCollection);
    }

    [Fact]
    public void AttemptRecordValidatesItsIdentityAndWindowBoundaries()
    {
        var identity = Identity();
        var valid = new ArcadeCompetitionAttemptStart("attempt-1", identity, "player-1", 100, identity.StartsAtUtc);

        Assert.Equal("attempt-1", valid.AttemptId);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArcadeCompetitionAttemptCompletion("attempt-2", identity, "player-1", -1, identity.StartsAtUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArcadeCompetitionAttemptStart("attempt-3", identity, "player-1", 0, identity.StartsAtUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArcadeCompetitionAttemptCompletion("attempt-4", identity, "player-1", 1, identity.EndsAtUtc));
        Assert.Throws<ArgumentException>(() => new ArcadeCompetitionAttemptStart(" ", identity, "player-1", 100, identity.StartsAtUtc));
    }

    [Fact]
    public void AttemptDocumentNameScopesTheCallerIdempotencyKeyToItsCompetition()
    {
        var first = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart("client-attempt-42", Identity(), "player-1", 100, Start));
        var replay = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart("client-attempt-42", Identity(), "player-1", 100, Start));
        var anotherCompetition = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart("client-attempt-42", LaterIdentity(), "player-1", 100, LaterStart));

        Assert.Equal(first.DocumentId, replay.DocumentId);
        Assert.NotEqual(first.DocumentId, anotherCompetition.DocumentId);
        var append = Assert.Single(typeof(IArcadeCompetitionStore).GetMethods(), method => method.Name == "StartAttemptAsync");
        Assert.Equal(typeof(Task<ArcadeCompetitionStartAttemptResult>), append.ReturnType);
    }

    [Fact]
    public void SettlementStateAndFirestoreShapeContainNoFinancialFields()
    {
        var pending = new ArcadeCompetitionSettlementState(Identity(), null);
        var completed = new ArcadeCompetitionSettlementState(Identity(), Start.AddDays(1));
        var fields = ArcadeCompetitionFirestoreDocuments.SettlementData(completed);

        Assert.False(pending.IsCompleted);
        Assert.True(completed.IsCompleted);
        Assert.Equal("completed", fields["status"]);
        Assert.Equal(completed.Competition.DocumentId, fields["competitionId"]);
        Assert.DoesNotContain(fields.Keys, key => key.Contains("payout", StringComparison.OrdinalIgnoreCase) || key.Contains("refund", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly DateTimeOffset Start = new(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterStart = Start.AddDays(1);

    private static ArcadeCompetitionIdentity Identity() => new("asteroids", ArcadeCompetitionWindowKind.Daily, Start, Start.AddDays(1));
    private static ArcadeCompetitionIdentity LaterIdentity() => new("asteroids", ArcadeCompetitionWindowKind.Daily, LaterStart, LaterStart.AddDays(1));
}
