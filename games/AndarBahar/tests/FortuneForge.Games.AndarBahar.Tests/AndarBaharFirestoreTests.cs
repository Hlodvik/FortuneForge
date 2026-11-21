using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using FortuneForge.Games.AndarBahar.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.AndarBahar.Tests;

[CollectionDefinition("Andar Bahar Firestore", DisableParallelization = true)]
public sealed class AndarBaharFirestoreCollection : ICollectionFixture<AndarBaharEmulator>;

public sealed class AndarBaharEmulator : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> output = new();
    private Process? process;
    public string Endpoint { get; private set; } = string.Empty;
    public async Task InitializeAsync()
    {
        var existing = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(existing))
        {
            if (!existing.StartsWith("127.0.0.1:", StringComparison.Ordinal) && !existing.StartsWith("localhost:", StringComparison.Ordinal))
                throw new InvalidOperationException("Andar Bahar tests require a localhost Firestore emulator.");
            Endpoint = existing;
            return;
        }
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "firebase", "emulators");
        var jar = Directory.GetFiles(directory, "cloud-firestore-emulator-*.jar").OrderDescending().First();
        var java = "java";
        if (OperatingSystem.IsWindows())
        {
            var adoptium = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium");
            if (Directory.Exists(adoptium)) java = Directory.GetDirectories(adoptium, "jdk-*").OrderDescending().Select(path => Path.Combine(path, "bin", "java.exe")).First(File.Exists);
        }
        using var reservation = new TcpListener(System.Net.IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var start = new ProcessStartInfo(java) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var arg in new[] { "-Xms32m", "-Xmx256m", "-jar", jar, "--host", "127.0.0.1", "--port", port.ToString(), "--project_id", "demo-andar-bahar-tests" }) start.ArgumentList.Add(arg);
        process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        if (!process.Start()) throw new InvalidOperationException("Could not start the local Firestore emulator.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        var deadline = Environment.TickCount64 + 45_000;
        while (Environment.TickCount64 < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException(string.Join("\n", output));
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(250);
                await client.ConnectAsync("127.0.0.1", port, timeout.Token);
                Endpoint = $"127.0.0.1:{port}"; return;
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException) { await Task.Delay(100); }
        }
        throw new TimeoutException(string.Join("\n", output));
    }
    public async Task DisposeAsync()
    {
        if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        process?.Dispose();
    }
}
