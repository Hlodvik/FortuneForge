using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Bots;
using Xunit;

namespace FortuneForge.Server.Tests.Bots;

public sealed class BotDirectoryTests
{
    [Fact]
    public void Selects_stable_unique_profiles_for_each_game()
    {
        var directory = Directory();

        var first = directory.Select(CardBotGames.Blackjack, 42, 5, preferredSkillLevel: 3);
        var second = directory.Select(CardBotGames.Blackjack, 42, 5, preferredSkillLevel: 3);

        Assert.Equal(first, second);
        Assert.Equal(5, first.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(first, profile => Assert.True(profile.Supports(CardBotGames.Blackjack)));
    }

    [Fact]
    public void Prefers_requested_skill_without_rewriting_a_profile_skill()
    {
        var profiles = Directory().Select(CardBotGames.Blackjack, 99, 4, preferredSkillLevel: 4);

        Assert.Equal([4, 4, 4, 3], profiles.Select(profile => profile.SkillLevel));
    }

    [Fact]
    public void Card_adapter_preserves_the_directory_identity_and_assigned_skill()
    {
        var directory = Directory();
        var adapter = new DirectoryCardBotIdentityProvider(directory);

        var profiles = directory.Select(CardBotGames.Solitaire, 7, 3, preferredSkillLevel: 3);
        var identities = adapter.Create(CardBotGames.Solitaire, 7, 3, preferredSkillLevel: 3);

        Assert.Equal(profiles.Select(profile => profile.Id), identities.Select(identity => identity.SeatId));
        Assert.Equal(profiles.Select(profile => profile.SkillLevel), identities.Select(identity => identity.SkillLevel));
    }

    [Fact]
    public void Rejects_an_incomplete_or_duplicate_directory()
    {
        var incomplete = Options();
        incomplete.Profiles.RemoveAt(7);
        Assert.Throws<InvalidOperationException>(() => new ConfiguredBotDirectory(incomplete));

        var duplicate = Options();
        duplicate.Profiles[7].Id = duplicate.Profiles[0].Id;
        Assert.Throws<InvalidOperationException>(() => new ConfiguredBotDirectory(duplicate));
    }

    private static ConfiguredBotDirectory Directory() => new(Options());

    private static BotDirectoryOptions Options() => new()
    {
        Profiles =
        [
            Profile("amber-cardinal", "AmberCardinal", 2),
            Profile("brisk-comet", "BriskComet", 3),
            Profile("calm-juniper", "CalmJuniper", 4),
            Profile("clever-otter", "CleverOtter", 3),
            Profile("copper-willow", "CopperWillow", 2),
            Profile("gentle-panda", "GentlePanda", 4),
            Profile("silver-robin", "SilverRobin", 3),
            Profile("swift-lantern", "SwiftLantern", 4)
        ]
    };

    private static BotProfileOptions Profile(string id, string displayName, int skillLevel) => new()
    {
        Id = id,
        DisplayName = displayName,
        SkillLevel = skillLevel,
        SupportedGames = [CardBotGames.Blackjack, CardBotGames.Solitaire, CardBotGames.TexasHoldem]
    };
}
