using FortuneForge.Server.Arcade.Competition;

namespace FortuneForge.Games.Arcade.Competition;

/// <summary>Immutable expected-window terms carried in a paid start request.</summary>
public sealed record ArcadeCompetitionPaidAdmission(DateTimeOffset? ExpectedWindowStartsAtUtc = null,
    DateTimeOffset? ExpectedWindowEndsAtUtc = null)
{
    public bool AllowsNewEntries(ArcadeCompetitionWindow currentWindow, TimeZoneInfo timeZone, bool recoverOnly = false)
    {
        ArgumentNullException.ThrowIfNull(currentWindow);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (ExpectedWindowStartsAtUtc is null && ExpectedWindowEndsAtUtc is null)
            return !recoverOnly;
        if (ExpectedWindowStartsAtUtc is not { } starts || ExpectedWindowEndsAtUtc is not { } ends)
            throw new ArgumentException("The expected arcade competition window requires both bounds.");
        if (starts.Offset != TimeSpan.Zero || ends.Offset != TimeSpan.Zero || starts >= ends)
            throw new ArgumentException("The expected arcade competition window must have increasing UTC bounds.");

        var expected = ArcadeCompetitionRules.GetWindow(currentWindow.Kind, starts, timeZone);
        if (expected.StartsAtUtc != starts || expected.EndsAtUtc != ends)
            throw new ArgumentException("The expected arcade competition window bounds are invalid.");
        return !recoverOnly && expected == currentWindow;
    }
}
