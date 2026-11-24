using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace FortuneForge.Games.FrontierFortune.Server;

public static class FrontierPaidRouteContract
{
    public const string SpinPath = "/api/slots/frontier-fortune/spins";
    public const string StatePath = "/api/slots/frontier-fortune/state";
    public const string BonusActionPath = "/api/slots/frontier-fortune/bonus-actions";
    public const string HandActionPath = "/api/slots/frontier-fortune/hand-actions";
    public const string LegacyStatePath = "/api/slots/frontier-fortune/legacy-v1/state";
    public const string LegacySpinPath = "/api/slots/frontier-fortune/legacy-v1/spins";
    public const string LegacyBonusActionPath = "/api/slots/frontier-fortune/legacy-v1/bonus-actions";
    public const string AuditGameId = "high-noon-fortune";
}
public sealed record FrontierSpinCommand(string OperationId, long WagerPoints);
public sealed record FrontierBonusCommand(string OperationId, string BonusId, string Kind, int? TargetIndex, long ExpectedRevision);
public sealed record FrontierHandCommand(string OperationId, long ExpectedRevision, string Kind, string? RoundId,
    bool[]? Held, int?[]? SelectedRows);

public static class FrontierPaidEndpoints
{
    public static void MapFrontierPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapGet(FrontierPaidRouteContract.StatePath, async (HttpContext context, FrontierRoundService service, CancellationToken token) =>
            await Run(context, resolveAccount, account => service.ReadOutlawAsync(account, token)))
            .WithMetadata(Route(FrontierPaidRouteContract.StatePath));
        endpoints.MapPost(FrontierPaidRouteContract.SpinPath,
            async (FrontierSpinCommand command, HttpContext context, FrontierRoundService service, IConfiguration config, CancellationToken token) =>
                await Run(context, resolveAccount, account => service.ExecuteAsync(account,
                    new(command.OperationId, "spin", command.WagerPoints) { MathVersion = OutlawRules.EngineVersion }, config.GetValue<bool>("FrontierFortune:AllowNewPaidRounds"), token)))
            .WithMetadata(Route(FrontierPaidRouteContract.SpinPath));
        endpoints.MapPost(FrontierPaidRouteContract.HandActionPath,
            async (FrontierHandCommand command, HttpContext context, FrontierRoundService service, CancellationToken token) =>
                await Run(context, resolveAccount, account => service.ExecuteAsync(account,
                    new(command.OperationId, command.Kind, ExpectedRevision: command.ExpectedRevision) { MathVersion = OutlawRules.EngineVersion,
                        RoundId = command.RoundId, Held = command.Held, SelectedRows = command.SelectedRows }, true, token)))
            .WithMetadata(Route(FrontierPaidRouteContract.HandActionPath));
        endpoints.MapGet(FrontierPaidRouteContract.LegacyStatePath, async (HttpContext context, FrontierRoundService service, CancellationToken token) =>
            await Run(context, resolveAccount, account => service.ReadAsync(account, token)))
            .WithMetadata(Route(FrontierPaidRouteContract.LegacyStatePath));
        endpoints.MapPost(FrontierPaidRouteContract.LegacySpinPath,
            async (FrontierSpinCommand command, HttpContext context, FrontierRoundService service, CancellationToken token) =>
                await Run(context, resolveAccount, account => service.ExecuteAsync(account, new(command.OperationId, "spin", command.WagerPoints), false, token)))
            .WithMetadata(Route(FrontierPaidRouteContract.LegacySpinPath));
        endpoints.MapPost(FrontierPaidRouteContract.LegacyBonusActionPath,
            async (FrontierBonusCommand command, HttpContext context, FrontierRoundService service, CancellationToken token) =>
                await Run(context, resolveAccount, account => service.ExecuteAsync(account,
                    new(command.OperationId, command.Kind, BonusId: command.BonusId, TargetIndex: command.TargetIndex,
                        ExpectedRevision: command.ExpectedRevision), true, token)))
            .WithMetadata(Route(FrontierPaidRouteContract.LegacyBonusActionPath));
    }
    private static GameWorkerPublicRoute Route(string path) => new(FrontierFortuneEngine.GameId, path, FrontierPaidRouteContract.AuditGameId);
    private static async Task<IResult> Run(HttpContext context, Func<HttpContext, string?> resolve, Func<string, Task<FrontierReply>> operation)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolve(context) is not { } account) return Results.Unauthorized();
        try
        {
            var reply = await operation(account);
            return reply.Accepted ? Results.Ok(reply) : Results.Conflict(reply);
        }
        catch (FrontierConflictException error) { return Results.Conflict(new { code = error.Code, error = error.Message }); }
        catch (ArgumentException error) { return Results.BadRequest(new { code = "frontier-invalid-command", error = error.Message }); }
        catch (GameWalletUnavailableException error) { return Results.Json(new { code = "retry-required", error = error.Message }, statusCode: 503); }
    }
}
