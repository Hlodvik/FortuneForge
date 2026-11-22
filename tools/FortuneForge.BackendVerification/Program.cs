using System.Net;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Hosting.InProcess;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Configuration;
using FortuneForge.Server.GameWorkers.Packages;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Framework and package logs stay on stderr; stdout is one machine-readable result.
var summaryOutput = Console.Out;
Console.SetOut(Console.Error);
try
{
    var result = await BackendVerification.RunAsync(args);
    await summaryOutput.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    }));
    return 0;
}
catch (Exception error)
{
    await Console.Error.WriteLineAsync($"Shared backend verification failed: {error.GetType().Name}: {error.Message}");
    return 1;
}

internal static class BackendVerification
{
    private const string Player = "shared-backend-player";
    private const string OtherPlayer = "shared-backend-other";
    private const long InitialCents = 10_000;
    private const long WagerCents = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<object> RunAsync(string[] args)
    {
        Require(args is ["--packages", var path] && !string.IsNullOrWhiteSpace(path),
            "Usage: FortuneForge.BackendVerification --packages <materialized-game-packages-directory>");
        ValidateEmulator();
        var packageDirectory = Path.GetFullPath(args[1]);
        var compositionPath = Path.Combine(packageDirectory, "composition.json");
        var composition = JsonSerializer.Deserialize<SelectedComposition>(await File.ReadAllBytesAsync(compositionPath), Json);
        Require(composition is { ContractVersion: 1, Packages.Length: 45 }, "Verification requires the complete 45-game composition.");
        var selected = composition!.Packages;
        Require(selected.Select(item => item.GameId).Distinct(StringComparer.Ordinal).Count() == 45 &&
            selected.Select(item => item.RuntimeGameId).Distinct(StringComparer.Ordinal).Count() == 45,
            "The selected catalogue must contain 45 distinct release and runtime identities.");
        Require(File.Exists(Path.Combine(AppContext.BaseDirectory, "backend-host.json")),
            "The root server's backend-host.json content must be copied beside the verification executable.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        var projectId = "demo-shared-backend-" + Guid.NewGuid().ToString("N");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FortuneForgeStartup).Assembly.FullName,
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
            Args = []
        });
        // No user secrets, production project, provider credentials, or remote exporter
        // configuration can flow into this disposable emulator-only verification host.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleCloud:ProjectId"] = projectId,
            ["GoogleCloud:FirestoreDatabaseId"] = "(default)",
            ["GamePackages:Directory"] = packageDirectory,
            ["GamePackages:Required"] = "true",
            ["GamePackages:AllowDevelopmentEmulator"] = "true",
            ["GameSecurityRequests:Enabled"] = "true",
            ["LocalDevelopment:DefaultAccount:Enabled"] = "false",
            ["Payments:Provider"] = "mock",
            ["Payments:MockSimulationEnabled"] = "false",
            ["Features:AndarBaharEnabled"] = "true",
            ["Features:DragonTigerEnabled"] = "true",
            ["AllowedHosts"] = "*"
        });
        builder.AddFortuneForge();
        var server = new InProcessGameServer();
        builder.Services.RemoveAll<IServer>();
        builder.Services.AddSingleton<IServer>(server);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        await using var app = builder.Build();
        app.UseFortuneForgePipeline();
        await app.StartAsync(token);
        try
        {
            Require(app.Services.GetRequiredService<FirestoreDb>().ProjectId == projectId,
                "The running host must use only the uniquely named emulator project.");
            var transport = app.Services.GetRequiredService<IGamePackageTransport>();
            foreach (var package in selected)
            {
                Require(transport.Contains(package.RuntimeGameId), "A selected package is missing from the in-process transport.");
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://game-package.internal/healthz");
                using var health = await transport.SendAsync(package.RuntimeGameId, request, token);
                Require(health.StatusCode == HttpStatusCode.OK, $"Package health failed for {package.GameId}.");
                using var payload = JsonDocument.Parse(await health.Content.ReadAsStringAsync(token));
                Require(payload.RootElement.GetProperty("gameId").GetString() == package.RuntimeGameId &&
                    payload.RootElement.GetProperty("contractVersion").GetInt32() == 1,
                    $"Loaded package identity differs from the selected descriptor for {package.GameId}.");
            }

            var database = app.Services.GetRequiredService<FirestoreDb>();
            var accounts = app.Services.GetRequiredService<IAccountStore>() as FirestoreAccountStore
                ?? throw new InvalidOperationException("Verification requires the real Firestore account store.");
            var session = await CreateAccountAsync(database, accounts, Player, "Verification Player", token);
            var otherSession = await CreateAccountAsync(database, accounts, OtherPlayer, "Verification Other", token);
            using var browser = new HttpClient(new InProcessHttpMessageHandler(server.Endpoint))
            {
                BaseAddress = new Uri("https://fortuneforgegame.web.app"),
                Timeout = TimeSpan.FromSeconds(45)
            };
            Require((await database.Collection("walletRoundSettlements").GetSnapshotAsync(token)).Count == 0 &&
                (await database.Collection("balanceTransactions").GetSnapshotAsync(token)).Count == 0,
                "A new emulator verification project must have no settled wagers before browser actions.");

            var games = new[]
            {
                new CardCase("andar-bahar", "{\"side\":\"andar\",\"stake\":0.50}", "{\"side\":\"bahar\",\"stake\":0.50}"),
                new CardCase("dragon-tiger", "{\"dragonStake\":0.50}", "{\"dragonStake\":1.00}")
            };
            var balance = InitialCents;
            var settled = new List<object>();
            foreach (var game in games)
            {
                var result = await VerifyCardAsync(database, transport, browser, game, session, otherSession, balance, token);
                balance = result.BalanceAfterCents;
                settled.Add(result);
            }
            Require(await BalanceAsync(database, Player, token) == balance &&
                await BalanceAsync(database, OtherPlayer, token) == InitialCents, "Final balances escaped account isolation.");
            var receipts = (await database.Collection("walletRoundSettlements").GetSnapshotAsync(token)).Documents;
            Require(receipts.Count == 2 && receipts.Select(item => item.GetValue<string>("gameId"))
                .Order(StringComparer.Ordinal).SequenceEqual(games.Select(item => item.GameId).Order(StringComparer.Ordinal)),
                "The two real game actions must produce exactly two independently scoped wallet settlements.");
            return new
            {
                success = true,
                projectId,
                emulatorOnly = true,
                packagesVerified = selected.Length,
                authenticatedGameWalletRoundTrips = settled,
                unsignedChildRejected = true,
                accountMismatchRejected = true,
                idempotentResponsesPreserved = true,
                originalAuditIntentPreserved = true
            };
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await app.StopAsync(shutdown.Token);
        }
    }

    private static async Task<CardResult> VerifyCardAsync(FirestoreDb database, IGamePackageTransport transport,
        HttpClient browser, CardCase game, string session, string otherSession, long beforeCents, CancellationToken token)
    {
        var path = $"/api/games/{game.GameId}/rounds";
        // The same actor and command key across both games catches accidental shared
        // wallet registrations or operation scopes without relying on generated deals.
        const string operation = "shared-backend-verification-command-0001";
        var beforeReceipts = (await database.Collection("walletRoundSettlements").GetSnapshotAsync(token)).Documents;
        var beforeLegs = (await database.Collection("balanceTransactions").GetSnapshotAsync(token)).Documents;
        using (var direct = Message(HttpMethod.Post, "https://game-package.internal" + path, session, game.Body, operation, Player))
        using (var unsigned = await transport.SendAsync(game.GameId, direct, token))
            Require(unsigned.StatusCode == HttpStatusCode.Forbidden, "A browser cookie cannot authenticate an unsigned child request.");
        using (var anonymous = Message(HttpMethod.Get, $"/api/games/{game.GameId}/status", null))
        using (var unauthenticated = await browser.SendAsync(anonymous, token))
            Require(unauthenticated.StatusCode == HttpStatusCode.Unauthorized, "The full root pipeline must reject an unauthenticated game request.");
        using (var wrongAccount = await SendAsync(browser, HttpMethod.Post, path, session, game.Body, operation, OtherPlayer, token))
            Require(wrongAccount.StatusCode == HttpStatusCode.Forbidden, "An expected-account mismatch must fail before the child can wager.");
        SameDocuments(beforeReceipts, (await database.Collection("walletRoundSettlements").GetSnapshotAsync(token)).Documents,
            "A rejected request created a settlement.");
        SameDocuments(beforeLegs, (await database.Collection("balanceTransactions").GetSnapshotAsync(token)).Documents,
            "A rejected request changed the ledger.");

        using var created = await SendAsync(browser, HttpMethod.Post, path, session, game.Body, operation, Player, token);
        Require(created.StatusCode == HttpStatusCode.Created, $"Authenticated {game.GameId} creation returned {(int)created.StatusCode}, expected 201.");
        var originalBytes = await created.Content.ReadAsByteArrayAsync(token);
        using var response = JsonDocument.Parse(originalBytes);
        var roundId = response.RootElement.GetProperty("roundId").GetString()
            ?? throw new InvalidOperationException("The game returned no round identity.");
        Require(roundId.Length == 64 && roundId.All(char.IsAsciiHexDigit), "A durable round must have its authoritative operation identity.");
        var payoutCents = Cents(response.RootElement.GetProperty("totalReturn").GetDecimal());
        var afterCents = checked(beforeCents - WagerCents + payoutCents);
        Require(Cents(response.RootElement.GetProperty("balance").GetDecimal()) == afterCents,
            "The game response does not match the exact wallet settlement balance.");
        var settlementId = GameWalletSettlementIdentity.CreateId(game.GameId, Player, roundId);
        var receiptReference = database.Collection("walletRoundSettlements").Document(settlementId);
        var receipt = await receiptReference.GetSnapshotAsync(token);
        Require(receipt.Exists && receipt.GetValue<string>("gameId") == game.GameId &&
            receipt.GetValue<string>("userId") == Player && receipt.GetValue<string>("operationId") == roundId &&
            receipt.GetValue<long>("wagerCents") == WagerCents && receipt.GetValue<long>("payoutCents") == payoutCents &&
            receipt.GetValue<long>("balanceBeforeCents") == beforeCents && receipt.GetValue<long>("balanceAfterCents") == afterCents,
            "The platform receipt lost game, actor, operation, or exact-money binding.");
        var legs = (await database.Collection("balanceTransactions").WhereEqualTo("walletSettlementId", settlementId)
            .GetSnapshotAsync(token)).Documents;
        Require(legs.Count == (payoutCents > 0 ? 2 : 1) && legs.All(item =>
            item.GetValue<string>("gameId") == game.GameId && item.GetValue<string>("userId") == Player &&
            item.GetValue<string>("walletSettlementId") == settlementId), "Ledger legs escaped the correct game or account scope.");
        Require(legs.Single(item => item.Id == settlementId + "-wager").GetValue<long>("amountCents") == -WagerCents,
            "The canonical wager leg does not match the exact charge.");
        if (payoutCents > 0)
            Require(legs.Single(item => item.Id == settlementId + "-payout").GetValue<long>("amountCents") == payoutCents,
                "The canonical payout leg does not match the exact return.");

        using (var retry = await SendAsync(browser, HttpMethod.Post, path, session, game.Body, operation, Player, token))
        {
            var retryBytes = await retry.Content.ReadAsByteArrayAsync(token);
            Require(retry.StatusCode == created.StatusCode && retry.Headers.Location?.OriginalString == created.Headers.Location?.OriginalString &&
                originalBytes.SequenceEqual(retryBytes),
                "Retrying the original command must preserve its original response and location.");
        }
        using (var changed = await SendAsync(browser, HttpMethod.Post, path, session, game.ChangedBody, operation, Player, token))
            Require(changed.StatusCode == HttpStatusCode.Conflict, "Changing a command body under its original key must conflict.");
        using (var foreign = await SendAsync(browser, HttpMethod.Get, path + "/" + roundId, otherSession, null, null, OtherPlayer, token))
            Require(foreign.StatusCode == HttpStatusCode.NotFound, "Another account must not read the round.");
        Require((await receiptReference.GetSnapshotAsync(token)).UpdateTime == receipt.UpdateTime,
            "Replay modified an immutable wallet settlement.");
        SameDocuments(legs, (await database.Collection("balanceTransactions").WhereEqualTo("walletSettlementId", settlementId)
            .GetSnapshotAsync(token)).Documents, "Replay changed the original ledger legs.");
        Require((await database.Collection("walletRoundSettlements").GetSnapshotAsync(token)).Count == beforeReceipts.Count + 1 &&
            (await database.Collection("balanceTransactions").GetSnapshotAsync(token)).Count == beforeLegs.Count + legs.Count,
            "Retries, rejected commands, or foreign reads created additional charges.");
        Require(await BalanceAsync(database, Player, token) == afterCents &&
            await BalanceAsync(database, OtherPlayer, token) == InitialCents, "A command changed the wrong account's balance.");

        var events = (await database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("instanceId", roundId).GetSnapshotAsync(token)).Documents;
        Require(events.Count == 1, "A real accepted round must preserve exactly one original engine observation.");
        var evidence = events.Single();
        Require(evidence.GetValue<string>("gameId") == game.GameId && evidence.GetValue<string>("userId") == Player,
            "The accepted engine observation lost its originating account or game.");
        using var payload = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var observation = payload.RootElement.GetProperty("request").Deserialize<GameRequestObservation>(Json);
        Require(observation is not null && !string.IsNullOrWhiteSpace(observation.RequestId) &&
            observation.RequestId == evidence.GetValue<string>("requestId") && observation.GameId == game.GameId &&
            observation.UserId == Player && observation.CommandId == operation && observation.Method == "POST" && observation.Path == path,
            "The child did not preserve the root's authenticated original request observation.");
        var intent = await database.Collection("gameSecurityRequestIntents").Document(observation!.RequestId).GetSnapshotAsync(token);
        Require(intent.Exists, "The accepted child action lacks the root's durable security intent.");
        var storedObservation = JsonSerializer.Deserialize<GameRequestObservation>(intent.GetValue<string>("observationJson"), Json);
        Require(storedObservation == observation, "The engine evidence differs from the root's original durable request intent.");
        Require((await database.Collection(RoundAuditDocuments.Summaries).WhereEqualTo("roundId", roundId).GetSnapshotAsync(token)).Count == 1,
            "The accepted round lacks its completed platform audit summary.");
        return new(game.GameId, WagerCents, payoutCents, afterCents, legs.Count);
    }

    private static async Task<string> CreateAccountAsync(FirestoreDb database, FirestoreAccountStore store,
        string user, string name, CancellationToken token)
    {
        Require((await store.CreateAsync(user, name, name.ToUpperInvariant(), user + "@example.test",
            "emulator-verification", "active", token)).Value is not null, "Failed to seed a real emulator account.");
        await database.Collection("userBalances").Document(user + "_slotsCredits").UpdateAsync(new Dictionary<string, object>
        {
            ["available"] = InitialCents / 100,
            ["availableFractionalCents"] = 0L
        }, cancellationToken: token);
        var session = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await store.CreateSessionAsync(GameSecurityEventDocuments.Hash(session), user, now, now.AddHours(1), null, token);
        return session;
    }

    private static async Task<long> BalanceAsync(FirestoreDb database, string user, CancellationToken token)
    {
        var balance = await database.Collection("userBalances").Document(user + "_slotsCredits").GetSnapshotAsync(token);
        Require(balance.GetValue<string>("userId") == user && balance.GetValue<string>("currencyId") == "slotsCredits",
            "The authoritative balance document lost its account or currency metadata.");
        var cents = Cents(Convert.ToDecimal(balance.GetValue<object>("available")));
        return checked(cents + balance.GetValue<long>("availableFractionalCents"));
    }

    private static HttpRequestMessage Message(HttpMethod method, string path, string? session, string? body = null,
        string? operation = null, string? expectedAccount = null)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Add("Origin", "https://fortuneforgegame.web.app");
        if (session is not null) message.Headers.Add("Cookie", $"{AccountSessionCookie.Name}={session}");
        if (operation is not null) message.Headers.Add("Idempotency-Key", operation);
        if (expectedAccount is not null) message.Headers.Add("X-FortuneForge-Expected-Account", expectedAccount);
        if (body is not null) message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return message;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient browser, HttpMethod method, string path,
        string session, string? body, string? operation, string expectedAccount, CancellationToken token)
    {
        // Exercise the actual rate limiter without exhausting its per-account token bucket.
        if (method != HttpMethod.Get) await Task.Delay(TimeSpan.FromMilliseconds(600), token);
        using var request = Message(method, path, session, body, operation, expectedAccount);
        return await browser.SendAsync(request, token);
    }

    private static void SameDocuments(IReadOnlyList<DocumentSnapshot> before, IReadOnlyList<DocumentSnapshot> after, string message) =>
        Require(before.Select(item => (item.Id, item.UpdateTime)).OrderBy(item => item.Id, StringComparer.Ordinal)
            .SequenceEqual(after.Select(item => (item.Id, item.UpdateTime)).OrderBy(item => item.Id, StringComparer.Ordinal)), message);

    private static long Cents(decimal value)
    {
        var exact = value * 100m;
        Require(exact == decimal.Truncate(exact), "Money contains sub-cent precision.");
        return checked((long)exact);
    }

    private static void ValidateEmulator()
    {
        var value = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        Require(!string.IsNullOrWhiteSpace(value) && Uri.TryCreate("http://" + value, UriKind.Absolute, out var address) &&
            address.IsLoopback && address.UserInfo.Length == 0 && address.AbsolutePath == "/" &&
            address.Query.Length == 0 && address.Fragment.Length == 0 && value.LastIndexOf(':') > 0 &&
            int.TryParse(value[(value.LastIndexOf(':') + 1)..], out var port) && port is > 0 and <= 65535 && port == address.Port,
            "FIRESTORE_EMULATOR_HOST must name an existing explicit loopback emulator endpoint; production is forbidden.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record SelectedComposition(int ContractVersion, SelectedPackage[] Packages);
    private sealed record SelectedPackage(string GameId, string RuntimeGameId, string ReleaseId);
    private sealed record CardCase(string GameId, string Body, string ChangedBody);
    private sealed record CardResult(string GameId, long WagerCents, long PayoutCents, long BalanceAfterCents, int LedgerLegs);
}
