namespace FortuneForge.Server.Bots.ThreeCardPoker;

internal sealed class ThreeCardPokerManagedPlayerSupplyRequiredException(
    string assignmentId,
    int count,
    IReadOnlyCollection<string> excludedProfileIds) : Exception
{
    public string AssignmentId { get; } = assignmentId;
    public int Count { get; } = count;
    public IReadOnlyCollection<string> ExcludedProfileIds { get; } = excludedProfileIds;
}

internal sealed record ThreeCardPokerReservedManagedPlayer(
    ManagedPlayerProfile Profile,
    string AssignmentId);

internal sealed class ThreeCardPokerManagedPlayerSupply(
    IReadOnlyCollection<ThreeCardPokerReservedManagedPlayer>? reserved = null,
    bool generateWhenEmpty = false)
{
    private readonly List<ThreeCardPokerReservedManagedPlayer> available = reserved?.ToList() ?? [];
    private readonly HashSet<string> used = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> UsedProfileIds => used;

    public ManagedPlayerProfile Take(
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
            if (!generateWhenEmpty)
                throw new ThreeCardPokerManagedPlayerSupplyRequiredException(assignmentId, 1, excludedProfileIds);
            reservation = new(ManagedPlayerIdentityFactory.Create("three-card-poker", nowUtc), assignmentId);
            available.Add(reservation);
        }
        var profile = reservation.Profile;
        used.Add(profile.UserId);
        return profile;
    }
}
