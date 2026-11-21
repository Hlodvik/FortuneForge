using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Server.Bots;

internal static class GameManagedPlayerRosterEndpoints
{
    private static readonly TimeSpan RequestClockTolerance = TimeSpan.FromMinutes(2);

    internal static void MapGameManagedPlayerRoster(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(GameManagedPlayerRosterProtocol.RoutePattern, ApplyAsync).ExcludeFromDescription();

    private static async Task<IResult> ApplyAsync(HttpContext context, string gameId, string action,
        IConfiguration configuration, IManagedPlayerQueuer queuer, CancellationToken cancellationToken)
    {
        if (!GameManagedPlayerRosterProtocol.IsValidGameId(gameId) || !ManagedPlayerGames.All.Contains(gameId, StringComparer.Ordinal) ||
            action is not (GameManagedPlayerRosterProtocol.ReserveAction or GameManagedPlayerRosterProtocol.HeartbeatAction or
                GameManagedPlayerRosterProtocol.TryRenewAction or GameManagedPlayerRosterProtocol.ReleaseAction))
            return Results.NotFound();

        var settings = configuration.GetSection($"ManagedPlayerRoster:Games:{gameId}");
        if (!settings.GetValue<bool>("Enabled") || string.IsNullOrWhiteSpace(settings["SigningKey"]))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        byte[] key;
        try { key = GameWorkerAuthentication.ReadSigningKey(settings["SigningKey"]!); }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var proof = await GameManagedPlayerRosterProtocol.VerifyRequestAsync(context.Request, gameId, key, now, cancellationToken);
        if (proof is not { } verified || !GameManagedPlayerRosterProtocol.IsValidAssignment(verified.AccountId))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!context.Request.HasJsonContentType()) return Results.BadRequest();

        try
        {
            if (action == GameManagedPlayerRosterProtocol.ReserveAction)
            {
                var request = await JsonSerializer.DeserializeAsync<ManagedPlayerReserveRequest>(context.Request.Body,
                    GameManagedPlayerRosterProtocol.WireJson, cancellationToken);
                if (request is null || !Matches(request.AssignmentId, request.NowUtc, verified.AccountId, now.UtcDateTime) ||
                    request.Count is < 1 or > GameManagedPlayerRosterProtocol.MaximumCount ||
                    !GameManagedPlayerRosterProtocol.AreValidProfileIds(request.ExcludedProfileIds, GameManagedPlayerRosterProtocol.MaximumExclusions))
                    return Results.BadRequest();

                var players = await queuer.ReserveAsync(gameId, request.AssignmentId, request.Count, request.ExcludedProfileIds,
                    now.UtcDateTime, cancellationToken);
                context.Response.Headers[GameManagedPlayerRosterProtocol.RequestSha256Header] = verified.BodySha256;
                return Results.Json(players.Select(player => new ManagedPlayerRosterProfile(player.UserId, player.PlayerName, player.SkillLevel)).ToArray(),
                    GameManagedPlayerRosterProtocol.WireJson);
            }

            var lease = await JsonSerializer.DeserializeAsync<ManagedPlayerLeaseRequest>(context.Request.Body,
                GameManagedPlayerRosterProtocol.WireJson, cancellationToken);
            if (lease is null || !Matches(lease.AssignmentId, lease.NowUtc, verified.AccountId, now.UtcDateTime) ||
                !GameManagedPlayerRosterProtocol.AreValidProfileIds(lease.ProfileIds, GameManagedPlayerRosterProtocol.MaximumCount))
                return Results.BadRequest();

            IResult result;
            switch (action)
            {
                case GameManagedPlayerRosterProtocol.TryRenewAction:
                    var renewed = await queuer.TryRenewAsync(gameId, lease.AssignmentId, lease.ProfileIds, now.UtcDateTime, cancellationToken);
                    result = Results.Json(renewed, GameManagedPlayerRosterProtocol.WireJson);
                    break;
                case GameManagedPlayerRosterProtocol.HeartbeatAction:
                    await queuer.HeartbeatAsync(gameId, lease.AssignmentId, lease.ProfileIds, now.UtcDateTime, cancellationToken);
                    result = Results.NoContent();
                    break;
                default:
                    await queuer.ReleaseForGameAsync(gameId, lease.AssignmentId, lease.ProfileIds, now.UtcDateTime, cancellationToken);
                    result = Results.NoContent();
                    break;
            }
            context.Response.Headers[GameManagedPlayerRosterProtocol.RequestSha256Header] = verified.BodySha256;
            return result;
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (NotSupportedException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    }

    private static bool Matches(string assignmentId, DateTime requestedUtc, string assertedAssignment, DateTime nowUtc) =>
        assignmentId == assertedAssignment && requestedUtc.Kind == DateTimeKind.Utc &&
        (requestedUtc - nowUtc).Duration() <= RequestClockTolerance;
}
