using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.TwentyFortyEight;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed class PackagedGameWorkerForwardingTests
{
    private static readonly IConfiguration Enabled2048 = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["GameWorkers:2048:ForwardPublicRoutes"] = "true" })
        .Build();

    [Fact]
    public void Puzzle_status_can_use_its_local_compatibility_route()
    {
        var context = Route("/api/games/2048/status", "api/games/2048/status");

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, new ConfigurationBuilder().Build()));
        var route = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(context, Enabled2048));
        Assert.Equal("2048", route.Id);
        Assert.False(route.AllowLocalFallback);
    }

    [Theory]
    [InlineData("2048")]
    [InlineData("drop-merge")]
    public void Fresh_puzzle_starts_select_the_worker_without_an_opt_in_and_fail_closed_locally(string gameId)
    {
        var path = $"/api/games/{gameId}/games";
        var context = Route(path, path, "POST");
        var configuration = new ConfigurationManager();
        var worker = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(context, configuration));
        Assert.Equal(gameId, worker.Id);
        Assert.False(worker.AllowLocalFallback);

        var local = gameId == "2048"
            ? new TwentyFortyEightController(null!, null!).Start(null, default)
            : new DropMergeController(null!, null!).Start(null, default);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(local).StatusCode);
    }

    [Theory]
    [InlineData("andar-bahar", "AndarBaharEnabled")]
    [InlineData("craps", "CrapsEnabled")]
    [InlineData("dragon-tiger", "DragonTigerEnabled")]
    [InlineData("hi-lo", "HiLoEnabled")]
    [InlineData("mines", "MinesPaidEnabled")]
    public void Signed_table_reads_keep_their_worker_while_new_wagers_are_paused(string gameId, string feature)
    {
        var path = $"/api/games/{gameId}/rounds/latest";
        var context = Route(path, path);
        var settings = new ConfigurationManager { [$"Features:{feature}"] = "false" };

        var worker = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(context, settings));
        Assert.Equal(gameId, worker.Id);
        Assert.False(worker.AllowLocalFallback);

        context.Request.Headers["X-FortuneForge-Practice"] = "TRUE";
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, settings));

        context.Request.Headers["X-FortuneForge-Practice"] = new Microsoft.Extensions.Primitives.StringValues(["true", "false"]);
        Assert.Same(worker, PackagedGameWorkerForwarding.MatchGame(context, settings));
    }

    [Theory]
    [InlineData("/api/games/20480/status", "api/games/2048/status", "GET")]
    [InlineData("/api/games/2048/status", "api/games/{name}/status", "GET")]
    [InlineData("/api/games/2048/status", "api/games/2048/status", "PATCH")]
    public void OnlyMatchingLiteralRoutesAndSupportedMethodsAreForwarded(string path, string pattern, string method)
    {
        var context = Route(path, pattern, method);

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, Enabled2048));
    }

    [Theory]
    [InlineData("/api/cards/blackjack/table/queue/example", "api/cards/blackjack/table/queue/{ticketId}", "DELETE", "blackjack")]
    [InlineData("/api/solitaire/queue/example", "api/solitaire/queue/{ticketId}", "DELETE", "solitaire")]
    [InlineData("/api/solitaire/free/runs/example/replay", "api/solitaire/free/runs/{runId}/replay", "POST", "free-solitaire")]
    public void LegacyPublicRoutesUseOnlyTheirExplicitlyEnabledWorker(string path, string pattern, string method, string gameId)
    {
        var context = Route(path, pattern, method);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{gameId}:ForwardPublicRoutes"] = "true"
        }).Build();

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, new ConfigurationBuilder().Build()));
        var worker = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(context, configuration));
        Assert.Equal(gameId, worker.Id);
        Assert.False(worker.AllowLocalFallback);
    }

    [Fact]
    public void FreeArcadeForwardingDoesNotActivatePaidCompetitionRouting()
    {
        const string gameId = "asteroids";
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{gameId}:ForwardFreeRoutes"] = "true",
            [$"GameWorkers:{gameId}:ForwardPublicRoutes"] = "true"
        }).Build();
        var free = Route("/api/arcade-competitions/asteroids/free/runs", "api/arcade-competitions/asteroids/free/runs", "POST");
        var paid = Route("/api/arcade-competitions/asteroids/day/attempts", "api/arcade-competitions/{gameId}/{period}/attempts", "POST");

        Assert.Equal(gameId, PackagedGameWorkerForwarding.MatchGame(free, settings)?.Id);
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(paid, settings));
        settings[$"GameWorkers:{gameId}:ForwardPaidCompetitionRoutes"] = "true";
        Assert.False(PackagedGameWorkerForwarding.MatchGame(paid, settings)!.AllowLocalFallback);
    }

    [Theory]
    [InlineData("asteroids")]
    [InlineData("flappy")]
    [InlineData("horse-flight")]
    public void ArcadeStandingsStayPublicWhileRunStatusRemainsWorkerOwned(string game)
    {
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{game}:ForwardPaidCompetitionRoutes"] = "true",
            ["Features:HorseFlightEnabled"] = "true",
        }).Build();
        var path = $"/api/arcade-competitions/{game}/daily";
        var standings = Route(path, "api/arcade-competitions/{gameId}/{period}", "GET");
        var run = Route(path + "/runs/a-run", "api/arcade-competitions/{gameId}/{period}/runs/{runId}", "GET");

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(standings, settings));
        Assert.Equal(game, PackagedGameWorkerForwarding.MatchGame(run, settings)?.Id);
    }

    [Fact]
    public void DirectHorseFlightRoutesUseTheFreeWorkerAndRespectTheFeatureGate()
    {
        const string game = "horse-flight";
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{game}:ForwardFreeRoutes"] = "true",
            ["Features:HorseFlightEnabled"] = "true",
        }).Build();
        var direct = Route("/api/games/horse-flight/runs/a-run/complete", "api/games/horse-flight/runs/{runId}/complete", "POST");
        var worker = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(PackagedGameWorkerForwarding.MatchGame(direct, settings));
        Assert.Equal(game, worker.Id);
        Assert.False(worker.AllowLocalFallback);

        settings["Features:HorseFlightEnabled"] = "false";
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(direct, settings));
    }

    [Fact]
    public async Task CardQueueCancellationForwardsAndSignsTheRawBodyAndVersion()
    {
        const string path = "/api/cards/blackjack/table/queue/example";
        const string actor = "queue-owner";
        const string gameId = "blackjack";
        const string originalBody = "{\"expectedVersion\":8,\"ticket\":\"example\"}";
        var body = Encoding.UTF8.GetBytes(originalBody);
        var key = new byte[32];
        var clock = new FixedWorkerClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{gameId}:Url"] = "http://localhost/",
            [$"GameWorkers:{gameId}:SigningKey"] = Convert.ToBase64String(key)
        }).Build();
        var context = Route(path, "api/cards/blackjack/table/queue/{ticketId}", "DELETE");
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();
        var expectedHeaders = new Dictionary<string, string>
        {
            ["Idempotency-Key"] = "original-queue-cancel",
            ["If-Match"] = "\"8\""
        };
        foreach (var (header, value) in expectedHeaders) context.Request.Headers[header] = value;

        using var handler = new ObservedWorkerHandler(async request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal(path, request.RequestUri?.PathAndQuery);
            Assert.Equal(originalBody, await request.Content!.ReadAsStringAsync());
            foreach (var (header, value) in expectedHeaders)
                Assert.Equal(value, Assert.Single(request.Headers.GetValues(header)));
            Assert.Equal(GameWorkerAuthentication.CreateAssertion(gameId, actor, "DELETE", path, body, key,
                clock.GetUtcNow(), headers: expectedHeaders),
                Assert.Single(request.Headers.GetValues(GameWorkerAuthentication.AssertionHeader)));
            var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"error\":\"stale-version\"}", Encoding.UTF8, "application/json")
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"9\"");
            return response;
        });
        using var http = new HttpClient(handler, disposeHandler: false);
        var environment = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }).Environment;
        var workers = new GameWorkerClient(http, configuration, environment, clock, NullLogger<GameWorkerClient>.Instance);
        var route = new PackagedGameWorkerForwarding.PackagedGame(gameId, "test", AllowLocalFallback: false, PathPrefix: "/api/cards/blackjack");

        await PackagedGameWorkerForwarding.ForwardAsync(context, workers, route, actor);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal("\"9\"", context.Response.Headers.ETag.ToString());
        Assert.Equal(1, handler.Calls);
    }

    private sealed class FixedWorkerClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task PaidArcadeRecoverySignsTheQueryAndKeepsTheOriginalBodyAndOperation()
    {
        const string gameId = "asteroids";
        const string path = "/api/arcade-competitions/asteroids/daily/attempts";
        const string query = "?recoverOnly=true";
        const string actor = "arcade-player";
        var body = "{ \"expectedWindowStartsAtUtc\": \"2026-11-20T22:00:00Z\", \"expectedWindowEndsAtUtc\": \"2026-11-21T22:00:00Z\" }"u8.ToArray();
        var headers = new Dictionary<string, string> { ["Idempotency-Key"] = "original-arcade-attempt" };
        var clock = new FixedWorkerClock(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var key = new byte[32];
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{gameId}:Url"] = "http://localhost/",
            [$"GameWorkers:{gameId}:SigningKey"] = Convert.ToBase64String(key),
        }).Build();
        var context = Route(path, "api/arcade-competitions/{gameId}/{period}/attempts", "POST");
        context.Request.QueryString = new QueryString(query);
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentType = "application/json";
        context.Request.Headers["Idempotency-Key"] = headers["Idempotency-Key"];
        context.Response.Body = new MemoryStream();

        using var handler = new ObservedWorkerHandler(async request =>
        {
            Assert.Equal(path + query, request.RequestUri?.PathAndQuery);
            Assert.Equal(body, await request.Content!.ReadAsByteArrayAsync());
            Assert.Equal(headers["Idempotency-Key"], Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            Assert.Equal(GameWorkerAuthentication.CreateAssertion(gameId, actor, "POST", path + query, body, key,
                clock.GetUtcNow(), headers: headers), Assert.Single(request.Headers.GetValues(GameWorkerAuthentication.AssertionHeader)));
            return new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("{\"code\":\"arcade-entry-closed\"}", Encoding.UTF8, "application/json"),
            };
        });
        using var http = new HttpClient(handler, disposeHandler: false);
        var environment = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }).Environment;
        var workers = new GameWorkerClient(http, configuration, environment, clock, NullLogger<GameWorkerClient>.Instance);
        var route = new PackagedGameWorkerForwarding.PackagedGame(gameId, "test", AllowLocalFallback: false,
            PathPrefix: "/api/arcade-competitions/asteroids");

        await PackagedGameWorkerForwarding.ForwardAsync(context, workers, route, actor);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class ObservedWorkerHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> observe) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return observe(request);
        }
    }

    [Fact]
    public void PracticeRequestsKeepTheirLocalRoute()
    {
        var context = Route("/api/games/2048/games", "api/games/2048/games", "POST");
        context.Request.Headers["X-FortuneForge-Practice"] = "true";

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, Enabled2048));
    }

    [Theory]
    [InlineData("GET", "", true)]
    [InlineData("POST", "/move", true)]
    [InlineData("POST", "/undo", false)]
    [InlineData("POST", "/continue", false)]
    [InlineData("POST", "/reset", false)]
    public void OnlyDurable2048SessionsUseTheWorker(string method, string action, bool enabled)
    {
        var settings = enabled ? Enabled2048 : new ConfigurationBuilder().Build();
        var durableId = Guid.CreateVersion7();
        var legacyId = Guid.NewGuid();
        var pattern = "api/games/2048/games/{gameId:guid}" + action;
        var durable = Route($"/api/games/2048/games/{durableId:D}{action}", pattern, method);
        durable.Request.RouteValues["gameId"] = durableId;
        var legacy = Route($"/api/games/2048/games/{legacyId:D}{action}", pattern, method);
        legacy.Request.RouteValues["gameId"] = legacyId.ToString("D");

        var worker = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(durable, settings));
        Assert.Equal("2048", worker.Id);
        Assert.False(worker.AllowLocalFallback);
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(legacy, settings));
    }

    [Fact]
    public void MissingSessionRouteValueIsNeverForwardedAsANewSession()
    {
        var context = Route($"/api/games/2048/games/{Guid.CreateVersion7():D}", "api/games/2048/games/{gameId:guid}");

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, Enabled2048));
    }

    [Fact]
    public void PracticeLabelNeverForwardsAnExistingDurable2048Session()
    {
        var durableId = Guid.CreateVersion7();
        var context = Route($"/api/games/2048/games/{durableId:D}/move", "api/games/2048/games/{gameId:guid}/move", "POST");
        context.Request.RouteValues["gameId"] = durableId;
        context.Request.Headers["X-FortuneForge-Practice"] = "true";

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(context, Enabled2048));
    }

    [Fact]
    public void ExistingDropMergeSessionsResumeOnlyOnTheirOwningBackend()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GameWorkers:drop-merge:ForwardPublicRoutes"] = "true" }).Build();
        var durableId = Guid.CreateVersion7();
        var legacyId = Guid.NewGuid();
        const string pattern = "api/games/drop-merge/games/{gameId:guid}/drop";
        var durable = Route($"/api/games/drop-merge/games/{durableId:D}/drop", pattern, "POST");
        durable.Request.RouteValues["gameId"] = durableId;
        var legacy = Route($"/api/games/drop-merge/games/{legacyId:D}/drop", pattern, "POST");
        legacy.Request.RouteValues["gameId"] = legacyId;

        Assert.Equal("drop-merge", PackagedGameWorkerForwarding.MatchGame(durable, disabled)?.Id);
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(legacy, enabled));
    }

    [Fact]
    public void OptInForwardsTheRegisteredArcaneFeatureRoutesOnly()
    {
        const string path = "/api/slots/arcane/tome";
        const string gameId = "arcane-archives-v1";
        var registered = Route(path, path, "POST", new GameWorkerPublicRoute(gameId, "/api/slots/arcane", "arcane-archives"));
        var ordinary = Route(path, path, "POST");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"GameWorkers:{gameId}:ForwardPublicRoutes"] = "true" }).Build();

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(ordinary, configuration));
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(registered, new ConfigurationBuilder().Build()));
        var worker = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(registered, configuration));
        Assert.Equal(gameId, worker.Id);
        Assert.False(worker.AllowLocalFallback);
    }

    [Theory]
    [InlineData("mines")]
    [InlineData("sic-bo")]
    [InlineData("keno")]
    public void ConfiguredNumberGameRoutesStayOnTheirWorkerWhenNewWagersArePaused(string gameId)
    {
        var context = Route($"/api/games/{gameId}/rounds", $"api/games/{gameId}/rounds", "POST");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{gameId}:ForwardPublicRoutes"] = "true",
        }).Build();

        var worker = Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(PackagedGameWorkerForwarding.MatchGame(context, configuration));
        Assert.Equal(gameId, worker.Id);
        Assert.False(worker.AllowLocalFallback);
    }

    [Fact]
    public void LiarsDiceKeepsLegacyMatchesLocalAndDurableMatchesOnTheirWorker()
    {
        const string pattern = "api/games/liars-dice/matches/{matchId:guid}/bid";
        var legacyId = Guid.NewGuid();
        var durableId = Guid.CreateVersion7();
        var legacy = Route($"/api/games/liars-dice/matches/{legacyId:D}/bid", pattern, "POST");
        var durable = Route($"/api/games/liars-dice/matches/{durableId:D}/bid", pattern, "POST");
        var starts = Route("/api/games/liars-dice/matches", "api/games/liars-dice/matches", "POST");
        legacy.Request.RouteValues["matchId"] = legacyId;
        durable.Request.RouteValues["matchId"] = durableId;
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:LiarsDiceEnabled"] = "true",
            ["GameWorkers:liars-dice:ForwardPublicRoutes"] = "true",
        }).Build();

        Assert.Null(PackagedGameWorkerForwarding.MatchGame(legacy, settings));
        Assert.Equal("liars-dice", PackagedGameWorkerForwarding.MatchGame(starts, settings)?.Id);
        settings["GameWorkers:liars-dice:ForwardPublicRoutes"] = "false";
        Assert.False(Assert.IsType<PackagedGameWorkerForwarding.PackagedGame>(
            PackagedGameWorkerForwarding.MatchGame(starts, settings)).AllowLocalFallback);
        Assert.False(PackagedGameWorkerForwarding.MatchGame(durable, settings)!.AllowLocalFallback);
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(legacy, settings));
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(
            Route("/api/games/liars-dice/status", "api/games/liars-dice/status"), settings));
        settings["Features:LiarsDiceEnabled"] = "false";
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(durable, settings));
        Assert.Null(PackagedGameWorkerForwarding.MatchGame(starts, settings));
    }

    private static DefaultHttpContext Route(string path, string pattern, string method = "GET", params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern),
            0, new EndpointMetadataCollection(metadata), "Game route"));
        return context;
    }
}
