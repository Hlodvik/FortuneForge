using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CrookedRoad;
using FortuneForge.Games.CrookedRoad.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.RoundAudit;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Accounts;

public sealed class CrookedRoadStandardWalletTests
{
    private sealed class LocalFirestoreFactAttribute : FactAttribute
    {
        public LocalFirestoreFactAttribute()
        {
            if (!HasLocalEmulator) Skip = "An explicit loopback Firestore emulator is required.";
        }
    }
    private static bool HasLocalEmulator => Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") is { } host &&
        Uri.TryCreate("http://" + host, UriKind.Absolute, out var uri) && uri.IsLoopback;
    private sealed class PayingDraws : IGameRandomSource { public int Next(int maximumExclusive) => 0; }

    // This adapter changes transport only: receipt IDs and ledger writes come from the real wallet store.
    private sealed class StandardWallet(FirestoreDb database) : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly FirestoreWalletRoundSettlementStore wallet = new(database);
        public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token) =>
            (await wallet.SettleAsync(WalletRoundSettlement.FromDirectRequest(CrookedRoadRules.GameId, accountId, request),
                DateTimeOffset.UtcNow, token)).ToWireReceipt();
        public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token) =>
            new(0, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, accountId, false, token));
    }

    [LocalFirestoreFact]
    public async Task NativePaidRoundFinalizesAgainstRealStandardWalletAndAuditRejectsLegacyLegIds()
    {
        if (!HasLocalEmulator) throw new InvalidOperationException("Live Firestore is forbidden in this test.");
        var database = new FirestoreDbBuilder { ProjectId = "crooked-real-wallet-" + Guid.NewGuid().ToString("N"),
            EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        const string account = "crooked-road-standard-wallet-player";
        await database.Collection("userBalances").Document(account + "_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = account, ["currencyId"] = "slotsCredits", ["available"] = 100L,
            ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L
        });
        var wallet = new StandardWallet(database);
        var random = new PayingDraws();
        var service = new CrookedRoadRoundService(new(database, random, TimeProvider.System),
            new(database, random, TimeProvider.System), wallet, wallet);
        var request = new CrookedRoadRoundRequest(Guid.NewGuid().ToString(), 100, false);
        var first = await service.SpinAsync(account, request, true, default);

        Assert.True(first.Settled);
        Assert.NotNull(first.Wallet.PayoutTransactionId);
        Assert.Equal(first.Wallet.SettlementId + "-wager", first.Wallet.WagerTransactionId);
        Assert.Equal(first.Wallet.SettlementId + "-payout", first.Wallet.PayoutTransactionId);
        Assert.Null(first.Wallet.LegacyFreeGameTransactionId);
        Assert.Null(first.Wallet.LegacyFreeGamesRemaining);
        var state = await service.ReadStateAsync(account, default);
        Assert.Null(state.PendingOperation);
        Assert.Equal(1, state.GameFeatureState.Funding.Spins);
        Assert.Equal(first.Wallet, (await service.SpinAsync(account, request, false, default)).Wallet);
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Single((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        var prepared = Assert.Single((await database.Collection(CrookedRoadRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal("settled", prepared.GetValue<string>("status"));

        var summaryDocument = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var summary = FirestoreGameRoundAuditStore.Summary(summaryDocument);
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(summary.Id).GetSnapshotAsync();
        using var document = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var data = document.RootElement.GetProperty("data");
        Assert.Empty(CrookedRoadEvidence.Inspect(summary, data));
        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["wallet"]!["wagerTransactionId"] = summary.RoundId + "-wager";
        Assert.Equal("crooked-road-settlement-proof-mismatch", Assert.Single(CrookedRoadEvidence.Inspect(summary,
            JsonSerializer.SerializeToElement(changed))).Code);
    }
}
