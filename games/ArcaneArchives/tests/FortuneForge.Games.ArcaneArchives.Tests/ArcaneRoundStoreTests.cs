using System.Globalization;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.ArcaneArchives.Tests;

public sealed class ArcaneRoundStoreTests
{
    private const string Account = "test-account";
    private const string GameId = ArcaneArchivesEngine.GameId;
    private const long Wager = 12;

    [FirestoreEmulatorFact]
    public async Task CommittedWalletReplyLossResumesExactPreparedOutcomeAndPublishesOnce()
    {
        var database = Connect();
        var wallet = new IdempotentWallet { LoseFirstResponse = true };
        var request = new ArcaneRoundRequest("original-spin-id-123456789", Wager, false);
        var first = new ArcaneRoundService(new ArcaneRoundStore(database), wallet);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => first.SpinAsync(Account, request, true, default));
        Assert.Equal(request, (await first.ReadStateAsync(Account, default)).PendingRequest);
        Assert.False((await Result(database, wallet.LastRequest!.OperationId).GetSnapshotAsync()).Exists);

        var conflict = await Assert.ThrowsAsync<ArcaneRoundConflictException>(() =>
            first.SpinAsync(Account, request with { OperationId = "different-spin-id-123456" }, true, default));
        Assert.Equal(request, conflict.PendingRequest);
        await Assert.ThrowsAsync<ArcaneRoundConflictException>(() =>
            first.SpinAsync(Account, request with { WagerPoints = Wager + 4 }, true, default));
        Assert.Equal(1, wallet.Calls);

        var replica = new ArcaneRoundService(new ArcaneRoundStore(database), wallet);
        var result = await replica.SpinAsync(Account, request, false, default);
        var round = Assert.IsType<ArcanePreparedRound>(result.Round);
        Assert.Equal(wallet.LastRequest.OperationId, round.Outcome.SpinId.ToString("N"));
        Assert.Equal(wallet.LastRequest.OutcomeSha256, round.OutcomeSha256);
        Assert.NotNull(result.Features);
        Assert.Equal(1, wallet.Commits);
        Assert.Equal(2, wallet.Calls);
        Assert.Null((await replica.ReadStateAsync(Account, default)).PendingRequest);
        Assert.True((await Result(database, round.Outcome.SpinId.ToString("N")).GetSnapshotAsync()).Exists);
        Assert.Single((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);

        var replay = await replica.SpinAsync(Account, request, false, default);
        Assert.Equal(result.Wallet, replay.Wallet);
        Assert.Equal(round.OutcomeSha256, replay.Round!.OutcomeSha256);
        Assert.Equal(2, wallet.Calls);

        var raw = database.Collection(ArcaneRoundStore.RawCollectionName).Document(round.AdmissionId.ToString("N"));
        var snapshot = await raw.GetSnapshotAsync();
        Assert.True(ArcaneRoundStore.TryGetSettledRequestId(snapshot, round.Outcome.SpinId.ToString("N"), out _));
        Assert.False(ArcaneRoundStore.TryGetSettledRequestId(snapshot, Guid.NewGuid().ToString("N"), out _));
        await raw.DeleteAsync();
        await Assert.ThrowsAsync<ArcaneRoundConflictException>(() => replica.SpinAsync(Account, request, true, default));
        Assert.Equal(2, wallet.Calls);
    }

    [FirestoreEmulatorFact]
    public async Task TerminalDeclineClearsGuardWithoutRevealingOutcomeOrChangingFeatures()
    {
        var database = Connect();
        var wallet = new IdempotentWallet { InitialBalance = 0 };
        var replica = new ArcaneRoundService(new ArcaneRoundStore(database), wallet);
        var request = new ArcaneRoundRequest("declined-spin-id-123456789", Wager, false);
        using var requestScope = GameSecurityContext.Push(new GameRequestObservation("private-decline", Account,
            "private-session-id", "203.0.113.11", DateTime.UtcNow, "POST", "/api/slots/arcane/prepared-spins",
            "arcane-archives", null, request.OperationId, null, null, "sanitized", "direct"));
        var before = await replica.ReadStateAsync(Account, default);
        var result = await replica.SpinAsync(Account, request, true, default);
        Assert.True(result.Wallet.InsufficientFunds);
        Assert.Null(result.Round);
        Assert.Null(result.Features);

        var after = await replica.ReadStateAsync(Account, default);
        Assert.Null(after.PendingRequest);
        Assert.True(ArcaneTome.SameState(before.Tome, after.Tome));
        Assert.True(ArcanePotions.SameState(before.Potions, after.Potions));
        Assert.False((await Result(database, wallet.LastRequest!.OperationId).GetSnapshotAsync()).Exists);
        var redacted = Assert.Single((await database.Collection(ArcaneRoundStore.RawCollectionName).GetSnapshotAsync()).Documents);
        Assert.False(redacted.ContainsField("requestId"));
        Assert.DoesNotContain("private-session-id", redacted.GetValue<string>("preparedJson"), StringComparison.Ordinal);
        Assert.False(ArcaneRoundStore.TryGetSettledRequestId(redacted, wallet.LastRequest.OperationId, out _));
        Assert.Equal(result, await replica.SpinAsync(Account, request, false, default));
        Assert.Equal(1, wallet.Calls);
    }

    [FirestoreEmulatorFact]
    public async Task LegacyOwnedKeySignalsExactRecoveryAndDifferentKeyDiscoversPendingTerms()
    {
        var database = Connect();
        var wallet = new IdempotentWallet();
        var replica = new ArcaneRoundService(new ArcaneRoundStore(database), wallet);
        var old = new ArcaneRoundRequest("legacy-owned-id-123456789", Wager, false);
        var id = Guid.NewGuid().ToString("N");
        var started = DateTime.UtcNow.AddMinutes(-3);
        var guard = Guard(database);
        var terms = JsonSerializer.Serialize(new
        {
            wagerPoints = old.WagerPoints,
            pointValueInCents = ArcaneArchivesEngine.CreateGame().Wagering.PointValueInCents.ToString("G29", CultureInfo.InvariantCulture),
            useFreeSpin = false, useSpecialBoost = false, specialBoostCost = 0,
        });
        var batch = database.StartBatch();
        batch.Create(database.Collection("slotPreparedOperations").Document(GameSecurityEventDocuments.Hash($"{GameId}\n{Account}\n{old.OperationId}")),
            new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L, ["admissionId"] = id, ["requestSha256"] = GameSecurityEventDocuments.Hash(terms),
            });
        batch.Create(database.Collection("slotSpinAdmissions").Document(id), new Dictionary<string, object>
        {
            ["admissionId"] = id, ["userId"] = Account, ["gameId"] = GameId, ["status"] = "admitted",
            ["preparedOperationId"] = old.OperationId, ["requestedWagerPoints"] = old.WagerPoints,
            ["isFreeSpin"] = false, ["specialBoostApplied"] = false,
        });
        batch.Create(guard, new Dictionary<string, object>
        {
            [SlotSpinGuard.ActiveAdmissionField] = id, [SlotSpinGuard.PreparedAdmissionField] = id,
            [SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(started),
        });
        await batch.CommitAsync();

        var pending = await replica.ReadStateAsync(Account, default);
        Assert.True(pending.LegacyPending);
        Assert.Equal(old, pending.PendingRequest);
        var conflict = await Assert.ThrowsAsync<ArcaneRoundConflictException>(() =>
            replica.SpinAsync(Account, old with { OperationId = "brand-new-id-123456789" }, true, default));
        Assert.Equal(old, conflict.PendingRequest);
        await Assert.ThrowsAsync<ArcaneLegacyPreparedRoundException>(() => replica.SpinAsync(Account, old, false, default));
        await Assert.ThrowsAsync<ArcaneRoundConflictException>(() =>
            replica.SpinAsync(Account, old with { WagerPoints = Wager + 4 }, true, default));

        await guard.UpdateAsync(new Dictionary<string, object> { [SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete });
        var unkeyed = await replica.ReadStateAsync(Account, default);
        Assert.True(unkeyed.LegacyPending);
        Assert.Null(unkeyed.PendingRequest);
        await Assert.ThrowsAsync<ArcaneRoundConflictException>(() =>
            replica.SpinAsync(Account, old with { OperationId = "another-brand-new-123456" }, true, default));
        Assert.Equal(0, wallet.Calls);
    }

    [FirestoreEmulatorFact]
    public async Task PendingSpinPreservesAndConsumesTheAdmittedTomeAndPotionOnce()
    {
        var database = Connect();
        var wallet = new IdempotentWallet { LoseFirstResponse = true };
        var service = new ArcaneRoundService(new ArcaneRoundStore(database), wallet);
        var tome = ArcaneTome.SelectPage(ArcaneTome.Empty with { GrimoireReady = true }, "herbalism", true);
        var bottle = new ArcanePotionBottle(1, ArcaneTome.MandrakeRoot, 2) { LockedWagerPoints = Wager };
        var potions = new ArcanePotionState([], [], new(bottle, ArcanePotions.Duration), 2);
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["arcaneTomeJson"] = JsonSerializer.Serialize(tome),
            ["arcanePotionsJson"] = JsonSerializer.Serialize(potions),
        });

        var request = new ArcaneRoundRequest("boosted-spin-id-123456789", Wager, false);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync(Account, request, true, default));
        var pending = await service.ReadStateAsync(Account, default);
        Assert.True(ArcaneTome.SameState(tome, pending.Tome));
        Assert.True(ArcanePotions.SameState(potions, pending.Potions));
        await Assert.ThrowsAsync<ArcaneTomeConflictException>(() => new ArcaneFeatureStore(database)
            .SelectTomePageAsync(Account, "herbalism", false, DateTime.UtcNow, default));

        var result = await service.SpinAsync(Account, request, false, default);
        Assert.Equal(ArcaneTome.BoostedSpinCount - 1, result.Features!.Tome!.State.BoostedSpinsRemaining);
        Assert.Equal(ArcanePotions.Duration - 1, result.Features.Potions!.Active!.SpinsRemaining);
        var outcome = result.Round!.Outcome;
        Assert.True(ArcaneArchivesEngine.MatchesTomeState(outcome.ReelSetId, tome));
        Assert.DoesNotContain(ArcaneTome.GrimoireSymbolId, outcome.Reels.SelectMany(reel => reel));
        var persisted = await service.ReadStateAsync(Account, default);
        Assert.True(ArcaneTome.SameState(result.Features.Tome.State, persisted.Tome));
        Assert.True(ArcanePotions.SameState(result.Features.Potions, persisted.Potions));
        Assert.Equal(1, wallet.Commits);
    }

    [FirestoreEmulatorFact]
    public async Task LegacyFreeSpinCarriesItsStoredWagerAndUsesOneScopedEntitlement()
    {
        var database = Connect();
        var wallet = new IdempotentWallet { FreeGames = 1 };
        var replica = new ArcaneRoundService(new ArcaneRoundStore(database), wallet);
        await Guard(database).CreateAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 300L, ["freeSpinWagerPoints"] = 12L,
            ["freeSpinFeatureMode"] = "legacy-arcane",
        });
        var request = new ArcaneRoundRequest("free-spin-legacy-id-123456789", 16, true);
        var result = await replica.SpinAsync(Account, request, true, default);
        Assert.True(wallet.LastRequest!.SpendLegacyFreeGame);
        Assert.Equal(0, wallet.LastRequest.WagerCents);
        Assert.Equal(12, result.Round!.Outcome.WagerPoints);
        Assert.Equal(0, result.Round.FreeSpinsRemaining);
        Assert.Equal("legacy-arcane", result.Round.AdmittedFreeSpinFeatureMode);
        Assert.Null(result.Round.FreeSpinFeatureMode);
        Assert.Null((await replica.ReadStateAsync(Account, default)).FreeSpinWagerPoints);
        Assert.Equal(0, wallet.FreeGames);
        Assert.Equal(result.Wallet, (await replica.SpinAsync(Account, request, false, default)).Wallet);
        Assert.Equal(1, wallet.Commits);
    }

    private static DocumentReference Guard(FirestoreDb database) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, GameId));

    private static DocumentReference Result(FirestoreDb database, string spinId) =>
        database.Collection("slotSpinResults").Document(spinId);

    private static FirestoreDb Connect()
    {
        var project = $"demo-arcane-{Guid.NewGuid():N}"[..29];
        return new FirestoreDbBuilder { ProjectId = project, EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
    }

    private sealed class IdempotentWallet : IGameWalletClient
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Terms, GameWalletSettlementReceipt Receipt)> receipts = [];
        private long? balance;
        public long InitialBalance { get; init; } = 100_000;
        public bool LoseFirstResponse { get; set; }
        public long FreeGames { get; set; }
        public int Calls { get; private set; }
        public int Commits => receipts.Count;
        public GameWalletSettlementRequest? LastRequest { get; private set; }

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            if (receipts.TryGetValue(request.OperationId, out var original))
            {
                Assert.Equal(original.Terms, request);
                return Task.FromResult(original.Receipt with { WasAlreadySettled = true });
            }

            var current = balance ?? InitialBalance;
            var insufficient = current < request.WagerCents;
            var noFreeGame = request.SpendLegacyFreeGame && FreeGames == 0;
            var accepted = !insufficient && !noFreeGame;
            var afterWager = accepted ? current - request.WagerCents : current;
            balance = accepted ? afterWager + request.PayoutCents : afterWager;
            if (accepted && request.SpendLegacyFreeGame) FreeGames--;
            var spin = request.OperationId;
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(GameId, accountId, spin),
                accepted && request.WagerCents > 0 ? spin + "-wager" : null,
                accepted && request.PayoutCents > 0 ? spin + "-payout" : null,
                current, afterWager, balance.Value, DateTimeOffset.UtcNow, false, insufficient, noFreeGame,
                accepted && request.SpendLegacyFreeGame ? spin + "-free-game-use" : null, FreeGames);
            receipts.Add(spin, (request, receipt));
            if (LoseFirstResponse)
            {
                LoseFirstResponse = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }
    }
}

public sealed class FirestoreEmulatorFactAttribute : FactAttribute
{
    public FirestoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "Run with the Firestore emulator to verify Arcane spin and wallet recovery.";
    }
}
