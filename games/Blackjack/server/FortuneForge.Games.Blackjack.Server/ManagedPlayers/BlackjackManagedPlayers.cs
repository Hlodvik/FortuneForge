using FortuneForge.Games.Abstractions.ManagedPlayers;
namespace FortuneForge.Server.Bots.Blackjack;

internal sealed class BlackjackManagedPlayerSupplyRequiredException(
    string assignmentId,
    int count,
    IReadOnlyCollection<string> excludedProfileIds) : Exception
{
    public string AssignmentId { get; } = assignmentId;
    public int Count { get; } = count;
    public IReadOnlyCollection<string> ExcludedProfileIds { get; } = excludedProfileIds;
}

internal sealed record BlackjackReservedManagedPlayer(
    ManagedPlayerRosterProfile Profile,
    string AssignmentId);

internal sealed class BlackjackManagedPlayerSupply(
    IReadOnlyCollection<BlackjackReservedManagedPlayer>? reserved = null,
    Func<DateTime, ManagedPlayerRosterProfile>? createForTests = null)
{
    private readonly List<BlackjackReservedManagedPlayer> available = reserved?.ToList() ?? [];
    private readonly HashSet<string> used = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> UsedProfileIds => used;

    public ManagedPlayerRosterProfile Take(
        string assignmentId,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc)
    {
        var excluded = excludedProfileIds.ToHashSet(StringComparer.Ordinal);
        var reservation = available.FirstOrDefault(candidate =>
            candidate.AssignmentId == assignmentId &&
            !used.Contains(candidate.Profile.UserId) &&
            !excluded.Contains(candidate.Profile.UserId));
        if (reservation is null)
        {
            if (createForTests is null)
                throw new BlackjackManagedPlayerSupplyRequiredException(assignmentId, 1, excludedProfileIds);
            reservation = new(createForTests(nowUtc), assignmentId);
            available.Add(reservation);
        }
        var profile = reservation.Profile;
        used.Add(profile.UserId);
        return profile;
    }
}
