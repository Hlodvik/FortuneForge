using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

[CollectionDefinition("Competitive Solitaire Firestore emulator", DisableParallelization = true)]
public sealed class SolitaireFirestoreEmulatorCollection :
    ICollectionFixture<SolitaireFirestoreEmulatorFixture>
{
    public const string Name = "Competitive Solitaire Firestore emulator";
}

public sealed class SolitaireFirestoreEmulatorFixture : IAsyncLifetime
{
    private const int Port = 8794;
    private readonly ConcurrentQueue<string> output = new();
    private Process? process;
    private string? priorHost;
    private string? emulatorWorkDirectory;

    public async Task InitializeAsync()
    {
        priorHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        var repositoryRoot = FindRepositoryRoot();
        var configuration = Path.Combine(
            repositoryRoot,
            "FortuneForge.Server.Tests",
            "Solitaire",
            "firebase-emulator.test.json");
        emulatorWorkDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fortuneforge-solitaire-emulator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emulatorWorkDirectory);
        var start = new ProcessStartInfo
        {
            FileName = FirebaseExecutable(),
            WorkingDirectory = emulatorWorkDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("emulators:start");
        start.ArgumentList.Add("--only");
        start.ArgumentList.Add("firestore");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add("demo-fortuneforge-solitaire-tests");
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(configuration);
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("The Firebase Firestore emulator could not be started.");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(75);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The Firebase Firestore emulator exited early.\n{string.Join("\n", output)}");
            }
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync("127.0.0.1", Port, timeout.Token);
                Environment.SetEnvironmentVariable("FIRESTORE_EMULATOR_HOST", $"127.0.0.1:{Port}");
                return;
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                await Task.Delay(200);
            }
        }
        throw new TimeoutException(
            $"The Firebase Firestore emulator did not listen on port {Port}.\n{string.Join("\n", output)}");
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("FIRESTORE_EMULATOR_HOST", priorHost);
        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process?.Dispose();
        if (emulatorWorkDirectory is { } directory && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void Capture(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) output.Enqueue(value);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FortuneForge.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the FortuneForge repository root.");
    }

    private static string FirebaseExecutable()
    {
        if (!OperatingSystem.IsWindows()) return "firebase";
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var command = Path.Combine(appData, "npm", "firebase.cmd");
        return File.Exists(command) ? command : "firebase.cmd";
    }
}
