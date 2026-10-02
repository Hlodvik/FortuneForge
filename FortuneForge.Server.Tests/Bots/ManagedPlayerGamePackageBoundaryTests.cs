using System.Text.RegularExpressions;
using Xunit;

namespace FortuneForge.Server.Tests.Bots;

public sealed partial class ManagedPlayerGamePackageBoundaryTests
{
    [Theory]
    [InlineData("Blackjack")]
    [InlineData("Hearts")]
    [InlineData("LiarsDice")]
    [InlineData("Solitaire")]
    [InlineData("TexasHoldem")]
    public void Multiplayer_game_packages_do_not_define_managed_player_concepts(
        string packageName)
    {
        var packageRoot = Path.Combine(
            RepositoryRoot(), "games", packageName);
        AssertPackageIsPlayerNeutral(packageRoot);
    }

    [Fact]
    public void Shared_card_package_does_not_own_managed_player_runtime()
    {
        var packageRoot = Path.Combine(
            RepositoryRoot(), "games", "shared", "FortuneForge.Games.Cards");
        AssertPackageIsPlayerNeutral(packageRoot);
    }

    private static void AssertPackageIsPlayerNeutral(string packageRoot)
    {
        var files = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("package-lock.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(files);
        foreach (var path in files)
        {
            var content = File.ReadAllText(path);
            Assert.False(
                ManagedPlayerTerm().IsMatch(content),
                $"{Path.GetRelativePath(packageRoot, path)} contains an application-owned managed-player concept.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "FortuneForge.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("The Fortune Forge repository root was not found.");
    }

    [GeneratedRegex(@"\b(?:bot|bots|managed)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManagedPlayerTerm();
}
