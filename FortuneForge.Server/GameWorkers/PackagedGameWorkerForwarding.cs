using FortuneForge.Games.Abstractions;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.HorseFlight;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Games.TwentyFortyEight;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Arcade.HorseFlight;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace FortuneForge.Server.GameWorkers;

internal static class PackagedGameWorkerForwarding
{
    private const string GameRoutePrefix = "api/games/";
    private const string CardRoutePrefix = "api/cards/";
    private const string ArcadeCompetitionRoutePrefix = ArcadeCompetitionController.RoutePrefix + "/";
    private const string SolitairePath = "/api/solitaire";
    private const string FreeSolitairePath = SolitairePath + "/free";
    private const string PracticeHeader = "X-FortuneForge-Practice";
    private static readonly PackagedGame[] Games =
    [
        new("andar-bahar", Feature: null, AllowLocalFallback: false),
        new("craps", Feature: null, AllowLocalFallback: false),
        new("dragon-tiger", Feature: null, AllowLocalFallback: false),
        new("hi-lo", Feature: null, AllowLocalFallback: false),
        new("mines", Feature: null, AllowLocalFallback: false),
    ];

    internal static WebApplication UsePackagedGameWorkerForwarding(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var game = MatchGame(context, app.Configuration);
            if (game is null)
            {
                await next(context);
                return;
            }

            // An invalid practice marker must never submit a paid request to a worker.
            if (game.Feature is null && context.Request.Headers.ContainsKey(PracticeHeader))
            {
                await Results.Json(new { code = "invalid-practice-header", message = "Send one practice header with the value true." },
                    statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
                return;
            }

            var workers = context.RequestServices.GetRequiredService<GameWorkerClient>();
            if (!workers.IsConfigured(game.Id) && game.AllowLocalFallback)
            {
                await next(context);
                return;
            }

            var accounts = context.RequestServices.GetRequiredService<AccountService>();
            var account = (await accounts.GetProfileAsync(AccountSessionCookie.Read(context.Request),
                context.RequestAborted)).Value;
            if (account is null)
            {
                if (game.AllowLocalFallback)
                {
                    await next(context);
                    return;
                }

                var unauthorized = game.Id switch
                {
                    TwentyFortyEightSession.GameId => Results.Json(TwentyFortyEightErrors.AuthenticationRequired(), statusCode: 401),
                    DropMergeSession.GameId => Results.Json(DropMergeErrors.AuthenticationRequired(), statusCode: 401),
                    _ => Results.Unauthorized()
                };
                await unauthorized.ExecuteAsync(context);
                return;
            }

            if (!GameAccountBinding.Matches(context.Request, account.UserId))
            {
                await Results.Json(GameAccountBinding.SessionChanged,
                    statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                return;
            }

            await ForwardAsync(context, workers, game, account.UserId);
        });
        return app;
    }

    internal static PackagedGame? MatchGame(HttpContext context, IConfiguration configuration)
    {
        var request = context.Request;
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsPost(request.Method) ||
            HttpMethods.IsDelete(request.Method)) ||
            context.GetEndpoint() is not RouteEndpoint endpoint || endpoint.RoutePattern.RawText is not { } pattern)
            return null;

        var route = pattern.TrimStart('/');
        var routeGame = ReadGameSegment(route, GameRoutePrefix, requireSubroute: true);
        var legacyGame = Games.FirstOrDefault(candidate => candidate.Id == routeGame);
        if (legacyGame is { Feature: null })
        {
            // The worker owns new-wager admission. Its recovery paths remain routable while admission is paused.
            return request.Path.StartsWithSegments(legacyGame.Path, StringComparison.OrdinalIgnoreCase) &&
                !IsExactPracticeRequest(request) ? legacyGame : null;
        }
        if (IsPracticeRequest(request)) return null;

        if (routeGame == LiarsDiceSession.GameId)
        {
            var dice = OptIn(LiarsDiceSession.GameId);
            if (!configuration.GetValue("Features:LiarsDiceEnabled", false) ||
                !request.Path.StartsWithSegments(dice.Path, StringComparison.OrdinalIgnoreCase))
                return null;
            if (IsCollectionPost(request, route, dice.Path + "/matches")) return dice;
            if (endpoint.RoutePattern.Parameters.Any(parameter => parameter.Name == "matchId"))
                return Guid.TryParse(request.RouteValues["matchId"]?.ToString(), out var matchId) &&
                    PuzzleSessionIdentity.IsDurableId(matchId) ? dice : null;
            return dice.IsEnabled(configuration) ? dice : null;
        }
        if (route.StartsWith(ArcadeCompetitionRoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Standings are public aggregate reads. Worker-owned runs always require an authenticated account.
            if (HttpMethods.IsGet(request.Method) && string.Equals(route,
                ArcadeCompetitionController.PublicStandingsRoutePattern, StringComparison.OrdinalIgnoreCase))
                return null;
            var competition = MatchLegacyRoute(route, request.Path);
            if (competition?.Id == HorseFlightModule.GameId && !HorseFlightController.IsEnabled(configuration)) return null;
            return competition?.IsEnabled(configuration) == true ? competition : null;
        }

        if (routeGame == HorseFlightModule.GameId)
        {
            if (!HorseFlightController.IsEnabled(configuration)) return null;
            var free = new PackagedGame(routeGame, $"GameWorkers:{routeGame}:ForwardFreeRoutes", AllowLocalFallback: false);
            if (free.IsEnabled(configuration) && request.Path.StartsWithSegments(free.Path, StringComparison.OrdinalIgnoreCase))
                return free;
        }

        if (endpoint.Metadata.GetMetadata<GameWorkerPublicRoute>() is { } routeMetadata)
        {
            var selected = OptIn(routeMetadata.WorkerGameId, routeMetadata.PathPrefix);
            return request.Path.StartsWithSegments(selected.Path, StringComparison.OrdinalIgnoreCase) && selected.IsEnabled(configuration)
                ? selected : null;
        }

        if (MatchLegacyRoute(route, request.Path) is { } legacy)
            return request.Path.StartsWithSegments(legacy.Path, StringComparison.OrdinalIgnoreCase) && legacy.IsEnabled(configuration)
                ? legacy : null;
        if (routeGame is not { } gameId) return null;

        // Existing compute workers can serve the same game ID. Only forward
        // public routes after the selected worker supports them.
        var configuredGame = OptIn(gameId);
        var game = configuredGame.IsEnabled(configuration) ? configuredGame : legacyGame ?? configuredGame;
        if (!request.Path.StartsWithSegments(game.Path, StringComparison.OrdinalIgnoreCase)) return null;

        if (game.Id is TwentyFortyEightSession.GameId or DropMergeSession.GameId)
        {
            // Fresh sessions always belong to the durable worker. Existing v4 sessions remain process-local.
            if (IsCollectionPost(request, route, game.Path + "/games")) return game;
            if (endpoint.RoutePattern.Parameters.Any(parameter => parameter.Name == "gameId"))
                return Guid.TryParse(request.RouteValues["gameId"]?.ToString(), out var id) &&
                    PuzzleSessionIdentity.IsDurableId(id) ? game : null;
        }

        return game.IsEnabled(configuration) ? game : null;
    }

    private static bool IsCollectionPost(HttpRequest request, string route, string path) =>
        HttpMethods.IsPost(request.Method) &&
        string.Equals(route, path.TrimStart('/'), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(request.Path.Value?.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase);

    private static PackagedGame? MatchLegacyRoute(string pattern, PathString path)
    {
        if (ReadGameSegment(pattern, CardRoutePrefix) is { } cardGame &&
            cardGame is "blackjack" or "three-card-poker" or "texas-holdem")
            return OptIn(cardGame, "/" + CardRoutePrefix + cardGame);

        var routePath = new PathString("/" + pattern);
        if (routePath.StartsWithSegments(FreeSolitairePath, StringComparison.OrdinalIgnoreCase))
            return OptIn("free-solitaire", FreeSolitairePath);
        if (routePath.StartsWithSegments(SolitairePath, StringComparison.OrdinalIgnoreCase))
            return OptIn("solitaire", SolitairePath);

        if (pattern.StartsWith(ArcadeCompetitionRoutePrefix, StringComparison.OrdinalIgnoreCase) &&
            ReadGameSegment(path.Value?.TrimStart('/') ?? string.Empty, ArcadeCompetitionRoutePrefix) is { } arcadeGame &&
            arcadeGame is "asteroids" or "flappy" or "horse-flight")
        {
            var competitionPath = "/" + ArcadeCompetitionRoutePrefix + arcadeGame;
            var freePath = competitionPath + "/free";
            return path.StartsWithSegments(freePath, StringComparison.OrdinalIgnoreCase)
                ? new(arcadeGame, $"GameWorkers:{arcadeGame}:ForwardFreeRoutes", AllowLocalFallback: false, PathPrefix: freePath)
                : new(arcadeGame, $"GameWorkers:{arcadeGame}:ForwardPaidCompetitionRoutes", AllowLocalFallback: false, PathPrefix: competitionPath);
        }
        return null;
    }

    private static string? ReadGameSegment(string route, string prefix, bool requireSubroute = false)
    {
        if (!route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var end = route.IndexOf('/', prefix.Length);
        if (end < 0 && requireSubroute) return null;
        var gameId = route[prefix.Length..(end < 0 ? route.Length : end)];
        return gameId.Length is > 0 and <= 64 && gameId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? gameId.ToLowerInvariant() : null;
    }

    private static PackagedGame OptIn(string gameId, string? pathPrefix = null) =>
        new(gameId, $"GameWorkers:{gameId}:ForwardPublicRoutes", AllowLocalFallback: false, PathPrefix: pathPrefix);

    internal static bool IsPublicRouteForwardingEnabled(IConfiguration configuration, string gameId) =>
        OptIn(gameId).IsEnabled(configuration);

    private static bool IsPracticeRequest(HttpRequest request) =>
        request.Headers[PracticeHeader].Any(value =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    private static bool IsExactPracticeRequest(HttpRequest request) =>
        request.Headers.TryGetValue(PracticeHeader, out var values) && values.Count == 1 &&
        string.Equals(values[0], "true", StringComparison.OrdinalIgnoreCase);

    internal static async Task ForwardAsync(HttpContext context, GameWorkerClient workers, PackagedGame game, string userId)
    {
        var request = context.Request;
        var isWrite = !HttpMethods.IsGet(request.Method);
        var target = request.PathBase.ToUriComponent() + request.Path.ToUriComponent() +
            request.QueryString.ToUriComponent();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in GameWorkerAuthentication.AssertedHeaderNames)
            if (request.Headers.TryGetValue(name, out var value))
                headers.Add(name, value.ToString());

        var body = isWrite ? await ReadBodyAsync(request, context.RequestAborted) : Array.Empty<byte>();
        context.Response.Headers.CacheControl = "no-store, max-age=0";

        try
        {
            using var upstream = await workers.SendAsync(game.Id, userId, new HttpMethod(request.Method),
                target, body, isWrite ? request.ContentType : null, headers, context.RequestAborted, preserveResponse: true);
            await WriteResponseAsync(context, game, upstream);
        }
        catch (GameWorkerUnavailableException) when (!context.Response.HasStarted)
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            // An ambiguous worker failure can follow a committed wager. Do not submit it to another engine.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            if (game.Id == TwentyFortyEightSession.GameId)
            {
                await context.Response.WriteAsJsonAsync(TwentyFortyEightErrors.WorkerUnavailable(), context.RequestAborted);
                return;
            }
            if (game.Id == DropMergeSession.GameId)
            {
                await context.Response.WriteAsJsonAsync(DropMergeErrors.WorkerUnavailable(), context.RequestAborted);
                return;
            }

            await context.Response.WriteAsJsonAsync(new
            {
                code = "game-worker-unavailable",
                message = isWrite
                    ? "The game service could not confirm the action. Retry the same action with its original operation ID."
                    : "The game service is temporarily unavailable."
            }, context.RequestAborted);
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Body.CanSeek)
            request.Body.Position = 0;
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        return body.ToArray();
    }

    private static async Task WriteResponseAsync(HttpContext context, PackagedGame game, HttpResponseMessage upstream)
    {
        var contentType = upstream.Content.Headers.ContentType;
        if (contentType?.MediaType is { } mediaType &&
            !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
            throw new GameWorkerUnavailableException(game.Id);

        var response = context.Response;
        response.StatusCode = (int)upstream.StatusCode;
        response.ContentType = contentType?.ToString();
        if (upstream.Headers.RetryAfter is { } retryAfter)
            response.Headers.RetryAfter = retryAfter.ToString();
        if (upstream.Headers.ETag is { } entityTag)
            response.Headers.ETag = entityTag.ToString();
        if (upstream.Headers.TryGetValues("Vary", out var vary))
            response.Headers.Vary = new StringValues(vary.ToArray());
        if (upstream.Headers.Location is { IsAbsoluteUri: false } location &&
            location.OriginalString.StartsWith(game.Path + "/", StringComparison.Ordinal))
            response.Headers.Location = location.OriginalString;

        await upstream.Content.CopyToAsync(response.Body, context.RequestAborted);
    }

    internal sealed record PackagedGame(string Id, string? Feature, bool AllowLocalFallback = true,
        string? PathPrefix = null)
    {
        internal string Path => PathPrefix ?? "/api/games/" + Id;

        internal bool IsEnabled(IConfiguration configuration) => Feature is null || configuration.GetValue(Feature, false);
    }
}
