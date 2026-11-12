using System.Net;
using Google.Api.Gax;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal sealed record DemoGameAccount(string UserId, FirestoreDb Database)
{
    internal static DemoGameAccount? Create(IConfiguration configuration)
    {
        var projectId = configuration["GoogleCloud:ProjectId"];
        var userId = configuration["GameWorker:DemoUserId"];
        var emulatorHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(userId) &&
            string.IsNullOrWhiteSpace(emulatorHost))
            return null;

        if (string.IsNullOrWhiteSpace(userId) || userId.IndexOfAny(['/', '\\']) >= 0)
            throw new InvalidOperationException("Local game workers require GameWorker:DemoUserId.");

        return new DemoGameAccount(userId, CreateEmulatorDatabase(configuration, projectId, emulatorHost));
    }

    internal static FirestoreDb CreateEmulatorDatabase(IConfiguration configuration, string? projectId, string? emulatorHost)
    {
        if (projectId is null || !projectId.StartsWith("demo-", StringComparison.Ordinal) ||
            projectId.Length == "demo-".Length || !IsLoopbackEmulator(emulatorHost))
        {
            throw new InvalidOperationException(
                "Local game calls require a demo-* project and a loopback FIRESTORE_EMULATOR_HOST.");
        }

        return new FirestoreDbBuilder
        {
            ProjectId = projectId,
            DatabaseId = configuration["GoogleCloud:FirestoreDatabaseId"] ?? "(default)",
            EmulatorDetection = EmulatorDetection.EmulatorOnly
        }.Build();
    }

    internal static bool IsLocalRequest(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not { } address ||
            !IsLoopback(address) || !IsLoopbackHost(context.Request.Host.Host))
            return false;

        var origin = context.Request.Headers.Origin.ToString();
        return origin.Length == 0
            ? context.Request.Headers["Sec-Fetch-Site"] != "cross-site"
            : Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https" && IsLoopbackHost(uri.Host);
    }

    private static bool IsLoopbackEmulator(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || !Uri.TryCreate("http://" + host, UriKind.Absolute, out var endpoint))
            return false;

        var separator = host.LastIndexOf(':');
        return separator > 0 && int.TryParse(host.AsSpan(separator + 1), out var port) &&
            port is > 0 and <= 65535 && port == endpoint.Port && IsLoopbackHost(endpoint.Host) &&
            endpoint.UserInfo.Length == 0 && endpoint.AbsolutePath == "/" &&
            endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0;
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsLoopback(address);

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
