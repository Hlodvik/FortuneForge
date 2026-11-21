using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Asteroids.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Tests.Solitaire;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FortuneForge.Games.Asteroids.Tests;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class AsteroidsPaidRunTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LostDebitResponseRecoversCommittedSeedAndOnlyOneCharge()
    {
        var database = Database();
        var clock = new Clock();
        var wallet = new Wallet { LoseNextResponse = true };
        var rules = new ArcadeCompetitionRulesOptions();
        var window = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, clock.Now, rules.TimeZone);
        var admission = new ArcadeCompetitionPaidAdmission(window.StartsAtUtc, window.EndsAtUtc);
        var bodySha256 = BodyHash(JsonSerializer.Serialize(admission));
        var firstRuns = new AsteroidsPaidRuns(() => 42);
        var first = Service(database, wallet, firstRuns, clock, rules);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => first.StartAsync("lost-asteroids-charge", "alice",
            ArcadeCompetitionWindowKind.Daily, default, admission, requestBodySha256: bodySha256));
        clock.Now = window.EndsAtUtc.AddHours(1);
        var recoveredRuns = new AsteroidsPaidRuns(() => 99);
        var recovered = await Service(database, wallet, recoveredRuns, clock, rules)
            .StartAsync("lost-asteroids-charge", "alice", ArcadeCompetitionWindowKind.Daily, default, admission,
                requestBodySha256: bodySha256);
        Assert.Equal(42UL, recovered.Run.Run.Seed);
        Assert.Equal(window.StartsAtUtc, recovered.Attempt.Competition.StartsAtUtc);
        Assert.Single(wallet.Charges);
        var run = Assert.Single((await database.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);
        Assert.Equal("000000000000002a", run.GetValue<string>("seedHex"));
        Assert.Single((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<ArcadeCompetitionEntryConflictException>(() => first.StartAsync("lost-asteroids-charge", "alice",
            ArcadeCompetitionWindowKind.Daily, default, admission, requestBodySha256: BodyHash("different exact bytes")));
        Assert.Single(wallet.Charges);
    }

    [Fact]
    public async Task NeverAdmittedPreviousWindowAndLegacyRecoveryCannotChargeTheNewWindow()
    {
        var database = Database();
        var wallet = new Wallet();
        var clock = new Clock();
        var rules = new ArcadeCompetitionRulesOptions();
        var originalWindow = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, clock.Now, rules.TimeZone);
        var admission = new ArcadeCompetitionPaidAdmission(originalWindow.StartsAtUtc, originalWindow.EndsAtUtc);
        clock.Now = originalWindow.EndsAtUtc.AddHours(1);
        var service = Service(database, wallet, new(() => 42), clock, rules);
        await Assert.ThrowsAsync<ArcadeCompetitionEntryPausedException>(() => service.StartAsync("never-admitted-asteroids",
            "alice", ArcadeCompetitionWindowKind.Daily, default, admission));
        await Assert.ThrowsAsync<ArcadeCompetitionEntryPausedException>(() => service.StartAsync("legacy-ambiguous-asteroids",
            "alice", ArcadeCompetitionWindowKind.Daily, default, recoverOnly: true));
        Assert.Empty(wallet.Charges);
        Assert.Empty((await database.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CompletionAndRolloverRetryReturnTheOriginalOutcomeWithoutChargingOrWritingTwice()
    {
        var database = Database();
        var wallet = new Wallet();
        var clock = new Clock();
        var rules = new ArcadeCompetitionRulesOptions();
        var runs = new AsteroidsPaidRuns(() => 42);
        var service = Service(database, wallet, runs, clock, rules);
        var started = await service.StartAsync("asteroids-paid-complete", "alice", ArcadeCompetitionWindowKind.Daily, default);
        var replay = new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, []);
        var completed = await service.CompleteAsync(started.Attempt.Competition, started.Run.Run.RunId, "alice", replay, default);
        clock.Now = started.Attempt.Competition.EndsAtUtc.AddDays(1);
        var retry = await Service(database, wallet, new(() => 99), clock, rules)
            .CompleteAsync(started.Attempt.Competition.WindowKind, started.Run.Run.RunId, "alice", replay, default);
        Assert.True(retry.WasAlreadyCompleted);
        Assert.Equal(completed with { WasAlreadyCompleted = true }, retry);
        Assert.Single(wallet.Charges);
        Assert.Equal(2, (await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Count);
        Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        var recovered = await service.GetAsync(started.Attempt.Competition.WindowKind, started.Run.Run.RunId, "alice", default);
        Assert.Equal("completed", recovered.Status);
        Assert.Equal(started.Attempt.Competition.StartsAtUtc, recovered.StartsAtUtc);
        Assert.Equal(completed.Score, recovered.Score);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CompleteAsync(started.Attempt.Competition,
            started.Run.Run.RunId, "alice", new(AsteroidsReplayEvaluator.MaximumReplaySteps, [new(0, AsteroidsInput.Fire)]), default));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CompleteAsync(started.Attempt.Competition,
            started.Run.Run.RunId, "bob", replay, default));
    }

    [Fact]
    public async Task HistoricalPaidAttemptUsesItsOriginalRunAndLegacyLedgerWithoutAnotherCharge()
    {
        var database = Database();
        var wallet = new Wallet();
        var clock = new Clock();
        var rules = new ArcadeCompetitionRulesOptions();
        var window = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, clock.Now, rules.TimeZone);
        var competition = new ArcadeCompetitionIdentity("asteroids", window.Kind, window.StartsAtUtc, window.EndsAtUtc);
        var attempt = (await new FirestoreArcadeCompetitionStore(database).StartAttemptAsync(
            new("legacy-asteroids-attempt", competition, "alice", 75, Now.AddMinutes(-20)), default)).Attempt;
        var legacyRuns = new AsteroidsPaidRuns(() => 0x000000010000002a);
        var legacyRun = legacyRuns.PrepareRun(attempt);
        await database.RunTransactionAsync(transaction =>
        {
            legacyRuns.WriteRun(transaction, database, legacyRuns.RunDocument(database, attempt), attempt, legacyRun,
                $"arcade-competition-{attempt.DocumentId}-entry");
            return Task.FromResult(true);
        });
        wallet.LegacyTransactions.Add($"arcade-competition-{attempt.DocumentId}-entry");
        var service = Service(database, wallet, new(() => 99), clock, rules);
        var retry = await service.StartAsync(attempt.AttemptId, "alice", competition.WindowKind, default,
            requestBodySha256: BodyHash(""));
        Assert.True(retry.WasAlreadyRecorded);
        Assert.Equal(legacyRun, retry.Run);
        Assert.Equal(75, retry.Attempt.EntryFeeCents);
        Assert.Empty(wallet.Charges);
        var completed = await service.CompleteAsync(competition, legacyRun.Run.RunId, "alice",
            new(AsteroidsReplayEvaluator.MaximumReplaySteps, []), default);
        Assert.False(completed.WasAlreadyCompleted);
        Assert.Empty(wallet.Charges);
        clock.Now = competition.EndsAtUtc.AddMinutes(1);
        var store = new FirestoreArcadeCompetitionStore(database);
        var executor = new ArcadeCompetitionSettlementExecutor(store,
            new ArcadeCompetitionSettlementPlanningService(store, clock, rules), wallet, clock);
        wallet.LoseNextPayoutResponse = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => executor.ExecuteAsync(competition, default));
        var settlement = await executor.ExecuteAsync(competition, default);
        Assert.Equal(75, Assert.Single(settlement.PayoutPlan.Instructions).AmountCents);
        Assert.True((await executor.ExecuteAsync(competition, default)).WasAlreadyCompleted);
        Assert.Single(wallet.Credits);
    }

    [Fact]
    public async Task NonterminalInputLeavesThePaidRunOpenAndDoesNotRepeatTheDebit()
    {
        var database = Database();
        var wallet = new Wallet();
        var service = Service(database, wallet, new(() => 42), new Clock(), new());
        var started = await service.StartAsync("asteroids-short-paid", "alice", ArcadeCompetitionWindowKind.Daily, default);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CompleteAsync(started.Attempt.Competition,
            started.Run.Run.RunId, "alice", new(1, []), default));
        var saved = await database.Collection("asteroidsRuns").Document(started.Attempt.DocumentId).GetSnapshotAsync();
        Assert.Equal("started", saved.GetValue<string>("status"));
        Assert.Single(wallet.Charges);
        Assert.Empty((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task PaidHttpContractsRecoverOriginalWindowAndNeverChargeAnUnacceptedExpiredIntent()
    {
        var database = Database();
        var wallet = new Wallet();
        var clock = new Clock();
        var rules = new ArcadeCompetitionRulesOptions();
        var service = Service(database, wallet, new(() => 42), clock, rules);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton(rules);
        builder.Services.AddSingleton(service);
        builder.Services.AddArcadeCompetitionStandings();
        await using var app = builder.Build();
        app.UseRouting();
        app.UseLocalGameWorkerCommandDigests(context => context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address));
        app.MapAsteroidsPaidRuns(context => context.Request.Headers["Test-User"].FirstOrDefault(),
            context => context.Request.Headers.ContainsKey("Test-Missing-Digest") ? null : GameWorkerAuthentication.GetCommandBodySha256(context));
        app.MapArcadeCompetitionStandings("asteroids");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        const string prefix = "/api/arcade-competitions/asteroids";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(prefix + "/daily/attempts", null)).StatusCode);
        client.DefaultRequestHeaders.Add("Test-User", "alice");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "asteroids-http-paid-start");
        var window = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, clock.Now, rules.TimeZone);
        var intent = new ArcadeCompetitionPaidAdmission(window.StartsAtUtc, window.EndsAtUtc);
        client.DefaultRequestHeaders.Add("Test-Missing-Digest", "true");
        using var unrecorded = await client.PostAsJsonAsync(prefix + "/daily/attempts", intent);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unrecorded.StatusCode);
        Assert.Empty(wallet.Charges);
        Assert.Empty((await database.Collection(ArcadeCompetitionPaidEntries<AsteroidsPaidRun>.PreparedCollection).GetSnapshotAsync()).Documents);
        client.DefaultRequestHeaders.Remove("Test-Missing-Digest");
        var admittedResponse = await client.PostAsJsonAsync(prefix + "/daily/attempts", intent);
        admittedResponse.EnsureSuccessStatusCode();
        var admitted = await admittedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(admitted.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(100, admitted.GetProperty("entryFeeCents").GetInt64());
        Assert.Equal("000000000000002a", admitted.GetProperty("seedHex").GetString());
        var runId = admitted.GetProperty("runId").GetString();
        using var differentBytes = new StringContent(JsonSerializer.Serialize(intent) + "\n", Encoding.UTF8, "application/json");
        using var conflictingLocalStart = await client.PostAsync(prefix + "/daily/attempts", differentBytes);
        Assert.Equal(HttpStatusCode.Conflict, conflictingLocalStart.StatusCode);
        var payload = new { rulesVersion = 2, totalSteps = 3600, commands = Array.Empty<object>() };
        var completedResponse = await client.PostAsJsonAsync($"{prefix}/daily/runs/{runId}/replay", payload);
        completedResponse.EnsureSuccessStatusCode();
        var completed = await completedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(completed.GetProperty("wasReplay").GetBoolean());
        clock.Now = window.EndsAtUtc.AddHours(1);
        var repeated = await client.PostAsJsonAsync($"{prefix}/daily/runs/{runId}/replay", payload);
        repeated.EnsureSuccessStatusCode();
        Assert.True((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("wasReplay").GetBoolean());
        var recoveredEntry = await client.PostAsJsonAsync(prefix + "/daily/attempts?recoverOnly=true", intent);
        recoveredEntry.EnsureSuccessStatusCode();
        Assert.Equal(runId, (await recoveredEntry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetString());
        var recovery = await client.GetFromJsonAsync<JsonElement>($"{prefix}/daily/runs/{runId}");
        Assert.Equal(window.StartsAtUtc, recovery.GetProperty("startsAtUtc").GetDateTimeOffset());
        Assert.Equal("completed", recovery.GetProperty("status").GetString());
        var history = await client.GetFromJsonAsync<JsonElement>($"{prefix}/daily?at={Uri.EscapeDataString(window.StartsAtUtc.ToString("O"))}");
        Assert.Equal(window.StartsAtUtc, history.GetProperty("startsAtUtc").GetDateTimeOffset());
        Assert.Equal(100, history.GetProperty("solePlayerRefund").GetProperty("amountCents").GetInt64());
        var allTime = await client.GetFromJsonAsync<JsonElement>(prefix + "/all-time");
        Assert.Equal(completed.GetProperty("score").GetInt64(), allTime.GetProperty("leaderboard")[0].GetProperty("score").GetInt64());
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "asteroids-never-recorded-http");
        var absent = await client.PostAsJsonAsync(prefix + "/daily/attempts?recoverOnly=true", intent);
        Assert.Equal(HttpStatusCode.Conflict, absent.StatusCode);
        Assert.Equal("arcade-competition-entry-not-recorded", (await absent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Single(wallet.Charges);
    }

    [Fact]
    public async Task SignedExactBodyDigestProtectsPendingAndAcceptedStartsAcrossRollover()
    {
        var database = Database();
        var wallet = new Wallet { LoseNextResponse = true };
        var clock = new Clock();
        var rules = new ArcadeCompetitionRulesOptions();
        var service = Service(database, wallet, new(() => 42), clock, rules);
        var signingKey = RandomNumberGenerator.GetBytes(32);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(signingKey);
        builder.Services.AddSingleton(service);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(AsteroidsModule.GameId);
        app.MapAsteroidsPaidRuns(GameWorkerAuthentication.GetAccountId, GameWorkerAuthentication.GetCommandBodySha256);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        const string target = "/api/arcade-competitions/asteroids/daily/attempts";
        const string operationKey = "asteroids-signed-exact-body";
        var window = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, clock.Now, rules.TimeZone);
        var body = JsonSerializer.Serialize(new
        {
            expectedWindowStartsAtUtc = window.StartsAtUtc.ToString("O"),
            expectedWindowEndsAtUtc = window.EndsAtUtc.ToString("O"),
        });

        async Task<HttpResponseMessage> StartAsync(string exactBody, bool recoverOnly = false)
        {
            var path = target + (recoverOnly ? "?recoverOnly=true" : "");
            var bytes = Encoding.UTF8.GetBytes(exactBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(bytes) };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Headers.Add("Idempotency-Key", operationKey);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader,
                GameWorkerAuthentication.CreateAssertion(AsteroidsModule.GameId, "alice", "POST", path, bytes,
                    signingKey, DateTimeOffset.UtcNow, headers: new Dictionary<string, string> { ["Idempotency-Key"] = operationKey }));
            return await client.SendAsync(request);
        }

        using var lost = await StartAsync(body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        var prepared = Assert.Single((await database.Collection(ArcadeCompetitionPaidEntries<AsteroidsPaidRun>.PreparedCollection)
            .GetSnapshotAsync()).Documents);
        using var intent = JsonDocument.Parse(prepared.GetValue<string>("intentJson"));
        Assert.Equal(BodyHash(body), intent.RootElement.GetProperty("request").GetProperty("requestBodySha256").GetString());
        Assert.Single(wallet.Charges);
        clock.Now = window.EndsAtUtc.AddHours(1);

        // Whitespace changes no parsed admission terms, but it is still a different original command body.
        using var changedPending = await StartAsync(body + "\n", recoverOnly: true);
        Assert.Equal(HttpStatusCode.Conflict, changedPending.StatusCode);
        Assert.Empty((await database.Collection("asteroidsRuns").GetSnapshotAsync()).Documents);
        using var recovered = await StartAsync(body, recoverOnly: true);
        recovered.EnsureSuccessStatusCode();
        var accepted = await recovered.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("000000000000002a", accepted.GetProperty("seedHex").GetString());
        Assert.Equal(window.StartsAtUtc, accepted.GetProperty("startsAtUtc").GetDateTimeOffset());
        using var changedAccepted = await StartAsync(body + "\n", recoverOnly: true);
        Assert.Equal(HttpStatusCode.Conflict, changedAccepted.StatusCode);
        using var retry = await StartAsync(body, recoverOnly: true);
        retry.EnsureSuccessStatusCode();
        var repeated = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(repeated.GetProperty("wasReplay").GetBoolean());
        Assert.Equal(accepted.GetProperty("runId").GetString(), repeated.GetProperty("runId").GetString());
        Assert.Single(wallet.Charges);
    }

    private FirestoreDb Database()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-asteroids-paid-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static AsteroidsPaidRunService Service(FirestoreDb database, Wallet wallet, AsteroidsPaidRuns runs,
        Clock clock, ArcadeCompetitionRulesOptions rules) => new(database,
        new ArcadeCompetitionPaidEntries<AsteroidsPaidRun>(database, "asteroids", rules, wallet, runs), runs, clock, rules);

    private static string BodyHash(string body) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = AsteroidsPaidRunTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Wallet : IArcadeCompetitionEntryWallet, IArcadeCompetitionPayoutCreditor
    {
        public bool LoseNextResponse { get; set; }
        public bool LoseNextPayoutResponse { get; set; }
        public Dictionary<string, (string Digest, GameWalletSettlementReceipt Receipt)> Charges { get; } = new();
        public HashSet<string> LegacyTransactions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ArcadeCompetitionPayoutCreditResult> Credits { get; } = new(StringComparer.Ordinal);

        public Task<GameWalletSettlementReceipt> ChargeAsync(ArcadeCompetitionEntryCharge charge, CancellationToken cancellationToken)
        {
            var entry = charge.Entry;
            if (Charges.TryGetValue(entry.AttemptDocumentId, out var prior))
            {
                if (prior.Digest != charge.OutcomeSha256) throw new GameWalletConflictException();
                return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
            }
            var receipt = new GameWalletSettlementReceipt(
                GameWalletSettlementIdentity.CreateId(entry.GameId, entry.PlayerId, entry.AttemptDocumentId),
                entry.TransactionId, null, 1_000, 1_000 - entry.EntryFeeCents, 1_000 - entry.EntryFeeCents,
                Now, false, false);
            Charges.Add(entry.AttemptDocumentId, (charge.OutcomeSha256, receipt));
            if (LoseNextResponse)
            {
                LoseNextResponse = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }

        public Task<bool> VerifyLegacyChargeAsync(ArcadeCompetitionEntryDebit entry, CancellationToken cancellationToken) =>
            Task.FromResult(LegacyTransactions.Contains(entry.TransactionId));

        public Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(ArcadeCompetitionIdentity competition,
            ArcadeCompetitionCreditInstruction instruction, DateTimeOffset creditedAtUtc, CancellationToken cancellationToken)
        {
            if (Credits.TryGetValue(instruction.IdempotencyKey, out var prior))
                return Task.FromResult(prior with { WasAlreadyCredited = true });
            var receipt = new ArcadeCompetitionPayoutCreditResult(ArcadeCompetitionPayoutPlan.WalletTransactionId(instruction.IdempotencyKey),
                1_000 + instruction.AmountCents, creditedAtUtc, false);
            Credits.Add(instruction.IdempotencyKey, receipt);
            if (LoseNextPayoutResponse)
            {
                LoseNextPayoutResponse = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }
    }
}
