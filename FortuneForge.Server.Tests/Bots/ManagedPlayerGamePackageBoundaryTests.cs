using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Bots;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace FortuneForge.Server.Tests.Bots;

public sealed partial class ManagedPlayerGamePackageBoundaryTests
{
    public static TheoryData<string> MultiplayerPackages => new()
    {
        "Blackjack", "Hearts", "LiarsDice", "Roulette", "Solitaire", "TexasHoldem", "ThreeCardPoker"
    };

    private static readonly HashSet<string> PlatformOwnedTypes = typeof(ManagedPlayerQueuer).Assembly.GetTypes()
        .Where(type => !type.IsNested && type.Namespace == "FortuneForge.Server.Bots")
        .Concat(typeof(IManagedPlayerRosterClient).Assembly.GetTypes().Where(type => !type.IsNested &&
            type.Namespace is "FortuneForge.Games.Abstractions.ManagedPlayers" or "FortuneForge.Games.Abstractions.Matchmaking"))
        .Append(typeof(FirestoreManagedPlayerPolicy))
        .Select(type => type.Name.Split('`')[0])
        .ToHashSet(StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(MultiplayerPackages))]
    public void Multiplayer_game_packages_do_not_own_platform_profiles_or_cross_game_leases(
        string packageName)
    {
        var packageRoot = Path.Combine(
            RepositoryRoot(), "games", packageName);
        foreach (var directory in new[] { "src", "server" })
        {
            var sources = SourceFiles(Path.Combine(packageRoot, directory)).ToArray();
            Assert.NotEmpty(sources);
            foreach (var path in sources.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var source = File.ReadAllText(path);
                Assert.Null(PlatformOwnershipViolation(source) is { } violation
                    ? $"{Path.GetRelativePath(packageRoot, path)}: {violation}" : null);
                if (directory == "src")
                    Assert.False(HostedRosterDependency().IsMatch(source),
                        $"{Path.GetRelativePath(packageRoot, path)} makes the domain depend on hosted roster transport.");
            }

            foreach (var project in sources.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
                Assert.DoesNotContain(XDocument.Load(project).Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference"),
                    reference => ((string?)reference.Attribute("Include"))?.Replace('\\', '/')
                        .EndsWith("/FortuneForge.Server.csproj", StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    [Theory]
    [MemberData(nameof(MultiplayerPackages))]
    public void Pure_rule_engines_remain_player_neutral(string packageName)
    {
        var root = Path.Combine(RepositoryRoot(), "games", packageName, "src");
        var engines = SourceFiles(root).Where(path =>
            path.EndsWith("Engine.cs", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("Rules.cs", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(engines);
        foreach (var path in engines)
            Assert.False(ManagedPlayerTerm().IsMatch(File.ReadAllText(path)),
                $"{Path.GetRelativePath(root, path)} makes the rule engine depend on managed-player runtime.");
    }

    [Fact]
    public void Ownership_boundary_accepts_signed_roster_adapters_and_game_owned_decisions()
    {
        const string adapter = """
            using FortuneForge.Games.Abstractions.ManagedPlayers;
            using FortuneForge.Games.Hosting;
            using FortuneForge.Server.Bots.Blackjack;
            public static class GameWorkerModule
            {
                public static void Configure(IServiceCollection services) => services.AddManagedPlayerRosterClient("blackjack");
            }
            public sealed class GameRoster(IManagedPlayerRosterClient roster)
            {
                public Task ReleaseAsync(string assignment, IReadOnlyCollection<string> profiles,
                    DateTime now, CancellationToken token) => roster.ReleaseAsync(assignment, profiles, now, token);
            }
            public sealed record GameManagedActor(ManagedPlayerRosterProfile Profile, ulong DecisionSeed);
            // Managed opponents use the shared roster; the game owns their legal actions.
            """;
        Assert.Null(PlatformOwnershipViolation(adapter));
    }

    [Theory]
    [InlineData("public sealed class ManagedPlayerProfileGenerator { }")]
    [InlineData("internal sealed class FirestoreManagedPlayerAssignmentStore { }")]
    [InlineData("public interface IManagedPlayerRosterClient { }")]
    [InlineData("public sealed record ManagedPlayerRosterProfile(string UserId);")]
    [InlineData("public static class ManagedTablePopulationDirector { }")]
    [InlineData("public static class FirestoreManagedPlayerPolicy { }")]
    [InlineData("database.Collection(\"managedPlayerAssignments\").Document(id).SetAsync(fields);")]
    [InlineData("transaction.Create(database.Collection(\"managedPlayerInfrastructure\").Document(id), fields);")]
    [InlineData("database.CollectionGroup(\"managedPlayerAssignments\").GetSnapshotAsync();")]
    [InlineData("database.Document(\"managedPlayerAssignments/id\").SetAsync(fields);")]
    [InlineData("transaction.Create(database.Document($\"managedPlayerAssignments/{id}\"), fields);")]
    [InlineData("database.Document(@\"managedPlayerInfrastructure/nameRegistry\").UpdateAsync(fields);")]
    [InlineData("using FortuneForge.Server.Bots; new ManagedPlayerQueuer(generator, profiles, assignments);")]
    [InlineData("new FortuneForge.Server.Bots.ManagedPlayerQueuer(generator, profiles, assignments);")]
    public void Ownership_boundary_rejects_copied_platform_owners_and_direct_lease_access(string source) =>
        Assert.NotNull(PlatformOwnershipViolation(source));

    [Theory]
    [InlineData("// The engine chooses a bot action.")]
    [InlineData("IManagedPlayerRosterClient roster;")]
    public void Pure_engine_boundary_rejects_managed_runtime_concepts(string source) =>
        Assert.True(ManagedPlayerTerm().IsMatch(source));

    [Theory]
    [InlineData("IManagedPlayerRosterClient roster;")]
    [InlineData("services.AddManagedPlayerRosterClient(\"liars-dice\");")]
    [InlineData("GameManagedPlayerRosterProtocol.ReserveAction;")]
    [InlineData("typeof(GameManagedPlayerRosterRegistration);")]
    public void Domain_boundary_rejects_hosted_roster_transport(string source) =>
        Assert.True(HostedRosterDependency().IsMatch(source));

    [Fact]
    public void Domain_boundary_accepts_game_owned_actor_state_and_roster_contracts()
    {
        const string source = """
            public sealed record GameManagedActor(ManagedPlayerRosterProfile Profile, ulong DecisionSeed);
            public interface IGamePlayerRoster
            {
                Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId,
                    int count, DateTime now, CancellationToken token);
            }
            """;
        Assert.Null(PlatformOwnershipViolation(source));
        Assert.False(HostedRosterDependency().IsMatch(source));
    }

    private static string? PlatformOwnershipViolation(string source)
    {
        foreach (Match declaration in TypeDeclaration().Matches(source))
            if (PlatformOwnedTypes.Contains(declaration.Groups["name"].Value.TrimStart('@')))
                return "Declares a platform-owned profile, lease, or shared roster/policy type.";
        if (PlatformStorageAccess().IsMatch(source))
            return "Accesses platform-owned managed-profile or cross-game assignment storage directly.";
        foreach (Match reference in PlatformRuntimeReference().Matches(source))
        {
            var name = reference.Groups["type"].Value;
            if (name.Length == 0 || PlatformOwnedTypes.Contains(name))
                return "Depends on the platform's concrete managed-player runtime instead of its shared roster port.";
        }
        return null;
    }

    [Fact]
    public void Shared_card_package_does_not_own_managed_player_runtime()
    {
        var packageRoot = Path.Combine(
            RepositoryRoot(), "games", "shared", "FortuneForge.Games.Cards");
        var files = SourceFiles(packageRoot).Where(path => !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(files);
        foreach (var path in files)
            Assert.False(ManagedPlayerTerm().IsMatch(File.ReadAllText(path)),
                $"{Path.GetRelativePath(packageRoot, path)} contains a managed-player runtime concept.");
    }

    private static IEnumerable<string> SourceFiles(string packageRoot) =>
        Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("package-lock.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "FortuneForge.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("The Fortune Forge repository root was not found.");
    }

    [GeneratedRegex(@"\b(?:bot|bots|managed)\b|\bI?Managed(?:Player|Table|Queue)[A-Za-z0-9_]*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManagedPlayerTerm();

    [GeneratedRegex(@"\b(?:class|interface|struct|enum|record(?:\s+(?:class|struct))?)\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex("\\b(?:Collection(?:Group)?|Document)\\s*\\(\\s*[$@]{0,2}\"(?:managedPlayerAssignments|managedPlayerInfrastructure)(?:/|\")", RegexOptions.CultureInvariant)]
    private static partial Regex PlatformStorageAccess();

    [GeneratedRegex(@"\bFortuneForge\.Server\.Bots(?:\.(?<type>[A-Za-z_][A-Za-z0-9_]*))?(?=[.;\s])", RegexOptions.CultureInvariant)]
    private static partial Regex PlatformRuntimeReference();

    [GeneratedRegex(@"\b(?:IManagedPlayerRosterClient|AddManagedPlayerRosterClient|GameManagedPlayerRosterRegistration|GameManagedPlayerRosterProtocol)\b", RegexOptions.CultureInvariant)]
    private static partial Regex HostedRosterDependency();
}
