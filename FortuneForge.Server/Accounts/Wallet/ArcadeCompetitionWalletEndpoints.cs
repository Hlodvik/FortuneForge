using System.Text.Json;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Wallet;

internal static partial class GameWalletSettlementEndpoints
{
    internal static void MapArcadeCompetitionWallet(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ArcadeCompetitionWalletProtocol.EntryRoutePattern, ChargeArcadeEntryAsync);
        endpoints.MapPost(ArcadeCompetitionWalletProtocol.EntryProofRoutePattern, ProveArcadeEntryAsync);
        endpoints.MapPost(ArcadeCompetitionWalletProtocol.PayoutRoutePattern, PayArcadePrizeAsync);
    }

    private static async Task<IResult> ChargeArcadeEntryAsync(HttpContext context, string gameId, IConfiguration configuration,
        IHostEnvironment environment, IWalletRoundSettlementStore wallet, CancellationToken token)
    {
        var (caller, failure) = await AuthenticateArcadeAsync(context, gameId, ArcadeCompetitionWalletProtocol.EntryRoute(gameId),
            configuration, environment, token);
        if (caller is null) return failure!;
        var charge = await ReadArcadeCommandAsync<ArcadeCompetitionEntryCharge>(context, token);
        if (!ArcadeCompetitionWalletProtocol.IsEntryCharge(gameId, caller.AccountId, charge)) return InvalidSettlement();

        var entry = charge!.Entry;
        try
        {
            var receipt = await wallet.SettleAsync(new(gameId, caller.AccountId, entry.AttemptDocumentId, charge.OutcomeSha256,
                entry.EntryFeeCents, 0, RequestBodySha256: caller.BodySha256), caller.Clock.GetUtcNow(), token,
                WalletRoundLedgerLayout.ArcadeEntry(entry.AttemptDocumentId, entry.AttemptId));
            return ArcadeWalletResponse(context, caller, receipt.ToWireReceipt());
        }
        catch (WalletRoundSettlementConflictException)
        {
            return Results.Conflict(new { code = "wallet-settlement-conflict" });
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            return InvalidSettlement();
        }
    }

    private static async Task<IResult> ProveArcadeEntryAsync(HttpContext context, string gameId, IConfiguration configuration,
        IHostEnvironment environment, FirestoreDb database, CancellationToken token)
    {
        var (caller, failure) = await AuthenticateArcadeAsync(context, gameId, ArcadeCompetitionWalletProtocol.EntryProofRoute(gameId),
            configuration, environment, token);
        if (caller is null) return failure!;
        var entry = await ReadArcadeCommandAsync<ArcadeCompetitionEntryDebit>(context, token);
        if (!ArcadeCompetitionWalletProtocol.IsEntryDebit(gameId, caller.AccountId, entry)) return InvalidSettlement();

        var verified = await database.RunTransactionAsync(async transaction =>
        {
            var attempt = database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection).Document(entry!.AttemptDocumentId);
            var ledger = FirestoreWalletDocuments.Transaction(database, entry.TransactionId);
            var snapshots = await transaction.GetAllSnapshotsAsync([attempt, ledger], token);
            if (!snapshots[0].Exists || !snapshots[1].Exists) return false;
            var stored = FirestoreArcadeCompetitionStore.ReadAttempt(snapshots[0]);
            return ArcadeCompetitionEntryDebit.From(stored) == entry &&
                snapshots[1].TryGetValue<string>("transactionId", out var id) && id == entry.TransactionId &&
                snapshots[1].TryGetValue<string>("userId", out var account) && account == caller.AccountId &&
                snapshots[1].TryGetValue<string>("currencyId", out var currency) && currency == FirestoreWalletDocuments.SlotsCreditsCurrencyId &&
                snapshots[1].TryGetValue<string>("type", out var type) && type == "arcade-competition-entry" &&
                snapshots[1].TryGetValue<string>("idempotencyKey", out var operation) && operation == entry.AttemptId &&
                snapshots[1].TryGetValue<double>("amount", out var amount) && amount == (double)RandMoney.CentsToRand(-entry.EntryFeeCents);
        }, cancellationToken: token);
        return ArcadeWalletResponse(context, caller, new ArcadeCompetitionEntryProof(verified));
    }

    private static async Task<IResult> PayArcadePrizeAsync(HttpContext context, string gameId, IConfiguration configuration,
        IHostEnvironment environment, IArcadeCompetitionStore competitions, IArcadeCompetitionPayoutCreditor creditor,
        CancellationToken token)
    {
        var (caller, failure) = await AuthenticateArcadeAsync(context, gameId, ArcadeCompetitionWalletProtocol.PayoutRoute(gameId),
            configuration, environment, token);
        if (caller is null) return failure!;
        var charge = await ReadArcadeCommandAsync<ArcadeCompetitionPayoutCharge>(context, token);
        if (!ArcadeCompetitionWalletProtocol.IsPayoutCharge(gameId, caller.AccountId, charge)) return InvalidSettlement();

        var (competition, instruction, creditedAtUtc) = charge!;
        var state = await competitions.ReadSettlementStateAsync(competition, token);
        if (state.PayoutPlan is not { } plan || !plan.Instructions.Contains(instruction)) return InvalidSettlement();
        var receipt = await creditor.CreditAsync(competition, instruction, creditedAtUtc, token);
        return ArcadeWalletResponse(context, caller, receipt);
    }

    private static Task<(WalletCaller? Caller, IResult? Failure)> AuthenticateArcadeAsync(HttpContext context, string gameId,
        string route, IConfiguration configuration, IHostEnvironment environment, CancellationToken token) =>
        gameId is "asteroids" or "flappy" or "horse-flight" &&
        configuration.GetValue($"GameWallet:Games:{gameId}:AllowArcadeCompetition", false)
            ? AuthenticateAsync(context, gameId, route, configuration, environment, token)
            : Task.FromResult<(WalletCaller?, IResult?)>((null, Results.NotFound()));

    private static async Task<T?> ReadArcadeCommandAsync<T>(HttpContext context, CancellationToken token) where T : class
    {
        if (!context.Request.HasJsonContentType()) return null;
        try { return await context.Request.ReadFromJsonAsync<T>(GameWalletAuthentication.WireJson, token); }
        catch (Exception error) when (error is JsonException or ArgumentException) { return null; }
    }

    private static IResult ArcadeWalletResponse<T>(HttpContext context, WalletCaller caller, T result)
    {
        context.Response.Headers[GameWalletAuthentication.RequestSha256Header] = caller.BodySha256;
        return Results.Json(result, GameWalletAuthentication.WireJson);
    }
}
