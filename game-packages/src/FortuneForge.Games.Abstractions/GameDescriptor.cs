using System.Text.RegularExpressions;

namespace FortuneForge.Games.Abstractions;

public enum GameCategory
{
    Card,
    Slot,
    Casino,
    Arcade,
    Dice,
    Other,
}

[Flags]
public enum GameCapability
{
    None = 0,
    Credits = 1 << 0,
    FreePlay = 1 << 1,
    Multiplayer = 1 << 2,
    Bots = 1 << 3,
    History = 1 << 4,
}

public sealed record GameDescriptor(
    string Id,
    string DisplayName,
    GameCategory Category,
    string PackageVersion,
    string ClientRoute,
    string? ApiBasePath,
    GameCapability Capabilities)
{
    private static readonly Regex IdPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex VersionPattern = new(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public void Validate()
    {
        if (!IdPattern.IsMatch(Id))
            throw new ArgumentException("A game ID must be lower-case kebab-case.", nameof(Id));
        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new ArgumentException("A game display name is required.", nameof(DisplayName));
        if (!VersionPattern.IsMatch(PackageVersion))
            throw new ArgumentException("A game package version must be semantic version text.", nameof(PackageVersion));
        ValidatePath(ClientRoute, nameof(ClientRoute));
        if (ApiBasePath is not null) ValidatePath(ApiBasePath, nameof(ApiBasePath));
    }

    private static void ValidatePath(string path, string parameterName)
    {
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("A game route must be an absolute application path.", parameterName);
    }
}
