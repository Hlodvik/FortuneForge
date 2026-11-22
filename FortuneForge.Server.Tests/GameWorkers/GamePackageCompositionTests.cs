using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Server.GameWorkers.Packages;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed class GamePackageCompositionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ff-package-integrity-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Selected_package_requires_exact_descriptor_and_bytes()
    {
        var selection = CreatePackage();
        WriteComposition(selection);
        Assert.Equal(selection.ReleaseId, Assert.Single(GamePackageComposition.ReadAndVerify(directory).Packages).ReleaseId);
        File.AppendAllText(Path.Combine(directory, selection.RuntimeGameId, selection.ReleaseId, selection.Entry), "tampered");
        Assert.Throws<InvalidOperationException>(() => GamePackageComposition.ReadAndVerify(directory));
    }

    [Fact]
    public void Extra_assembly_cannot_be_loaded_outside_the_selected_inventory()
    {
        var selection = CreatePackage();
        WriteComposition(selection);
        File.WriteAllText(Path.Combine(directory, selection.RuntimeGameId, selection.ReleaseId, "Unexpected.dll"), "unselected");
        Assert.Throws<InvalidOperationException>(() => GamePackageComposition.ReadAndVerify(directory));
    }

    [Fact]
    public void Reusing_a_runtime_identity_for_two_packages_is_rejected()
    {
        var selection = CreatePackage();
        WriteComposition(selection, selection with { GameId = "other-game" });
        Assert.Throws<InvalidOperationException>(() => GamePackageComposition.ReadAndVerify(directory));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("nested/../../outside.dll")]
    [InlineData("/outside.dll")]
    [InlineData("C:/outside.dll")]
    [InlineData("nested\\outside.dll")]
    public void Package_inventory_cannot_escape_its_directory(string path)
    {
        var selection = CreatePackage();
        WriteComposition(selection with { Files = [new(path, 1, new string('a', 64))] });
        Assert.Throws<InvalidOperationException>(() => GamePackageComposition.ReadAndVerify(directory));
    }

    [Fact]
    public void Descriptor_must_match_the_pinned_runtime_identity()
    {
        var selection = CreatePackage();
        var root = GamePackageComposition.PackageDirectory(directory, selection);
        var descriptor = Path.Combine(root, "release.json");
        var json = File.ReadAllText(descriptor).Replace("test-runtime", "wrong-runtime", StringComparison.Ordinal);
        File.WriteAllText(descriptor, json);
        WriteComposition(selection with { Files = Inventory(root) });
        Assert.Throws<InvalidOperationException>(() => GamePackageComposition.ReadAndVerify(directory));
    }

    private GamePackageSelection CreatePackage()
    {
        var selection = new GamePackageSelection("test-game", "test-runtime", Guid.NewGuid().ToString("D"),
            new string('a', 40), "Backend.dll", "Test.Backend", []);
        var root = GamePackageComposition.PackageDirectory(directory, selection);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, selection.Entry), "selected binary");
        File.WriteAllText(Path.Combine(root, "release.json"), JsonSerializer.Serialize(new
        {
            contractVersion = 1, selection.GameId, selection.RuntimeGameId, selection.ReleaseId,
            selection.SourceSha, selection.Entry, selection.ModuleType
        }, GamePackageComposition.Json));
        return selection with { Files = Inventory(root) };
    }

    private static GamePackageFile[] Inventory(string root) => Directory.GetFiles(root).Select(path =>
        new GamePackageFile(Path.GetFileName(path), new FileInfo(path).Length,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))).ToArray();

    private void WriteComposition(params GamePackageSelection[] packages) =>
        File.WriteAllText(Path.Combine(directory, "composition.json"),
            JsonSerializer.Serialize(new GamePackageComposition(1, packages), GamePackageComposition.Json));

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
