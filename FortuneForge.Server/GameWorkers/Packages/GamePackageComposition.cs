using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FortuneForge.Server.GameWorkers.Packages;

internal sealed record GamePackageFile(string Path, long Size, string Sha256);
internal sealed record GamePackageSelection(string GameId, string RuntimeGameId, string ReleaseId,
    string SourceSha, string Entry, string ModuleType, GamePackageFile[] Files);
internal sealed record GamePackageComposition(int ContractVersion, GamePackageSelection[] Packages)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static GamePackageComposition ReadAndVerify(string directory)
    {
        var manifest = JsonSerializer.Deserialize<GamePackageComposition>(
            File.ReadAllBytes(System.IO.Path.Combine(directory, "composition.json")), Json)
            ?? throw new InvalidOperationException("Missing backend package composition.");
        if (manifest.ContractVersion != 1 || manifest.Packages is not { Length: > 0 and <= 100 })
            throw new InvalidOperationException("Unsupported backend package composition.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var releaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (!IsId(package.GameId) || !IsId(package.RuntimeGameId) || !ids.Add(package.RuntimeGameId) ||
                !releaseIds.Add(package.GameId) || !Guid.TryParseExact(package.ReleaseId, "D", out _) ||
                !Regex.IsMatch(package.SourceSha ?? "", "\\A[0-9a-f]{40}\\z") ||
                string.IsNullOrWhiteSpace(package.ModuleType) || package.Files is not { Length: > 0 })
                throw new InvalidOperationException("Invalid backend package identity.");
            var root = PackageDirectory(directory, package);
            _ = SafeFile(directory, $"{package.RuntimeGameId}/{package.ReleaseId}/release.json");
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in package.Files)
            {
                var path = SafeFile(root, file.Path);
                if (!expected.Add(file.Path) || file.Size < 0 ||
                    !Regex.IsMatch(file.Sha256 ?? "", "\\A[0-9a-f]{64}\\z"))
                    throw new InvalidOperationException("Invalid backend package inventory.");
                using var stream = File.OpenRead(path);
                if (stream.Length != file.Size ||
                    !string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), file.Sha256, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Backend package integrity failed: {package.GameId}/{file.Path}.");
            }
            if (!expected.Contains(package.Entry) || !package.Entry.EndsWith(".dll", StringComparison.Ordinal) ||
                Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(path =>
                    !expected.Contains(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'))))
                throw new InvalidOperationException("Backend package contains unlisted files or lacks its entry assembly.");
            var descriptor = JsonSerializer.Deserialize<GamePackageDescriptor>(
                File.ReadAllBytes(SafeFile(root, "release.json")), Json);
            if (descriptor is null || descriptor.ContractVersion != 1 || descriptor.GameId != package.GameId ||
                descriptor.RuntimeGameId != package.RuntimeGameId || descriptor.ReleaseId != package.ReleaseId ||
                descriptor.SourceSha != package.SourceSha || descriptor.Entry != package.Entry || descriptor.ModuleType != package.ModuleType)
                throw new InvalidOperationException("Backend package descriptor differs from its selected version.");
        }
        return manifest;
    }

    internal static string PackageDirectory(string directory, GamePackageSelection package) =>
        System.IO.Path.Combine(directory, package.RuntimeGameId, package.ReleaseId);

    internal static string SafeFile(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or "..") || System.IO.Path.IsPathRooted(path))
            throw new InvalidOperationException("Unsafe backend package path.");
        var current = System.IO.Path.GetFullPath(root);
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Backend packages cannot contain symbolic links.");
        foreach (var part in path.Split('/'))
        {
            current = System.IO.Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Backend packages cannot contain symbolic links.");
        }
        return current;
    }

    private static bool IsId(string? id) => id is { Length: > 0 and <= 64 } &&
        Regex.IsMatch(id, "\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z");

    private sealed record GamePackageDescriptor(int ContractVersion, string GameId, string RuntimeGameId,
        string ReleaseId, string SourceSha, string Entry, string ModuleType);
}
