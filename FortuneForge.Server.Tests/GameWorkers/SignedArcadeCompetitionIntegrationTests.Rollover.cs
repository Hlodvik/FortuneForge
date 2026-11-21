using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Asteroids.Server;
using FortuneForge.Games.Flappy.Server;
using FortuneForge.Games.HorseFlight.Server;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Arcade.Competition;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed partial class SignedArcadeCompetitionIntegrationTests
{
    [Theory]
    [InlineData("asteroids", AsteroidsPaidRuns.CollectionName, "seedHex")]
    [InlineData("flappy", FlappyPaidRuns.CollectionName, "seed")]
    [InlineData("horse-flight", HorseFlightPaidRuns.CollectionName, "seed")]
    public async Task PaidIntentFirstRecoveryAfterWindowRolloverUsesOriginalAttempt(
        string game, string runCollection, string seedField)
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-arcade-rollover-{Guid.NewGuid():N}"[..29],
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var clock = new CompetitionClock(new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
        var accounts = new FirestoreAccountStore(database);
        var token = await CreateAccountAsync(accounts, Player);
        await database.Collection("userBalances").Document(Player + "_slotsCredits")
            .UpdateAsync(new Dictionary<string, object> { ["available"] = 5L, ["availableFractionalCents"] = 0L });
        var workerKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var walletKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var platform = await StartPlatformAsync(database, accounts, game, workerKey, walletKey, clock);
        var fault = new LoseWalletResponses(game);
        await using var worker = await StartWorkerAsync(database, game, platform.Urls.Single(), workerKey, walletKey, clock, fault);
        platform.Configuration[$"GameWorkers:{game}:Url"] = worker.Urls.Single();
        using var browser = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false })
            { BaseAddress = new Uri(platform.Urls.Single()) };
        var path = $"/api/arcade-competitions/{game}/daily";
        using var board = await ReadAsync(await SendAsync(browser, HttpMethod.Get, path, token: null));
        var starts = board.RootElement.GetProperty("startsAtUtc").GetDateTimeOffset();
        var ends = board.RootElement.GetProperty("endsAtUtc").GetDateTimeOffset();
        var body = $$"""
            {
              "expectedWindowStartsAtUtc": "{{starts:O}}",
              "expectedWindowEndsAtUtc": "{{ends:O}}"
            }
            """ + "\r\n";
        var startPath = path + "/attempts";
        var recoverPath = startPath + "?recoverOnly=true";

        using var lost = await SendAsync(browser, HttpMethod.Post, startPath, token, Operation, body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        Assert.True(fault.LostEntry);
        Assert.Empty(await DocumentsAsync(database, runCollection));
        var debit = Assert.Single(await DocumentsAsync(database, "balanceTransactions"));
        Assert.Equal(Player, debit.GetValue<string>("userId"));
        Assert.Equal(Operation, debit.GetValue<string>("idempotencyKey"));
        Assert.Equal(-100L, debit.GetValue<long>("amountCents"));
        var pending = Assert.Single(await DocumentsAsync(database, ArcadeCompetitionPaidEntries<AsteroidsPaidRun>.PreparedCollection));
        Assert.Equal("pending", pending.GetValue<string>("status"));
        var originalIntent = pending.GetValue<string>("intentJson");
        using var saved = JsonDocument.Parse(originalIntent);
        Assert.Equal(GameSecurityEventDocuments.Hash(body),
            saved.RootElement.GetProperty("request").GetProperty("requestBodySha256").GetString());
        var run = saved.RootElement.GetProperty("run");
        var identity = game == "asteroids" ? run.GetProperty("run") : run;
        var expectedRunId = identity.GetProperty("runId").GetString();
        var expectedSeed = game == "asteroids"
            ? JsonSerializer.Serialize(identity.GetProperty("seed").GetUInt64().ToString("x16", System.Globalization.CultureInfo.InvariantCulture))
            : identity.GetProperty("seed").GetRawText();

        // No run has been materialized yet: the very first recovery must find yesterday's paid intent.
        clock.Now = ends.AddSeconds(1);
        using var recovered = await ReadAsync(await SendAsync(browser, HttpMethod.Post, recoverPath, token, Operation, body));
        var value = recovered.RootElement;
        Assert.Equal(expectedRunId, value.GetProperty("runId").GetString());
        Assert.Equal(expectedSeed, value.GetProperty(seedField).GetRawText());
        Assert.Equal(starts, value.GetProperty("startsAtUtc").GetDateTimeOffset());
        Assert.Equal(ends, value.GetProperty("endsAtUtc").GetDateTimeOffset());
        Assert.Equal(100L, value.GetProperty("entryFeeCents").GetInt64());
        Assert.Equal(originalIntent, (await pending.Reference.GetSnapshotAsync()).GetValue<string>("intentJson"));

        var statusPath = $"{path}/runs/{expectedRunId}";
        using var status = await ReadAsync(await SendAsync(browser, HttpMethod.Get, statusPath, token));
        Assert.Equal(expectedRunId, status.RootElement.GetProperty("runId").GetString());
        Assert.Equal("started", status.RootElement.GetProperty("status").GetString());
        Assert.Equal(expectedSeed, status.RootElement.GetProperty(seedField).GetRawText());
        Assert.Equal(starts, status.RootElement.GetProperty("startsAtUtc").GetDateTimeOffset());
        Assert.Equal(ends, status.RootElement.GetProperty("endsAtUtc").GetDateTimeOffset());

        using var changed = await SendAsync(browser, HttpMethod.Post, recoverPath, token, Operation, body + " ");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var repeated = await ReadAsync(await SendAsync(browser, HttpMethod.Post, recoverPath, token, Operation, body));
        Assert.Equal(expectedRunId, repeated.RootElement.GetProperty("runId").GetString());
        Assert.Equal(expectedSeed, repeated.RootElement.GetProperty(seedField).GetRawText());
        Assert.Equal(starts, repeated.RootElement.GetProperty("startsAtUtc").GetDateTimeOffset());
        Assert.Equal(ends, repeated.RootElement.GetProperty("endsAtUtc").GetDateTimeOffset());
        Assert.True(repeated.RootElement.GetProperty("wasReplay").GetBoolean());
        using var unknown = await SendAsync(browser, HttpMethod.Post, recoverPath, token, Operation + "-unknown", body);
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);

        Assert.Single(await DocumentsAsync(database, runCollection));
        Assert.Single(await DocumentsAsync(database, ArcadeCompetitionFirestoreDocuments.AttemptsCollection));
        Assert.Single(await DocumentsAsync(database, ArcadeCompetitionPaidEntries<AsteroidsPaidRun>.PreparedCollection));
        Assert.Single(await DocumentsAsync(database, "walletRoundSettlements"));
        Assert.Equal(debit.Id, Assert.Single(await DocumentsAsync(database, "balanceTransactions")).Id);
        Assert.Equal(400L, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, Player, false, default));
    }
}
