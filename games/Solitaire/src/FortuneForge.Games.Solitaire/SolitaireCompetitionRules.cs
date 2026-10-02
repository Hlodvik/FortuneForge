namespace FortuneForge.Games.Solitaire;

internal static class SolitaireCompetitionRules
{
    public static readonly TimeSpan MatchDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PauseBudget = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LateHumanClaimWindow = TimeSpan.FromMinutes(3);

    public static IReadOnlyList<SolitairePlayerState> Rank(
        IReadOnlyList<SolitairePlayerState> players) => players
        .OrderByDescending(player => player.Game.Score)
        .ThenBy(player => player.ElapsedMilliseconds ?? long.MaxValue)
        .ThenBy(player => player.Game.Moves)
        .ThenBy(player => player.CompletedAtUtc ?? DateTime.MaxValue)
        .ThenBy(player => player.UserId, StringComparer.Ordinal)
        .ToArray();
}
