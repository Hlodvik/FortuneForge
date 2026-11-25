using System.Text.Json;

namespace FortuneForge.Server.Configuration;

/// <summary>Platform availability only. Games continue to own admission and recovery of their rounds.</summary>
internal static class GameMaintenance
{
    private static readonly HashSet<string> RuntimeIds = Read();
    internal static bool Contains(string? runtimeGameId) => runtimeGameId is not null && RuntimeIds.Contains(runtimeGameId);

    internal static void ApplyAdmissionSettings(string runtimeGameId, IDictionary<string, string?> settings)
    {
        if (!Contains(runtimeGameId)) return;
        var admissionKeys = settings.Keys.Where(key => key.EndsWith(":AllowNewPaidRounds", StringComparison.Ordinal)).ToArray();
        if (admissionKeys.Length == 0)
            throw new InvalidOperationException("A maintenance game must expose its new-round admission switch.");
        foreach (var key in admissionKeys) settings[key] = "false";
    }

    private static HashSet<string> Read()
    {
        using var stream = typeof(GameMaintenance).Assembly.GetManifestResourceStream("FortuneForge.GameMaintenance.json")
            ?? throw new InvalidOperationException("Missing committed game maintenance policy.");
        var configuration = JsonSerializer.Deserialize<Policy>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (configuration?.ContractVersion != 1 || configuration.RuntimeGameIds is null ||
            configuration.RuntimeGameIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
                id.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')) ||
            configuration.RuntimeGameIds.Distinct(StringComparer.Ordinal).Count() != configuration.RuntimeGameIds.Length)
            throw new InvalidOperationException("Invalid committed game maintenance policy.");
        return configuration.RuntimeGameIds.ToHashSet(StringComparer.Ordinal);
    }
    private sealed record Policy(int ContractVersion, string[] RuntimeGameIds);
}
