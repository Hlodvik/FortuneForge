using System.Text;
using System.Text.Json;

namespace FortuneForge.Server.Activity;

internal sealed class GameActivityMiddleware(RequestDelegate next, ILogger<GameActivityMiddleware> logger,
    IPlayerGameExperienceStore? experience = null)
{
    public async Task InvokeAsync(HttpContext context, IGameActivityAccountResolver accounts, GameActivityRecorder recorder)
    {
        var path = context.Request.Path.Value ?? "";
        var recentGame = AcceptedGameActivityReader.GameFor(context.Request.Method, path);
        var participationGame = experience is null ? null : AcceptedGameParticipation.GameFor(context.Request.Method, path);
        var game = recentGame ?? participationGame;
        if (game is null) { await next(context); return; }

        var requestKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (requestKey is null && context.Request.HasJsonContentType())
        {
            context.Request.EnableBuffering();
            try
            {
                using var request = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                foreach (var key in new[] { "requestId", "operationId" })
                    if (request.RootElement.ValueKind == JsonValueKind.Object && request.RootElement.TryGetProperty(key, out var id) && id.ValueKind == JsonValueKind.String)
                        requestKey = id.GetString();
            }
            catch (JsonException) { /* The game endpoint owns request validation. */ }
            finally { context.Request.Body.Position = 0; }
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
            AcceptedGameActivity? activity = null;
            AcceptedGameActivity? participation = null;
            if (context.Response.StatusCode is >= 200 and < 300 && buffer.Length is > 0 and < 2_000_000)
            {
                try
                {
                    var json = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
                    if (recentGame is not null) activity = AcceptedGameActivityReader.Read(recentGame, json, requestKey);
                    if (participationGame is not null) participation = AcceptedGameParticipation.Read(participationGame, path, json, requestKey);
                }
                catch (JsonException exception)
                {
                    logger.LogWarning(exception, "Could not read accepted catalogue activity for {GameId}", game);
                }
            }
            // Send the game result before touching analytics, then complete the
            // original response feature so HTTP/2 also sends END_STREAM promptly.
            if (!context.Response.HasStarted) context.Response.ContentLength ??= buffer.Length;
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            await originalBody.FlushAsync(context.RequestAborted);
            if (activity is not null || participation is not null)
            {
                context.Response.Body = originalBody;
                await context.Response.CompleteAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    var userId = await accounts.UserIdAsync(context, timeout.Token);
                    if (userId is not null)
                    {
                        if (participation is not null && experience is not null)
                        {
                            try { await experience.RecordAcceptedAsync(userId, participation, timeout.Token); }
                            catch (Exception error) { logger.LogWarning(error, "Could not record accepted player experience for {GameId}", game); }
                        }
                        if (activity is not null) await recorder.RecordAsync(userId, activity, timeout.Token);
                    }
                }
                catch (Exception exception)
                {
                    // Never replace a successful paid result or invite a wager
                    // retry because auxiliary activity storage is unavailable.
                    logger.LogWarning(exception, "Could not record accepted catalogue activity for {GameId}", game);
                }
            }
        }
        finally
        {
            if (!ReferenceEquals(context.Response.Body, originalBody)) context.Response.Body = originalBody;
        }
    }
}

