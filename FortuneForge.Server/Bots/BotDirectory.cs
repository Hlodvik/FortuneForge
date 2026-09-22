using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Bots;

/// <summary>
/// Stable application-managed participant profiles. This is deliberately independent
/// from queue demand, game rules, and account or wallet persistence.
/// </summary>
public sealed class BotDirectoryOptions
{
    public const string SectionName = "Bots:Directory";

    public List<BotProfileOptions> Profiles { get; set; } = [];
}

public sealed class BotProfileOptions
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int SkillLevel { get; set; }
    public List<string> SupportedGames { get; set; } = [];
}

public sealed record BotProfile(
    string Id,
    string DisplayName,
    int SkillLevel,
    IReadOnlySet<string> SupportedGames)
{
    public bool Supports(string gameId) => SupportedGames.Contains(gameId);
}

public interface IBotDirectory
{
    IReadOnlyList<BotProfile> Select(
        string gameId,
        ulong selectionSeed,
        int count,
        int? preferredSkillLevel = null);
}

public sealed class ConfiguredBotDirectory : IBotDirectory
{
    private readonly IReadOnlyList<BotProfile> profiles;

    public ConfiguredBotDirectory(BotDirectoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BotDirectoryValidation.ThrowIfInvalid(options);
        profiles = options.Profiles.Select(CreateProfile).ToArray();
    }

    public IReadOnlyList<BotProfile> Select(
        string gameId,
        ulong selectionSeed,
        int count,
        int? preferredSkillLevel = null)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            throw new ArgumentException("A game identifier is required.", nameof(gameId));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (preferredSkillLevel is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(preferredSkillLevel));

        var candidates = profiles.Where(profile => profile.Supports(gameId)).ToArray();
        if (count > candidates.Length)
        {
            throw new InvalidOperationException(
                $"Bot directory has {candidates.Length} profiles for '{gameId}', but {count} were requested.");
        }

        return candidates
            .OrderBy(profile => PreferenceRank(profile, preferredSkillLevel))
            .ThenBy(profile => StableRank(gameId, selectionSeed, profile.Id))
            .ThenBy(profile => profile.Id, StringComparer.Ordinal)
            .Take(count)
            .ToArray();
    }

    private static BotProfile CreateProfile(BotProfileOptions profile) => new(
        profile.Id.Trim(),
        profile.DisplayName.Trim(),
        profile.SkillLevel,
        new HashSet<string>(profile.SupportedGames.Select(game => game.Trim()), StringComparer.OrdinalIgnoreCase));

    private static int PreferenceRank(BotProfile profile, int? preferredSkillLevel) =>
        preferredSkillLevel is null ? 0 : Math.Abs(profile.SkillLevel - preferredSkillLevel.Value);

    private static ulong StableRank(string gameId, ulong selectionSeed, string profileId)
    {
        var value = Encoding.UTF8.GetBytes($"{gameId.Trim().ToLowerInvariant()}:{selectionSeed}:{profileId}");
        return BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(value));
    }
}

public static class BotDirectoryValidation
{
    public static void ThrowIfInvalid(BotDirectoryOptions options)
    {
        var errors = GetErrors(options).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }

    public static IEnumerable<string> GetErrors(BotDirectoryOptions? options)
    {
        if (options is null)
        {
            yield return "Bot directory options are required.";
            yield break;
        }
        if (options.Profiles.Count < 8)
            yield return "Bots:Directory:Profiles must define at least eight profiles.";

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in options.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !ids.Add(profile.Id.Trim()))
                yield return "Every bot profile must have a unique Id.";
            if (string.IsNullOrWhiteSpace(profile.DisplayName) || !displayNames.Add(profile.DisplayName.Trim()))
                yield return "Every bot profile must have a unique DisplayName.";
            if (profile.SkillLevel is < 1 or > 5)
                yield return $"Bot profile '{profile.Id}' has an invalid SkillLevel.";
            if (profile.SupportedGames.Count == 0 || profile.SupportedGames.Any(string.IsNullOrWhiteSpace))
                yield return $"Bot profile '{profile.Id}' must list at least one supported game.";
        }
    }
}
