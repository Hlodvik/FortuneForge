namespace FortuneForge.Server.Bots;

internal sealed class ManagedPlayerProfileGenerator(
    IManagedPlayerProfileRepository profiles) : IManagedPlayerProfileGenerator
{
    private const int MaximumGenerationAttempts = 20;

    public async Task<ManagedPlayerProfile> GenerateAsync(
        string gameId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumGenerationAttempts; attempt++)
        {
            var profile = ManagedPlayerIdentityFactory.Create(gameId, nowUtc);
            if (ManagedPlayerAvailabilityPolicy.IsSleeping(profile.UserId, nowUtc)) continue;
            if (await profiles.TryCreateAsync(profile, cancellationToken)) return profile;
        }

        throw new InvalidOperationException("A unique managed player profile could not be generated.");
    }
}
