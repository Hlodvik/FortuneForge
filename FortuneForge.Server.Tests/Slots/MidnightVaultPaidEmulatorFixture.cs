using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using FortuneForge.Server.Configuration;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MidnightVaultPaidEmulatorCollection : ICollectionFixture<MidnightVaultPaidEmulatorFixture>
{
    public const string Name = "Midnight Vault isolated paid emulator";
}

public sealed class MidnightVaultPaidEmulatorFixture : IAsyncLifetime
{
    private const int Port = 8799;
    private readonly ConcurrentQueue<string> output = new();
    private readonly string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    private Process? process;
    public string Endpoint { get; private set; } = "127.0.0.1:8799";
    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "FortuneForge.slnx"))) return directory.FullName;
            throw new DirectoryNotFoundException("The existing repository checkout was not found.");
        }
    }
    public static string EvidenceDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("MIDNIGHT_PAID_EVIDENCE_DIRECTORY");
            var path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(RepositoryRoot, "output", "maintenance-2026-11-02", "midnight-vault-frequency-rebalance")
                : Path.GetFullPath(configured, RepositoryRoot);
            var outputRoot = Path.GetFullPath(Path.Combine(RepositoryRoot, "output")) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Path.GetFullPath(path).StartsWith(outputRoot, comparison))
                throw new InvalidOperationException("Midnight paid evidence must stay inside this checkout's output directory.");
            return path;
        }
    }

    public async Task InitializeAsync()
    {
        var supplied = Environment.GetEnvironmentVariable("MIDNIGHT_PAID_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            if (!Uri.TryCreate("http://" + supplied, UriKind.Absolute, out var uri) || !uri.IsLoopback)
                throw new InvalidOperationException("Midnight paid tests require a loopback emulator.");
            Endpoint = supplied;
            return;
        }
        var jar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "firebase", "emulators", "cloud-firestore-emulator-v1.19.8.jar");
        if (!File.Exists(jar)) throw new FileNotFoundException("A cached Firestore emulator is required; this fixture never downloads one.", jar);
        using (var probe = new TcpClient())
        {
            try
            {
                await probe.ConnectAsync("127.0.0.1", Port);
                throw new InvalidOperationException($"Port {Port} is already owned; supply an explicitly coordinated loopback emulator instead.");
            }
            catch (SocketException) { }
        }
        Directory.CreateDirectory(EvidenceDirectory);
        var start = new ProcessStartInfo("java")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = EvidenceDirectory
        };
        foreach (var argument in new[] { "-jar", jar, "--host=127.0.0.1", $"--port={Port}", "--project_id=demo-midnight-paid-tests" })
            start.ArgumentList.Add(argument);
        process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) output.Enqueue(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) output.Enqueue(args.Data); };
        if (!process.Start()) throw new InvalidOperationException("The cached local emulator did not start.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException(string.Join(Environment.NewLine, output));
            using var probe = new TcpClient();
            try { await probe.ConnectAsync("127.0.0.1", Port); return; }
            catch (SocketException) { await Task.Delay(100); }
        }
        throw new TimeoutException("The isolated Firestore emulator did not become ready.");
    }

    public FirestoreDb Database()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["GoogleCloud:ProjectId"] = "demo-midnight-paid-" + Guid.NewGuid().ToString("N") }).Build();
        return AccountServicesConfiguration.CreateFirestore(configuration, Endpoint);
    }

    public async Task DisposeAsync()
    {
        if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        process?.Dispose();
        Directory.CreateDirectory(EvidenceDirectory);
        await File.WriteAllLinesAsync(Path.Combine(EvidenceDirectory, $"paid-emulator-{runId}.log"), output);
    }
}
