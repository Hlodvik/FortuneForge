using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.HorseFlight.Server;
using FortuneForge.Server.Arcade.Competition;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.HorseFlight.Tests;

public sealed class HorseFlightPaidRunTests
{
    [FirestoreEmulatorFact]
    public async Task LostWalletResponseAndWindowRolloverRecoverTheOriginalSeedAndOneDebit()
    {
        var setup = Create();
        var originalWindow = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, setup.Time.Now, setup.Rules.TimeZone);
        var admission = new ArcadeCompetitionPaidAdmission(originalWindow.StartsAtUtc, originalWindow.EndsAtUtc);
        setup.Wallet.LoseNextChargeResponse = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => setup.Service.StartAsync("paid-lost-response-0001", "alice",
            ArcadeCompetitionWindowKind.Daily, default, admission));
        Assert.Equal(1, setup.Wallet.Debits);
        Assert.Empty((await setup.Database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Empty((await setup.Database.Collection(HorseFlightPaidRuns.CollectionName).GetSnapshotAsync()).Documents);
        setup.Time.Now = setup.Time.Now.AddDays(1);
        var replicaRuns = new HorseFlightPaidRuns(() => throw new InvalidOperationException("Recovery must use its prepared seed."));
        var replicaEntries = new ArcadeCompetitionPaidEntries<HorseFlightPaidRun>(setup.Database, HorseFlightModule.GameId,
            setup.Rules, setup.Wallet, replicaRuns);
        var replica = new HorseFlightPaidRunService(setup.Database, replicaEntries, replicaRuns, setup.Time, setup.Rules);
        var recovered = await replica.StartAsync("paid-lost-response-0001", "alice", ArcadeCompetitionWindowKind.Daily,
            default, admission);
        var retry = await setup.Service.StartAsync("paid-lost-response-0001", "alice", ArcadeCompetitionWindowKind.Daily,
            default, admission);
        Assert.Equal(17U, recovered.Run.Seed);
        Assert.Equal(originalWindow.StartsAtUtc, recovered.Attempt.Competition.StartsAtUtc);
        Assert.Equal(originalWindow.EndsAtUtc, recovered.Attempt.Competition.EndsAtUtc);
        Assert.Equal(recovered.Run.RunId, retry.Run.RunId);
        Assert.True(retry.WasAlreadyRecorded);
        Assert.Equal(1, setup.Wallet.Debits);
        Assert.Single((await setup.Database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Single((await setup.Database.Collection(HorseFlightPaidRuns.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Single((await setup.Database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
        Assert.Empty((await setup.Database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [FirestoreEmulatorFact]
    public async Task VerifiedOriginalBodyCanRecoverByQueryAfterRolloverButDifferentBytesConflict()
    {
        var setup = Create();
        var window = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, setup.Time.Now, setup.Rules.TimeZone);
        var admission = new ArcadeCompetitionPaidAdmission(window.StartsAtUtc, window.EndsAtUtc);
        const string originalBody = "{\"expectedWindowStartsAtUtc\":\"2026-11-01T22:00:00Z\",\"expectedWindowEndsAtUtc\":\"2026-11-02T22:00:00Z\"}";
        var originalDigest = GameSecurityEventDocuments.Hash(originalBody);
        var first = await setup.Service.StartAsync("paid-immutable-body-0001", "alice", ArcadeCompetitionWindowKind.Daily,
            default, admission, requestBodySha256: originalDigest);
        setup.Time.Now = setup.Time.Now.AddDays(1);
        var recovered = await setup.Service.StartAsync("paid-immutable-body-0001", "alice", ArcadeCompetitionWindowKind.Daily,
            default, admission, recoverOnly: true, requestBodySha256: originalDigest);
        Assert.Equal(first.Run.RunId, recovered.Run.RunId);
        Assert.True(recovered.WasAlreadyRecorded);
        await Assert.ThrowsAsync<ArcadeCompetitionEntryConflictException>(() => setup.Service.StartAsync("paid-immutable-body-0001", "alice",
            ArcadeCompetitionWindowKind.Daily, default, admission, recoverOnly: true,
            requestBodySha256: GameSecurityEventDocuments.Hash(" " + originalBody)));
        Assert.Equal(1, setup.Wallet.Debits);
    }

    [FirestoreEmulatorFact]
    public async Task NeverAdmittedOldWindowAndLegacyRecoverOnlyRequestsCannotCreateANewWager()
    {
        var setup = Create();
        var old = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, setup.Time.Now.AddDays(-1), setup.Rules.TimeZone);
        await Assert.ThrowsAsync<ArcadeCompetitionEntryPausedException>(() => setup.Service.StartAsync("never-admitted-0001", "alice",
            ArcadeCompetitionWindowKind.Daily, default, new(old.StartsAtUtc, old.EndsAtUtc)));
        await Assert.ThrowsAsync<ArcadeCompetitionEntryPausedException>(() => setup.Service.StartAsync("legacy-uncertain-0001", "alice",
            ArcadeCompetitionWindowKind.Daily, default, recoverOnly: true));
        Assert.Equal(0, setup.Wallet.Debits);
        Assert.Empty((await setup.Database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
        Assert.Empty((await setup.Database.Collection(HorseFlightPaidRuns.CollectionName).GetSnapshotAsync()).Documents);
    }

    [FirestoreEmulatorFact]
    public async Task SavedReplayAndHistoryRecoverAfterCutoffWithoutAnotherDebitOrAudit()
    {
        var setup = Create();
        var opened = await setup.Service.StartAsync("paid-complete-retry-0001", "alice", ArcadeCompetitionWindowKind.Daily, default);
        var replay = Replay(opened.Run.Seed, opened.Run.RulesVersion);
        setup.Time.Now = setup.Time.Now.AddSeconds(10);
        var completed = await setup.Service.CompleteAsync(ArcadeCompetitionWindowKind.Daily, opened.Run.RunId, "alice", replay, default);
        Assert.False(completed.WasReplay);
        setup.Time.Now = setup.Time.Now.AddDays(2);
        var retried = await setup.Service.CompleteAsync(ArcadeCompetitionWindowKind.Daily, opened.Run.RunId, "alice", replay, default);
        Assert.True(retried.WasReplay);
        Assert.Equal(completed.Score, retried.Score);
        var history = await setup.Service.GetAsync(ArcadeCompetitionWindowKind.Daily, opened.Run.RunId, "alice", default);
        Assert.Equal("completed", history.Status);
        Assert.NotNull(history.Replay);
        Assert.Equal(opened.Attempt.Competition.StartsAtUtc, history.StartsAtUtc);
        Assert.Equal(1, setup.Wallet.Debits);
        Assert.Equal(2, (await setup.Database.Collection("gameSecurityEvents").GetSnapshotAsync()).Count);
        Assert.Single((await setup.Database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => setup.Service.GetAsync(ArcadeCompetitionWindowKind.Daily,
            opened.Run.RunId, "bob", default));
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Service.GetAsync(ArcadeCompetitionWindowKind.Weekly,
            opened.Run.RunId, "alice", default));
        await setup.Runs.RunDocument(setup.Database, opened.Attempt).UpdateAsync("score", completed.Score + 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.GetAsync(ArcadeCompetitionWindowKind.Daily,
            opened.Run.RunId, "alice", default));
    }

    [FirestoreEmulatorFact]
    public async Task EveryHistoricalPaidRulesVersionAndSavedFeeResumeWithoutAnotherCharge()
    {
        var setup = Create();
        var window = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, setup.Time.Now, setup.Rules.TimeZone);
        var competition = new ArcadeCompetitionIdentity(HorseFlightModule.GameId, ArcadeCompetitionWindowKind.Daily, window.StartsAtUtc, window.EndsAtUtc);
        foreach (var version in new[] { 1, 2, 3, 4 })
        {
            var key = $"horse-legacy-paid-start-{version}";
            var stored = await new FirestoreArcadeCompetitionStore(setup.Database).StartAttemptAsync(
                new(key, competition, "alice", 75, setup.Time.Now.AddMinutes(-1)), default);
            var legacyRun = new HorseFlightPaidRun(HorseFlightPaidRuns.RunId(stored.Attempt), 41, version,
                "started", 0, [], [], 0, HorseFlightPhase.Running, null);
            await setup.Database.RunTransactionAsync(transaction =>
            {
                setup.Runs.WriteRun(transaction, setup.Database, setup.Runs.RunDocument(setup.Database, stored.Attempt), stored.Attempt,
                    legacyRun, $"arcade-competition-{stored.Attempt.DocumentId}-entry");
                return Task.FromResult(0);
            });
            if (version == 1)
                await setup.Runs.RunDocument(setup.Database, stored.Attempt).UpdateAsync("rulesVersion", FieldValue.Delete);
            var recovered = await setup.Service.StartAsync(key, "alice", ArcadeCompetitionWindowKind.Daily, default);
            Assert.True(recovered.WasAlreadyRecorded);
            Assert.Equal(75, recovered.Attempt.EntryFeeCents);
            Assert.Equal(version, recovered.Run.RulesVersion);
            var result = await setup.Service.CompleteAsync(ArcadeCompetitionWindowKind.Daily, legacyRun.RunId, "alice", Replay(41, version), default);
            var history = await setup.Service.GetAsync(ArcadeCompetitionWindowKind.Daily, legacyRun.RunId, "alice", default);
            Assert.Equal(version, history.RulesVersion);
            Assert.Equal(result.Score, history.Score);
            Assert.Equal(75, history.EntryFeeCents);
        }
        Assert.Equal(0, setup.Wallet.Debits);
        Assert.Equal(4, setup.Wallet.LegacyProofReads);
    }

    private static HorseFlightReplay Replay(uint seed, int version)
    {
        var state = HorseFlightEngine.Start(seed, rulesVersion: version);
        while (state.Phase == HorseFlightPhase.Running && state.Tick < HorseFlightReplayEvaluator.MaximumTicks)
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
        Assert.NotEqual(HorseFlightPhase.Running, state.Phase);
        return new(state.Tick, []) { RulesVersion = version };
    }

    [FirestoreEmulatorFact]
    public async Task OriginalPaidHttpUrlsBindExactBodySupportHistoryAndRecoverQueries()
    {
        var setup = Create();
        await using var app = await HttpAsync(setup);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "paid-http-start-0001");
        var window = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, setup.Time.Now, setup.Rules.TimeZone);
        var body = JsonSerializer.Serialize(new { expectedWindowStartsAtUtc = window.StartsAtUtc.ToString("O"), expectedWindowEndsAtUtc = window.EndsAtUtc.ToString("O") });
        var start = await client.PostAsync(HorseFlightPaidRunEndpoints.RoutePrefix + "/daily/attempts", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        using var issued = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var runId = issued.RootElement.GetProperty("runId").GetString()!;
        var seed = issued.RootElement.GetProperty("seed").GetUInt32();
        var replay = Replay(seed, issued.RootElement.GetProperty("rulesVersion").GetInt32());
        var payload = new CompleteHorseFlightRunRequest(replay.TotalTicks, replay.JumpTicks, replay.RightClickTicks);
        var complete = await client.PostAsJsonAsync(HorseFlightPaidRunEndpoints.RoutePrefix + "/daily/runs/" + runId + "/replay", payload);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        setup.Time.Now = setup.Time.Now.AddDays(1);
        var retry = await client.PostAsync(HorseFlightPaidRunEndpoints.RoutePrefix + "/daily/attempts?recoverOnly=true", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var recovered = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal(runId, recovered.RootElement.GetProperty("runId").GetString());
        Assert.True(recovered.RootElement.GetProperty("wasReplay").GetBoolean());
        var conflict = await client.PostAsync(HorseFlightPaidRunEndpoints.RoutePrefix + "/daily/attempts?recoverOnly=true", new StringContent(" " + body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var saved = await client.GetAsync(HorseFlightPaidRunEndpoints.RoutePrefix + "/daily/runs/" + runId);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var history = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
        Assert.Equal("completed", history.RootElement.GetProperty("status").GetString());
        var standings = await client.GetAsync(HorseFlightPaidRunEndpoints.RoutePrefix + "/daily?at=" + Uri.EscapeDataString(window.StartsAtUtc.ToString("O")));
        Assert.Equal(HttpStatusCode.OK, standings.StatusCode);
        using var board = JsonDocument.Parse(await standings.Content.ReadAsStringAsync());
        Assert.Equal(1, board.RootElement.GetProperty("totalUniquePlayers").GetInt32());
        Assert.Equal(1, setup.Wallet.Debits);
    }

    [FirestoreEmulatorFact]
    public async Task AnAuthenticatedHttpStartWithoutAVerifiedBodyDigestCannotCharge()
    {
        var setup = Create();
        await using var app = await HttpAsync(setup, _ => null);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "paid-http-unverified-0001");
        var response = await client.PostAsync(HorseFlightPaidRunEndpoints.RoutePrefix + "/daily/attempts", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, setup.Wallet.Debits);
        Assert.Empty((await setup.Database.Collection("arcadeCompetitionAttempts").GetSnapshotAsync()).Documents);
    }

    [FirestoreEmulatorFact]
    public async Task LostRefundResponseRetriesTheOriginalSettlementAndCreditsOnce()
    {
        var setup = Create();
        var entered = await setup.Service.StartAsync("paid-refund-retry-0001", "alice", ArcadeCompetitionWindowKind.Daily, default);
        await setup.Service.CompleteAsync(ArcadeCompetitionWindowKind.Daily, entered.Run.RunId, "alice", Replay(entered.Run.Seed, entered.Run.RulesVersion), default);
        setup.Time.Now = entered.Attempt.Competition.EndsAtUtc.AddSeconds(1);
        var store = new FirestoreArcadeCompetitionStore(setup.Database);
        var executor = new ArcadeCompetitionSettlementExecutor(store,
            new ArcadeCompetitionSettlementPlanningService(store, setup.Time, setup.Rules), setup.Wallet, setup.Time);
        setup.Wallet.LoseNextPayoutResponse = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => executor.ExecuteAsync(entered.Attempt.Competition, default));
        Assert.Equal(1, setup.Wallet.Credits);
        var settled = await executor.ExecuteAsync(entered.Attempt.Competition, default);
        var retry = await executor.ExecuteAsync(entered.Attempt.Competition, default);
        Assert.True(settled.SettlementState.IsCompleted);
        Assert.True(retry.WasAlreadyCompleted);
        var instruction = Assert.Single(settled.PayoutPlan.Instructions);
        Assert.Equal(ArcadeCompetitionPayoutKind.Refund, instruction.Kind);
        Assert.Equal(entered.Attempt.EntryFeeCents, instruction.AmountCents);
        Assert.Equal(1, setup.Wallet.Credits);
        Assert.Equal(10000, setup.Wallet.Balance);
    }

    private static Setup Create()
    {
        var database = new FirestoreDbBuilder { ProjectId = $"demo-horse-flight-paid-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var time = new Clock();
        var rules = new ArcadeCompetitionRulesOptions();
        var wallet = new Wallet();
        var runs = new HorseFlightPaidRuns(() => 17);
        var entries = new ArcadeCompetitionPaidEntries<HorseFlightPaidRun>(database, HorseFlightModule.GameId, rules, wallet, runs);
        return new(database, time, rules, wallet, runs, new HorseFlightPaidRunService(database, entries, runs, time, rules));
    }
    private static async Task<WebApplication> HttpAsync(Setup setup, Func<HttpContext, string?>? overrideDigest = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(setup.Database);
        builder.Services.AddSingleton<TimeProvider>(setup.Time);
        builder.Services.AddSingleton(setup.Rules);
        builder.Services.AddSingleton(setup.Service);
        builder.Services.AddArcadeCompetitionStandings();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Request.EnableBuffering();
            context.Items["test-body-digest"] = Convert.ToHexStringLower(await SHA256.HashDataAsync(context.Request.Body, context.RequestAborted));
            context.Request.Body.Position = 0;
            await next(context);
        });
        string? Digest(HttpContext context) => context.Items["test-body-digest"] as string;
        var digest = overrideDigest ?? Digest;
        app.MapHorseFlightPaidRuns(_ => "alice", () => true, digest);
        app.MapArcadeCompetitionStandings(HorseFlightModule.GameId);
        await app.StartAsync();
        return app;
    }
    private sealed record Setup(FirestoreDb Database, Clock Time, ArcadeCompetitionRulesOptions Rules, Wallet Wallet,
        HorseFlightPaidRuns Runs, HorseFlightPaidRunService Service);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Wallet : IArcadeCompetitionEntryWallet, IArcadeCompetitionPayoutCreditor
    {
        private readonly Dictionary<string, (string Digest, GameWalletSettlementReceipt Receipt)> receipts = [];
        private readonly Dictionary<string, ArcadeCompetitionPayoutCreditResult> payouts = [];
        public bool LoseNextPayoutResponse { get; set; }
        public int Credits { get; private set; }
        public long Balance { get; private set; } = 10000;
        public bool LoseNextChargeResponse { get; set; }
        public int Debits { get; private set; }
        public int LegacyProofReads { get; private set; }
        public Task<GameWalletSettlementReceipt> ChargeAsync(ArcadeCompetitionEntryCharge charge, CancellationToken token)
        {
            var entry = charge.Entry;
            if (receipts.TryGetValue(entry.AttemptDocumentId, out var prior))
            {
                if (prior.Digest != charge.OutcomeSha256) throw new GameWalletConflictException();
                return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
            }
            Debits++;
            var before = Balance;
            Balance -= entry.EntryFeeCents;
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(HorseFlightModule.GameId,
                entry.PlayerId, entry.AttemptDocumentId), entry.TransactionId, null,
                before, Balance, Balance, entry.EnteredAtUtc, false, false);
            receipts.Add(entry.AttemptDocumentId, (charge.OutcomeSha256, receipt));
            if (LoseNextChargeResponse) { LoseNextChargeResponse = false; throw new GameWalletUnavailableException(); }
            return Task.FromResult(receipt);
        }
        public Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(ArcadeCompetitionIdentity competition,
            ArcadeCompetitionCreditInstruction instruction, DateTimeOffset creditedAtUtc, CancellationToken token)
        {
            if (payouts.TryGetValue(instruction.IdempotencyKey, out var existing))
                return Task.FromResult(existing with { WasAlreadyCredited = true });
            Credits++;
            Balance += instruction.AmountCents;
            var receipt = new ArcadeCompetitionPayoutCreditResult(ArcadeCompetitionPayoutPlan.WalletTransactionId(instruction.IdempotencyKey),
                Balance, creditedAtUtc, false);
            payouts.Add(instruction.IdempotencyKey, receipt);
            if (LoseNextPayoutResponse) { LoseNextPayoutResponse = false; throw new GameWalletUnavailableException(); }
            return Task.FromResult(receipt);
        }
        public Task<bool> VerifyLegacyChargeAsync(ArcadeCompetitionEntryDebit entry, CancellationToken token)
        {
            LegacyProofReads++;
            return Task.FromResult(true);
        }
    }
}
