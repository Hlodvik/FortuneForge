using System.Net;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Activity;
using FortuneForge.Server.SecurityAudit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class RequestObservationTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("POST", "/api/cards/texas-holdem/credit/matches/hand-1/actions", "texas-holdem", "hand-1")]
    [InlineData("POST", "/api/arcade-competitions/asteroids/day/runs/run-1/replay", "asteroids", "run-1")]
    [InlineData("POST", "/api/solitaire/free/runs/run-2/replay", "solitaire", "run-2")]
    [InlineData("POST", "/api/slots/spins", "slots", null)]
    [InlineData("POST", "/api/slots/demo/spins", "slots", null)]
    [InlineData("POST", "/api/games/2048/games/run-3/move", "2048", "run-3")]
    public void Only_known_mutating_game_routes_are_observed(string method, string path, string game, string? instance)
    {
        var route = GameRequestPayloadObservation.Route(method, path);
        Assert.NotNull(route); Assert.Equal(game, route.GameId); Assert.Equal(instance, route.InstanceId);
        Assert.Null(GameRequestPayloadObservation.Route("POST", "/api/accounts/login"));
        Assert.Null(GameRequestPayloadObservation.Route("POST", "/api/payments/checkouts"));
        Assert.Null(GameRequestPayloadObservation.Route("POST", "/api/games/unknown/rounds"));
        Assert.Null(GameRequestPayloadObservation.Route("GET", path));
    }

    [Fact]
    public void Preserves_game_command_values_and_explicitly_marks_unrecognized_fields()
    {
        var payload = Parse("""{"type":"raise","raiseTo":450,"expectedVersion":12,"operationId":"cmd-123","matchId":"match-9","futureExtension":"omitted"}""");
        Assert.Equal("cmd-123", payload.CommandId); Assert.Equal(12L, payload.StateVersion); Assert.Equal("match-9", payload.InstanceId);
        Assert.Equal("sanitized-partial", payload.State);
        using var json = JsonDocument.Parse(payload.Json!);
        Assert.Equal(450, json.RootElement.GetProperty("raiseTo").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("futureExtension", out _));
        Assert.Null(Parse("""{"expectedVersion":"bad"}""").StateVersion);
    }

    [Fact]
    public void Sensitive_fields_cause_whole_body_omission_including_nested_and_unknown_fields()
    {
        foreach (var key in new[] { "password", "accessToken", "cookie", "Authorization", "clientSecret", "email" })
        {
            var payload = Parse("{\"type\":\"call\",\"unknown\":{\"" + key + "\":\"DO-NOT-STORE\"}}");
            Assert.Null(payload.Json); Assert.Equal("sensitive-body-omitted", payload.State);
        }
        Assert.Equal("invalid-json", Parse("{broken").State);
        Assert.Equal("non-object", Parse("[]").State);
    }

    [Fact]
    public void Default_ip_mode_ignores_spoofed_forwarding_and_trusted_chain_stops_at_first_untrusted_hop()
    {
        var direct = GameRequestIpObservation.Read(IPAddress.Parse("203.0.113.8"), "198.51.100.1", new());
        Assert.Equal("203.0.113.8", direct.Address); Assert.Equal("connection", direct.Source);
        var options = new GameSecurityRequestOptions { IpMode = "TrustedProxyChain", TrustedProxyNetworks = ["10.0.0.0/24"], RequireTrustedProxy = true };
        var chain = GameRequestIpObservation.Read(IPAddress.Parse("10.0.0.3"), "198.51.100.99, 203.0.113.7, 10.0.0.2", options);
        Assert.Equal("203.0.113.7", chain.Address); Assert.True(chain.OriginAllowed);
        var attacker = GameRequestIpObservation.Read(IPAddress.Parse("203.0.113.8"), "198.51.100.1", options);
        Assert.False(attacker.OriginAllowed); Assert.Equal("203.0.113.8", attacker.Address);
        Assert.False(GameRequestIpObservation.Read(IPAddress.Parse("10.0.0.3"), "invalid", options).OriginAllowed);
        Assert.True(GameRequestIpObservation.Contains("10.0.0.0/24", IPAddress.Parse("::ffff:10.0.0.7")));
        Assert.False(GameRequestIpObservation.Contains("10.0.0.0/24", IPAddress.Parse("10.0.1.7")));
        Assert.False(GameRequestIpObservation.ValidOptions(withNetworks([])));

        GameSecurityRequestOptions withNetworks(string[] networks) => new() { IpMode = "TrustedProxyChain", TrustedProxyNetworks = networks };
    }

    [Fact]
    public async Task Rejected_command_has_durable_intent_before_handler_and_separate_http_result()
    {
        var store = new Store();
        var context = Context("""{"type":"raise","raiseTo":100,"expectedVersion":3}""");
        var middleware = Middleware(async request =>
        {
            Assert.Single(store.Intents);
            Assert.Equal(store.Intents[0], GameSecurityContext.Current);
            Assert.Equal(0, request.Request.Body.Position);
            using var body = new StreamReader(request.Request.Body, leaveOpen: true);
            Assert.Contains("raiseTo", await body.ReadToEndAsync());
            request.Response.StatusCode = 409;
        });
        await Invoke(middleware, context, store);
        Assert.Equal(409, Assert.Single(store.Results).HttpStatusCode);
        Assert.Equal("http-response", store.Results[0].Outcome);
        Assert.Null(GameSecurityContext.Current);
        Assert.DoesNotContain("raw-session", JsonSerializer.Serialize(store.Intents));
        Assert.Equal("203.0.113.3", store.Intents[0].IpAddress);
        Assert.Equal(Now, store.Intents[0].ReceivedAtUtc);
    }

    [Fact]
    public async Task Intent_outage_prevents_mutation_but_result_outage_never_replaces_committed_response()
    {
        var called = false;
        var unavailable = Context("{}");
        await Invoke(Middleware(_ => { called = true; return Task.CompletedTask; }), unavailable, new Store { FailIntent = true });
        Assert.False(called); Assert.Equal(503, unavailable.Response.StatusCode);

        var committed = Context("{}"); var store = new Store { FailResult = true };
        await Invoke(Middleware(request => { called = true; request.Response.StatusCode = 201; return Task.CompletedTask; }), committed, store);
        Assert.True(called); Assert.Equal(201, committed.Response.StatusCode); Assert.Single(store.Intents); Assert.Empty(store.Results);
    }

    [Fact]
    public async Task Strict_ingress_rejection_is_recorded_without_running_game_handler()
    {
        var store = new Store(); var called = false;
        var context = Context("{}"); context.Request.Headers["X-Forwarded-For"] = "198.51.100.4";
        await Invoke(Middleware(_ => { called = true; return Task.CompletedTask; }), context, store,
            options: new() { IpMode = "TrustedProxyChain", TrustedProxyNetworks = ["10.0.0.0/24"], RequireTrustedProxy = true });
        Assert.False(called); Assert.Single(store.Intents); Assert.Equal("untrusted-forwarding-ignored", store.Intents[0].IpSource);
        Assert.Equal(503, Assert.Single(store.Results).HttpStatusCode);
    }

    [Fact]
    public async Task Ambient_request_context_restores_nested_scopes_and_isolates_concurrent_requests()
    {
        var first = new GameRequestObservation("first", "alice", null, null, Now, "POST", "/", "mines", null, null, null, null, "empty", "connection");
        using (GameSecurityContext.Push(first))
        {
            using (GameSecurityContext.Push(first with { RequestId = "nested" })) Assert.Equal("nested", GameSecurityContext.Current!.RequestId);
            Assert.Equal("first", GameSecurityContext.Current!.RequestId);
            await Task.WhenAll(new[] { "a", "b" }.Select(async id =>
            {
                using var scope = GameSecurityContext.Push(first with { RequestId = id });
                await Task.Yield(); Assert.Equal(id, GameSecurityContext.Current!.RequestId);
            }));
            Assert.Equal("first", GameSecurityContext.Current!.RequestId);
        }
        Assert.Null(GameSecurityContext.Current);
    }

    [Fact]
    public async Task Pipeline_exception_remains_exception_with_unknown_http_status_and_durable_receipt()
    {
        var store = new Store();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(Middleware(_ => throw new InvalidOperationException("transaction rolled back")), Context("{}"), store));
        Assert.Single(store.Intents); var result = Assert.Single(store.Results);
        Assert.Equal("pipeline-exception", result.Outcome); Assert.Null(result.HttpStatusCode); Assert.Null(GameSecurityContext.Current);
    }

    [Fact]
    public async Task Sessions_ips_and_geo_are_observed_per_command_and_secrets_do_not_enter_receipts()
    {
        var store = new Store(); var geo = new Geo();
        var first = Context("""{"type":"call"}""");
        await Invoke(Middleware(_ => Task.CompletedTask), first, store, geo);
        var second = Context("""{"password":"NEVER-LOG"}""", "second-session", "198.51.100.6");
        await Invoke(Middleware(_ => Task.CompletedTask), second, store, geo);
        Assert.NotEqual(store.Intents[0].SessionId, store.Intents[1].SessionId);
        Assert.NotEqual(store.Intents[0].IpAddress, store.Intents[1].IpAddress);
        Assert.Equal(2, geo.Calls); Assert.Equal("local-v1", store.Intents[1].Geo!.Version);
        Assert.Null(store.Intents[1].RequestJson); Assert.DoesNotContain("NEVER-LOG", JsonSerializer.Serialize(store.Intents));
    }

    [Fact]
    public async Task Oversized_body_is_omitted_and_endpoint_still_receives_original_input()
    {
        var json = "{\"type\":\"" + new string('x', 1500) + "\"}";
        var store = new Store(); var context = Context(json);
        await Invoke(Middleware(async request =>
        {
            using var reader = new StreamReader(request.Request.Body, leaveOpen: true);
            Assert.Equal(json, await reader.ReadToEndAsync());
        }), context, store, options: new() { MaximumBodyBytes = 1024 });
        Assert.Null(store.Intents[0].RequestJson); Assert.Equal("oversize-omitted", store.Intents[0].RequestBodyState);
    }

    [Fact]
    public async Task Unauthenticated_or_non_game_routes_are_not_logged()
    {
        var store = new Store(); var context = Context("{}");
        context.Request.Path = "/api/accounts/login";
        await Invoke(Middleware(_ => Task.CompletedTask), context, store);
        Assert.Empty(store.Intents);
        context.Request.Path = "/api/games/mines/rounds";
        await Middleware(_ => Task.CompletedTask).InvokeAsync(context, new Resolver(null), store, new Geo(), Options.Create(new GameSecurityRequestOptions()), new Clock());
        Assert.Empty(store.Intents);
    }

    [Fact]
    public async Task Public_demo_spin_has_guest_request_and_ip_receipt_before_engine_runs()
    {
        var store = new Store();
        var context = Context("""{"gameId":"wukong-journey-v1","wagerPoints":100,"useFreeSpin":false,"energyBalance":25}""");
        context.Request.Path = "/api/slots/demo/spins";
        var middleware = Middleware(request =>
        {
            Assert.Single(store.Intents);
            Assert.Equal("public-demo", GameSecurityContext.Current?.UserId);
            Assert.Equal("wukong-journey-to-the-west", GameSecurityContext.Current?.GameId);
            request.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, new Resolver(null), store, new Geo(),
            Options.Create(new GameSecurityRequestOptions()), new Clock());

        Assert.Equal("public-demo", Assert.Single(store.Intents).UserId);
        Assert.Equal("203.0.113.3", store.Intents[0].IpAddress);
        Assert.Equal("http-response", Assert.Single(store.Results).Outcome);
        Assert.Null(GameSecurityContext.Current);
    }

    [Fact]
    public async Task Local_dataset_uses_longest_prefix_and_records_estimate_metadata()
    {
        var path = Path.Combine(Path.GetTempPath(), "game-geo-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"provider":"operator-test","version":"2026-10","networks":[{"cidr":"203.0.0.0/16","countryCode":"US"},{"cidr":"203.0.113.0/24","countryCode":"CA","accuracyRadiusKm":50,"confidence":0.7,"autonomousSystem":"AS64500","isProxyOrVpn":true}]}""");
            var options = new GameSecurityRequestOptions { GeoDatasetPath = path };
            Assert.True(ConfiguredGameRequestGeoResolver.ValidOptions(options));
            var resolver = new ConfiguredGameRequestGeoResolver(Options.Create(options));
            var found = await resolver.ObserveAsync("203.0.113.5", Now, default);
            Assert.Equal("CA", found!.CountryCode); Assert.Equal(50, found.AccuracyRadiusKm); Assert.True(found.IsProxyOrVpn);
            Assert.Equal(Now, found.ObservedAtUtc); Assert.Equal("2026-10", found.Version);
            Assert.Null(await resolver.ObserveAsync("198.51.100.5", Now, default));
            await File.WriteAllTextAsync(path, """{"provider":"x","version":"v1","networks":[{"cidr":"bad","confidence":4}]}""");
            Assert.False(ConfiguredGameRequestGeoResolver.ValidOptions(options));
        }
        finally { File.Delete(path); }
    }

    private static GameRequestPayload Parse(string json) => GameRequestPayloadObservation.Parse(Encoding.UTF8.GetBytes(json), null);
    private static GameSecurityRequestMiddleware Middleware(RequestDelegate next) => new(next, NullLogger<GameSecurityRequestMiddleware>.Instance);
    private static Task Invoke(GameSecurityRequestMiddleware middleware, HttpContext context, Store store, Geo? geo = null, GameSecurityRequestOptions? options = null) =>
        middleware.InvokeAsync(context, new Resolver("user-1"), store, geo ?? new Geo(), Options.Create(options ?? new()), new Clock());
    private static DefaultHttpContext Context(string body, string session = "raw-session", string ip = "203.0.113.3")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST"; context.Request.Path = "/api/cards/texas-holdem/credit/matches/match-1/actions";
        context.Request.ContentType = "application/json"; context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Headers.Authorization = "Bearer " + session; context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();
        return context;
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class Resolver(string? userId) : IGameActivityAccountResolver { public Task<string?> UserIdAsync(HttpContext context, CancellationToken ct) => Task.FromResult(userId); }
    private sealed class Store : IGameSecurityRequestStore
    {
        internal List<GameRequestObservation> Intents { get; } = [];
        internal List<GameRequestResult> Results { get; } = [];
        internal bool FailIntent { get; init; }
        internal bool FailResult { get; init; }
        public Task CreateIntentAsync(GameRequestObservation observation, CancellationToken ct)
        {
            if (FailIntent) throw new IOException("unavailable");
            Intents.Add(observation); return Task.CompletedTask;
        }
        public Task CreateResultAsync(GameRequestResult result, CancellationToken ct)
        {
            if (FailResult) throw new IOException("unavailable");
            Results.Add(result); return Task.CompletedTask;
        }
    }
    private sealed class Geo : IGameRequestGeoResolver
    {
        internal int Calls;
        public Task<GameGeoObservation?> ObserveAsync(string ipAddress, DateTime receivedAtUtc, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<GameGeoObservation?>(new("test", "local-v1", receivedAtUtc, "US", null, null, null, null));
        }
    }
}
