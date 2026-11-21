using System.Globalization;
using FortuneForge.Server.Arcade.Competition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FortuneForge.Games.Arcade.Competition;

public sealed record ArcadeCompetitionStandingsReply(int StatusCode, object Body);

public static class ArcadeCompetitionStandings
{
    public static RouteHandlerBuilder MapArcadeCompetitionStandings(this IEndpointRouteBuilder endpoints,
        string gameId, int allTimeMaximumPlaces = 100) =>
        endpoints.MapGet($"/api/arcade-competitions/{gameId}/{{period}}",
            async (string period, string? at, ArcadeCompetitionService service, CancellationToken token) =>
            {
                var reply = await ReadAsync(service, gameId, period, at, allTimeMaximumPlaces, token);
                return Results.Json(reply.Body, statusCode: reply.StatusCode);
            });

    public static async Task<ArcadeCompetitionStandingsReply> ReadAsync(ArcadeCompetitionService service,
        string gameId, string period, string? at, int allTimeMaximumPlaces, CancellationToken token)
    {
        if (period == "all-time")
        {
            if (at is not null) return BadRequest("arcade-competition-at-not-supported-for-all-time");
            var allTime = await service.GetAllTimeSnapshotAsync(gameId, allTimeMaximumPlaces, token);
            return new(StatusCodes.Status200OK, new { allTime.GameId, Period = "all-time", Leaderboard = allTime.Leaderboard });
        }
        if (!TryParsePeriod(period, out var windowKind)) return BadRequest("arcade-competition-period-invalid");
        var competition = service.ResolveCurrentCompetition(gameId, windowKind);
        if (at is not null)
        {
            if (!DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ||
                parsed.Offset != TimeSpan.Zero)
                return BadRequest("arcade-competition-at-invalid");
            competition = service.ResolveCompetitionAt(gameId, windowKind, parsed);
        }
        var snapshot = await service.GetSnapshotAsync(competition, token);
        return new(StatusCodes.Status200OK, new
        {
            competition.GameId, Period = period, competition.StartsAtUtc, competition.EndsAtUtc,
            EntriesOpen = snapshot.IsAcceptingAttempts, snapshot.SettlementState.IsCompleted, snapshot.TotalUniquePlayers,
            Leaderboard = snapshot.PrizePlacements, SolePlayerRefund = snapshot.Refunds.SingleOrDefault(),
            snapshot.VisibleJackpotCents,
        });
    }

    public static bool TryParsePeriod(string period, out ArcadeCompetitionWindowKind kind)
    {
        switch (period)
        {
            case "daily": kind = ArcadeCompetitionWindowKind.Daily; return true;
            case "weekly": kind = ArcadeCompetitionWindowKind.Weekly; return true;
            default: kind = default; return false;
        }
    }

    private static ArcadeCompetitionStandingsReply BadRequest(string code) =>
        new(StatusCodes.Status400BadRequest, new { Code = code });
}
