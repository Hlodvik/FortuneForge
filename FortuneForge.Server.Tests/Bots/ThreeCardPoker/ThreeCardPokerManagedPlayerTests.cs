using FortuneForge.Server.Bots;
using Xunit;

namespace FortuneForge.Server.Tests.Bots.ThreeCardPoker;

public sealed class ThreeCardPokerManagedPlayerTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Profiles_created_for_other_games_and_existing_profiles_support_three_card_poker()
    {
        var profile = ManagedPlayerIdentityFactory.Create(ManagedPlayerGames.Blackjack, Now);
        Assert.Contains(ManagedPlayerGames.ThreeCardPoker, profile.SupportedGames);
        var existingGames = ManagedPlayerGames.SupportedWithCurrentAdapters([ManagedPlayerGames.TexasHoldem]);
        Assert.Contains(ManagedPlayerGames.TexasHoldem, existingGames);
        Assert.Contains(ManagedPlayerGames.ThreeCardPoker, existingGames);
    }

}
