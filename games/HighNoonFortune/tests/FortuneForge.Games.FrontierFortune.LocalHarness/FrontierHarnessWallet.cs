using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.FrontierFortune.Server;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.FrontierFortune.LocalHarness;

/// <summary>Test infrastructure only: durable test credits and neutral wallet receipts; no game rules or outcomes.</summary>
public sealed class FrontierHarnessWallet(FirestoreDb database) : IGameWalletClient, IGameWalletAccountReader
{
    public const long InitialBalanceCents = 100_000;
    private DocumentReference Balance(string account) => database.Collection("frontierValidationWallets").Document(FrontierRoundIdentity.Key(account, "balance"));

    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    {
        var balance = await database.RunTransactionAsync(async tx =>
        {
            var document = await tx.GetSnapshotAsync(Balance(accountId), cancellationToken);
            if (document.Exists)
            {
                if (document.GetValue<string>("accountId") != accountId) throw new GameWalletUnavailableException();
                return document.GetValue<long>("balanceCents");
            }
            tx.Create(document.Reference, new Dictionary<string, object> { ["accountId"] = accountId, ["balanceCents"] = InitialBalanceCents });
            return InitialBalanceCents;
        }, cancellationToken: cancellationToken);
        return new(0, balance);
    }

    public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(request.OperationId, "N", out var operation) || operation == Guid.Empty ||
            request.Phase != GameWalletSettlementPhase.Direct || request.WagerCents < 0 || request.PayoutCents < 0 ||
            request.SpendLegacyFreeGame || request.ExpectedLegacyFreeGamesRemaining != 0)
            throw new ArgumentException("The validation wallet accepts only the production Frontier direct-settlement contract.");
        var id = GameWalletSettlementIdentity.CreateId(FrontierFortuneEngine.GameId, accountId, request.OperationId);
        var receiptRef = database.Collection("frontierValidationWalletReceipts").Document(id);
        var digest = FrontierRoundIdentity.Hash(request);
        return database.RunTransactionAsync(async tx =>
        {
            var documents = await tx.GetAllSnapshotsAsync([receiptRef, Balance(accountId)], cancellationToken);
            if (documents[0].Exists)
            {
                if (documents[0].GetValue<string>("requestSha256") != digest || documents[0].GetValue<string>("accountId") != accountId)
                    throw new GameWalletConflictException();
                var original = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(documents[0].GetValue<string>("receiptJson"), FrontierRules.Json)
                    ?? throw new GameWalletUnavailableException();
                return original with { WasAlreadySettled = true };
            }
            if (documents[1].Exists && documents[1].GetValue<string>("accountId") != accountId) throw new GameWalletUnavailableException();
            var before = documents[1].Exists ? documents[1].GetValue<long>("balanceCents") : InitialBalanceCents;
            var declined = before < GameWalletBalanceRequirement.RequiredCents(request.WagerCents, request.MinimumBalanceBeforeCents);
            var afterWager = declined ? before : checked(before - request.WagerCents);
            var after = declined ? before : checked(afterWager + request.PayoutCents);
            var receipt = new GameWalletSettlementReceipt(id,
                !declined && request.WagerCents > 0 ? id + "-wager" : null,
                !declined && request.PayoutCents > 0 ? id + "-payout" : null,
                before, afterWager, after, DateTimeOffset.UtcNow, false, declined, LegacyFreeGamesRemaining: 0);
            if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, FrontierFortuneEngine.GameId, accountId, request))
                throw new GameWalletUnavailableException();
            tx.Create(receiptRef, new Dictionary<string, object> { ["accountId"] = accountId, ["requestSha256"] = digest,
                ["receiptJson"] = JsonSerializer.Serialize(receipt, FrontierRules.Json), ["wagerCents"] = request.WagerCents,
                ["payoutCents"] = request.PayoutCents, ["testCreditsOnly"] = true });
            tx.Set(documents[1].Reference, new Dictionary<string, object> { ["accountId"] = accountId, ["balanceCents"] = after });
            return receipt;
        }, cancellationToken: cancellationToken);
    }
}
