using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Configuration;
using FortuneForge.Server.Tests.Solitaire;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Configuration;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreEmulatorIdentityIntegrationTests
{
    [Fact]
    public async Task Actual_host_sdk_bypasses_deny_rules_only_with_guarded_emulator_server_identity_while_browsers_are_denied()
    {
        var endpoint = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")
            ?? throw new InvalidOperationException("A localhost Firestore emulator is required.");
        var project = "demo-firestore-host-identity-" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleCloud:ProjectId"] = project,
            ["GoogleCloud:FirestoreDatabaseId"] = "(default)",
        }).Build();
        // This is the real application's guarded factory, not a separate test-only privileged SDK.
        var database = AccountServicesConfiguration.CreateFirestore(configuration, endpoint);
        using var client = new HttpClient { BaseAddress = new Uri("http://" + endpoint), Timeout = TimeSpan.FromSeconds(15) };
        var rules = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "firestore.rules"));
        var loaded = await client.PutAsJsonAsync($"/emulator/v1/projects/{project}:securityRules", new
            { rules = new { files = new[] { new { content = rules } } } });
        Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);

        // Covers transaction begin, batch read, commit, normal document reads and query RPCs.
        // No production project, credentials or funds are reachable through this test.
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 100L, ["availableFractionalCents"] = 0L });
        var service = new MinesService(new FirestoreMinesStore(database), new MinesEngine(new FixedBoard()));
        var round = (await service.StartAsync("player", new(Guid.NewGuid().ToString("N"), 100, 3))).Round!;
        await service.RevealAsync("player", round.RoundId, new(Guid.NewGuid().ToString("N"), 24));
        var paid = await service.CashOutAsync("player", round.RoundId, new(Guid.NewGuid().ToString("N")));
        Assert.Equal(10_002, paid.BalanceCents);
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.True((await database.Collection("minesRounds").Document(round.RoundId).GetSnapshotAsync()).Exists);

        var paths = new[]
        {
            "minesRounds/" + round.RoundId,
            "userBalances/player_slotsCredits",
            "minesCommands/private-command",
            "minesSessions/private-session",
            "balanceTransactions/private-ledger",
        };
        var tokens = new string?[] { null, BrowserToken(project, "player"), BrowserToken(project, "fake-admin", admin: true) };
        foreach (var token in tokens)
        foreach (var path in paths)
        {
            var url = $"/v1/projects/{project}/databases/(default)/documents/{path}";
            using var read = new HttpRequestMessage(HttpMethod.Get, url);
            if (token is not null) read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(read)).StatusCode);
            using var write = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { fields = new { userId = new { stringValue = "player" } } }) };
            if (token is not null) write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(write)).StatusCode);
        }
        Assert.Equal(10_002, (await service.SessionAsync("player")).BalanceCents);
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
    }

    private static string BrowserToken(string project, string userId, bool admin = false)
    {
        static string Encode(object value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Encode(new { alg = "none", typ = "JWT" }) + "." + Encode(new
        {
            iss = "https://securetoken.google.com/" + project, aud = project, sub = userId, user_id = userId,
            iat = now, exp = now + 3600, auth_time = now, admin, role = admin ? "admin" : "player",
            firebase = new { sign_in_provider = "custom", identities = new { } },
        }) + ".";
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "firestore.rules"))) return directory.FullName;
        throw new InvalidOperationException("The checked-in Firestore rules could not be located.");
    }
    private sealed class FixedBoard : IMinesBoardGenerator
    {
        public System.Collections.Immutable.ImmutableArray<int> Generate(int mineCount) => [0, 1, 2];
    }
}
