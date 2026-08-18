using Xunit;

namespace FortuneForge.Server.Tests.Cards;

public sealed class GamePackageBoundaryTests
{
    [Theory]
    [InlineData(typeof(BlackjackRules), "FortuneForge.Games.Blackjack")]
    [InlineData(typeof(BlackjackTableEngine), "FortuneForge.Games.Blackjack")]
    [InlineData(typeof(SolitaireEngine), "FortuneForge.Games.Solitaire")]
    [InlineData(typeof(SolitaireCompetitionRules), "FortuneForge.Games.Solitaire")]
    [InlineData(typeof(CreditHoldemEngine), "FortuneForge.Games.TexasHoldem")]
    [InlineData(typeof(TexasHoldemRules), "FortuneForge.Games.TexasHoldem")]
    public void Server_uses_versioned_game_assemblies(Type boundaryType, string expectedAssembly)
    {
        var assembly = boundaryType.Assembly;

        Assert.Equal(expectedAssembly, assembly.GetName().Name);
        Assert.Equal(new Version(0, 4, 0, 0), assembly.GetName().Version);

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
