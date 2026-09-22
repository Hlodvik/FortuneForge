using FortuneForge.Server.Bots;

namespace FortuneForge.Server.Cards.Bots;

internal interface ICardBotIdentityProvider
{
    IReadOnlyList<BotIdentity> Create(string gameId, ulong selectionSeed, int count, int preferredSkillLevel);
}

/// <summary>
/// Keeps card queues dependent on a small participant contract while the platform
/// owns profile identity and skill assignment in one shared directory.
/// </summary>
internal sealed class DirectoryCardBotIdentityProvider(IBotDirectory directory) : ICardBotIdentityProvider
{
    public IReadOnlyList<BotIdentity> Create(
        string gameId,
        ulong selectionSeed,
        int count,
        int preferredSkillLevel)
    {
        CardBotSkillLevels.Validate(preferredSkillLevel);
        return directory.Select(gameId, selectionSeed, count, preferredSkillLevel)
            .Select(profile => new BotIdentity(profile.Id, profile.DisplayName, profile.SkillLevel))
            .ToArray();
    }
}
