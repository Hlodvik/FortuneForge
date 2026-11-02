using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Activity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.SecurityAudit;

internal sealed class GameSecurityRequestMiddleware(RequestDelegate next, ILogger<GameSecurityRequestMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IGameActivityAccountResolver accounts,
        IGameSecurityRequestStore store, IGameRequestGeoResolver geoResolver,
        IOptions<GameSecurityRequestOptions> configuration, TimeProvider clock)
    {
        var options = configuration.Value;
        var route = GameRequestPayloadObservation.Route(context.Request.Method, context.Request.Path.Value ?? "");
        if (!options.Enabled || route is null) { await next(context); return; }
        var received = clock.GetUtcNow().UtcDateTime;
        var started = Stopwatch.GetTimestamp();
        var userId = await accounts.UserIdAsync(context, context.RequestAborted);
        if (userId is null && !route.IsPublicDemo) { await next(context); return; }
        userId ??= "public-demo";
        var requestId = Guid.NewGuid().ToString("N");
        // Forwarded headers never establish their own trust. Deployment must explicitly enumerate actual ingress peers.
        var ip = GameRequestIpObservation.Read(context.Connection.RemoteIpAddress,
            context.Request.Headers["X-Forwarded-For"].ToString(), options);
        var payload = await ReadPayloadAsync(context, options);
        var token = AccountSessionCookie.Read(context.Request);
        var session = string.IsNullOrWhiteSpace(token) ? null :
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("game-session-v1\n" + token)));
        GameGeoObservation? geo = null;
        if (ip.Address is not null && ip.OriginAllowed)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.StorageTimeoutSeconds));
            try { geo = ValidateGeo(await geoResolver.ObserveAsync(ip.Address, received, timeout.Token)); }
            catch (Exception exception) { logger.LogWarning("Game geolocation observation unavailable ({ExceptionType}).", exception.GetType().Name); }
        }
        var gameId = route.GameId == "slots" ? payload.GameId ?? "slots" : route.GameId;
        var observation = new GameRequestObservation(requestId, userId, session, ip.Address, received,
            context.Request.Method, context.Request.Path.Value ?? "", gameId, route.InstanceId ?? payload.InstanceId,
            payload.CommandId, payload.StateVersion, payload.Json, payload.State, ip.Source, geo);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.StorageTimeoutSeconds)))
        {
            try { await store.CreateIntentAsync(observation, timeout.Token); }
            catch (Exception exception)
            {
                // No unjournaled authenticated game command is allowed to mutate state during a receipt outage.
                logger.LogError("Game request {RequestId} could not be durably recorded ({ExceptionType}); command was not processed.", requestId, exception.GetType().Name);
                await UnavailableAsync(context, "Game recording is temporarily unavailable. Please try again.");
                return;
            }
        }

        using var scope = GameSecurityContext.Push(observation);
        var returned = false;
        try
        {
            if (!ip.OriginAllowed) await UnavailableAsync(context, "The game connection could not be verified.");
            else await next(context);
            returned = true;
        }
        finally
        {
            var result = new GameRequestResult(requestId, clock.GetUtcNow().UtcDateTime,
                returned ? context.Response.StatusCode : null, returned ? "http-response" : "pipeline-exception",
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            // Separate write survives rejected/rolled-back game transactions and client disconnects.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.StorageTimeoutSeconds));
            try { await store.CreateResultAsync(result, timeout.Token); }
            catch (Exception exception)
            {
                // The durable intent remains unresolved. Never invite replay of an already committed wager by replacing its response.
                logger.LogError("Game request {RequestId} has a durable intent but no durable HTTP result ({ExceptionType}); reconciliation is required.", requestId, exception.GetType().Name);
            }
        }
    }

    private static async Task<GameRequestPayload> ReadPayloadAsync(HttpContext context, GameSecurityRequestOptions options)
    {
        var command = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (!context.Request.HasJsonContentType()) return new(null, "non-json-omitted", GameRequestPayloadObservation.SafeId(command), null, null);
        context.Request.EnableBuffering();
        try
        {
            var bytes = new byte[options.MaximumBodyBytes + 1];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = await context.Request.Body.ReadAsync(bytes.AsMemory(read), context.RequestAborted);
                if (count == 0) break;
                read += count;
            }
            if (read > options.MaximumBodyBytes) return new(null, "oversize-omitted", GameRequestPayloadObservation.SafeId(command), null, null);
            return GameRequestPayloadObservation.Parse(bytes.AsMemory(0, read), command);
        }
        finally { context.Request.Body.Position = 0; }
    }

    private static Task UnavailableAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "5";
        return context.Response.WriteAsJsonAsync(new { error = "game-recording-unavailable", message });
    }

    internal static GameGeoObservation? ValidateGeo(GameGeoObservation? geo)
    {
        if (geo is null) return null;
        bool Text(string? value) => value is null || value.Length <= 128;
        if (geo.ObservedAtUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(geo.Provider) || string.IsNullOrWhiteSpace(geo.Version) ||
            !Text(geo.Provider) || !Text(geo.Version) || !Text(geo.CountryCode) || !Text(geo.RegionCode) || !Text(geo.City) || !Text(geo.AutonomousSystem) || !Text(geo.NetworkId) ||
            geo.Latitude is { } lat && (!double.IsFinite(lat) || lat < -90 || lat > 90) ||
            geo.Longitude is { } lon && (!double.IsFinite(lon) || lon < -180 || lon > 180) ||
            geo.AccuracyRadiusKm is { } accuracy && (!double.IsFinite(accuracy) || accuracy < 0) ||
            geo.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)) return null;
        return geo;
    }
}

public static class GameSecurityRequestConfiguration
{
    public static IServiceCollection AddGameSecurityRequests(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GameSecurityRequestOptions>().Bind(configuration.GetSection("GameSecurityRequests"))
            .Validate(GameRequestIpObservation.ValidOptions, "Game request IP mode, trusted ingress networks, or bounded storage limits are invalid.")
            .Validate(ConfiguredGameRequestGeoResolver.ValidOptions, "The optional game request geolocation dataset must be a valid bounded local JSON file.")
            .ValidateOnStart();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IGameSecurityRequestStore, FirestoreGameSecurityRequestStore>();
        services.TryAddSingleton<IGameRequestGeoResolver, ConfiguredGameRequestGeoResolver>();
        return services;
    }

    // Call after rate limiting and before any middleware that rewrites Connection.RemoteIpAddress.
    public static IApplicationBuilder UseGameSecurityRequests(this IApplicationBuilder app) => app.UseMiddleware<GameSecurityRequestMiddleware>();
}
