using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemFirestoreEmulatorFixture : IAsyncLifetime
{
    private const int Port = 8791;
    private readonly ConcurrentQueue<string> output = new();
    private Process? process;
    private string? emulatorWorkDirectory;

    public string Endpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var priorHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(priorHost))
        {
            if (!priorHost.StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase) &&
                !priorHost.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Credit Hold'em integration tests require a localhost Firestore emulator.");
            Endpoint = priorHost;
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var configuration = Path.Combine(
            repositoryRoot,
            "FortuneForge.Server.Tests",
            "Cards",
            "TexasHoldem",
            "Credit",
            "firebase-emulator.test.json");
        var executable = FirebaseExecutable();
        emulatorWorkDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fortuneforge-holdem-emulator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emulatorWorkDirectory);
        var start = new ProcessStartInfo
        {
            FileName = executable,
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
        start.ArgumentList.Add("demo-fortuneforge-holdem-tests");
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(configuration);
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Capture(eventArgs.Data);
        if (!process.Start()) throw new InvalidOperationException("The Firebase Firestore emulator could not be started.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(75);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"The Firebase Firestore emulator exited early.\n{string.Join("\n", output)}");
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync("127.0.0.1", Port, timeout.Token);
                Endpoint = $"127.0.0.1:{Port}";
                return;
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                await Task.Delay(200);
            }
        }
        throw new TimeoutException($"The Firebase Firestore emulator did not listen on port {Port}.\n{string.Join("\n", output)}");
    }

    public async Task DisposeAsync()
    {
        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process?.Dispose();
        if (emulatorWorkDirectory is { } directory)
            await DeleteTemporaryDirectoryAsync(directory);
    }

    private void Capture(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) output.Enqueue(value);
    }

    private static async Task DeleteTemporaryDirectoryAsync(string directory)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(directory); attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(100);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FortuneForge.slnx")))
            directory = directory.Parent;
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
