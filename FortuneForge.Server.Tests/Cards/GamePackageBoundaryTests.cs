using FortuneForge.Games.Craps;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Games.Roulette;
using Xunit;

namespace FortuneForge.Server.Tests.Cards;

public sealed class GamePackageBoundaryTests
{
    [Theory]
    [InlineData(typeof(BlackjackRules), "FortuneForge.Games.Blackjack", 0, 4, 1)]
    [InlineData(typeof(BlackjackTableEngine), "FortuneForge.Games.Blackjack", 0, 4, 1)]
    [InlineData(typeof(CrapsEngine), "FortuneForge.Games.Craps", 0, 1, 1)]
    [InlineData(typeof(HeartsEngine), "FortuneForge.Games.Hearts", 0, 1, 1)]
    [InlineData(typeof(LiarsDiceEngine), "FortuneForge.Games.LiarsDice", 0, 1, 1)]
    [InlineData(typeof(RouletteEngine), "FortuneForge.Games.Roulette", 0, 1, 1)]
    [InlineData(typeof(SolitaireEngine), "FortuneForge.Games.Solitaire", 0, 4, 1)]
    [InlineData(typeof(SolitaireCompetitionRules), "FortuneForge.Games.Solitaire", 0, 4, 1)]
    [InlineData(typeof(CreditHoldemEngine), "FortuneForge.Games.TexasHoldem", 0, 4, 1)]
    [InlineData(typeof(TexasHoldemRules), "FortuneForge.Games.TexasHoldem", 0, 4, 1)]
    public void Server_uses_versioned_game_assemblies(
        Type boundaryType,
        string expectedAssembly,
        int major,
        int minor,
        int patch)
    {
        var assembly = boundaryType.Assembly;

        Assert.Equal(expectedAssembly, assembly.GetName().Name);
        Assert.Equal(new Version(major, minor, patch, 0), assembly.GetName().Version);

        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("Firebase", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Google.Cloud", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("FortuneForge.Server", StringComparison.Ordinal));
    }
}
