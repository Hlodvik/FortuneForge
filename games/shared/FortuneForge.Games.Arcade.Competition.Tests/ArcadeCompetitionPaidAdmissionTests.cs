using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Games.Arcade.Competition.Tests;

public sealed class ArcadeCompetitionPaidAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 4, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = ArcadeCompetitionRules.JohannesburgTimeZone;

    [Theory]
    [InlineData(ArcadeCompetitionWindowKind.Daily)]
    [InlineData(ArcadeCompetitionWindowKind.Weekly)]
    public void CurrentBoundsPermitOnlyFreshAdmissionAndCanonicalOldBoundsPermitRecovery(ArcadeCompetitionWindowKind kind)
    {
        var current = ArcadeCompetitionRules.GetWindow(kind, Now, Zone);
        var previous = ArcadeCompetitionRules.GetWindow(kind, current.StartsAtUtc.AddSeconds(-1), Zone);
        var exact = new ArcadeCompetitionPaidAdmission(current.StartsAtUtc, current.EndsAtUtc);
        var stale = new ArcadeCompetitionPaidAdmission(previous.StartsAtUtc, previous.EndsAtUtc);

        Assert.True(new ArcadeCompetitionPaidAdmission().AllowsNewEntries(current, Zone));
        Assert.False(new ArcadeCompetitionPaidAdmission().AllowsNewEntries(current, Zone, recoverOnly: true));
        Assert.True(exact.AllowsNewEntries(current, Zone));
        Assert.False(exact.AllowsNewEntries(current, Zone, recoverOnly: true));
        Assert.False(stale.AllowsNewEntries(current, Zone));
    }

    [Fact]
    public void PartialNonUtcOrNonCanonicalBoundsAreInvalidEvenInRecoveryMode()
    {
        var current = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, Now, Zone);
        var withOffset = current.StartsAtUtc.ToOffset(TimeSpan.FromHours(2));
        Assert.Throws<ArgumentException>(() => new ArcadeCompetitionPaidAdmission(current.StartsAtUtc)
            .AllowsNewEntries(current, Zone, recoverOnly: true));
        Assert.Throws<ArgumentException>(() => new ArcadeCompetitionPaidAdmission(withOffset, current.EndsAtUtc)
            .AllowsNewEntries(current, Zone));
        Assert.Throws<ArgumentException>(() => new ArcadeCompetitionPaidAdmission(current.StartsAtUtc, current.EndsAtUtc.AddMinutes(1))
            .AllowsNewEntries(current, Zone));
    }
}
