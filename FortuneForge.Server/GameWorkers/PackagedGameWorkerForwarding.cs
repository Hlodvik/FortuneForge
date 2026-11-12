using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace FortuneForge.Server.GameWorkers;

internal static class PackagedGameWorkerForwarding
{
    private static readonly PackagedGame[] Games =
    [
        new("andar-bahar", "Features:AndarBaharEnabled"),
        new("craps", "Features:CrapsEnabled"),
        new("dragon-tiger", "Features:DragonTigerEnabled"),
        new("hi-lo", "Features:HiLoEnabled"),
        new("mines", "Features:MinesEnabled", "Features:MinesPaidEnabled"),
    ];

    internal static WebApplication UsePackagedGameWorkerForwarding(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var game = MatchGame(context);
            if (game is null || !game.IsEnabled(app.Configuration) || IsPracticeRequest(context.Request))
            {
                await next(context);
                return;
            }

            var workers = context.RequestServices.GetRequiredService<GameWorkerClient>();
            if (!workers.IsConfigured(game.Id))
            {
                await next(context);
                return;
            }

            var accounts = context.RequestServices.GetRequiredService<AccountService>();
            var account = (await accounts.GetProfileAsync(AccountSessionCookie.Read(context.Request),
                context.RequestAborted)).Value;
            if (account is null)
            {
                // Keep the existing endpoint's authentication response for this game.
                await next(context);
                return;
            }

            await ForwardAsync(context, workers, game, account.UserId);
        });
        return app;
    }

    private static PackagedGame? MatchGame(HttpContext context)
    {
        var request = context.Request;
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsPost(request.Method)) ||
            context.GetEndpoint() is not RouteEndpoint endpoint || endpoint.RoutePattern.RawText is not { } pattern)
            return null;

        var route = pattern.TrimStart('/');
        return Games.FirstOrDefault(game =>
            request.Path.StartsWithSegments(game.Path, StringComparison.OrdinalIgnoreCase) &&
            route.StartsWith(game.Path[1..] + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPracticeRequest(HttpRequest request) =>
        request.Headers["X-FortuneForge-Practice"].Any(value =>
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    private static async Task ForwardAsync(HttpContext context, GameWorkerClient workers, PackagedGame game, string userId)
    {
        var request = context.Request;
        var isWrite = HttpMethods.IsPost(request.Method);
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
            using var upstream = await workers.SendAsync(game.Id, userId, isWrite ? HttpMethod.Post : HttpMethod.Get,
                target, body, isWrite ? request.ContentType : null, headers, context.RequestAborted);
            await WriteResponseAsync(context, game, upstream);
        }
        catch (GameWorkerUnavailableException) when (!context.Response.HasStarted)
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            // An ambiguous worker failure can follow a committed wager. Do not submit it to another engine.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
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
        if (upstream.Headers.TryGetValues("Vary", out var vary))
            response.Headers.Vary = new StringValues(vary.ToArray());
        if (upstream.Headers.Location is { IsAbsoluteUri: false } location &&
            location.OriginalString.StartsWith(game.Path + "/", StringComparison.Ordinal))
            response.Headers.Location = location.OriginalString;

        await upstream.Content.CopyToAsync(response.Body, context.RequestAborted);
    }

    private sealed record PackagedGame(string Id, string Feature, string? PaidFeature = null)
    {
        internal string Path => "/api/games/" + Id;

        internal bool IsEnabled(IConfiguration configuration) =>
            configuration.GetValue(Feature, false) &&
            (PaidFeature is null || configuration.GetValue(PaidFeature, false));
    }
}
