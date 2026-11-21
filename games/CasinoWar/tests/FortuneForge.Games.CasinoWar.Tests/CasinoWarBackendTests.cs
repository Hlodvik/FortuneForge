using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Server.Cards.CasinoWar;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.CasinoWar.Tests;

public sealed class CasinoWarBackendTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [CasinoWarEmulatorFact]
    public async Task LostOpeningWalletResponseRecoversOnAnotherReplicaWithOneChargeAndOneHistoryEntry()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet { LoseNextResponse = true };
        var first = new CasinoWarFirestoreStore(db, wallet, wallet, true);
        const string key = "casino-war-opening-lost-0001";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => first.StartAsync("alice", key, 100, 0,
            OpeningWinDeck(), Now, default));
        Assert.Empty((await db.Collection(CasinoWarRoundCodec.Rounds).GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection(CasinoWarFirestoreStore.Commands).GetSnapshotAsync()).Documents);
        Assert.Equal(1, wallet.SettlementCount);
        var replica = new CasinoWarFirestoreStore(db, wallet, wallet);
        var id = CasinoWarRoundCodec.Hash($"alice\n{key}");
        var recovered = (await replica.GetAsync("alice", id, default))!;
        Assert.Equal(CasinoWarRoundPhase.Completed, recovered.Round.Round.Phase);
        Assert.Equal(10_100, recovered.BalanceCents);
        var retry = await replica.StartAsync("alice", key, 100, 0, TieDeck(), Now.AddHours(1), default);
        Assert.Equal(CasinoWarService.ToResponse(recovered), CasinoWarService.ToResponse(retry));
        Assert.Equal(1, wallet.SettlementCount);
        Assert.Single((await db.Collection(CasinoWarRoundCodec.Events).GetSnapshotAsync()).Documents);
        var history = Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal(100, history.GetValue<long>("wagerCents"));
        Assert.Equal(200, history.GetValue<long>("payoutCents"));
        var protectedEvidence = Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        using (var capture = JsonDocument.Parse(protectedEvidence.GetValue<string>("payloadJson")))
        {
            var proof = capture.RootElement.GetProperty("financialProof");
            Assert.Equal("signed-wallet", proof.GetProperty("provenance").GetString());
            Assert.Single(proof.GetProperty("walletOperations").EnumerateArray());
            Assert.Equal(100, proof.GetProperty("entries").EnumerateArray().Sum(entry => entry.GetProperty("amountCents").GetInt64()));
        }
        await NoDirectWalletWrites(db);
    }

    [CasinoWarEmulatorFact]
    public async Task TieSideAwardAndWarChargeSettleOnceAndEveryActionKeepsItsOwnExactReceipt()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet();
        var store = new CasinoWarFirestoreStore(db, wallet, wallet, true);
        var opening = await store.StartAsync("alice", "casino-war-side-opening-0001", 100, 100, TieDeck(), Now, default);
        Assert.Equal(10_900, opening.BalanceCents);
        Assert.Empty((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        wallet.LoseNextResponse = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.DecideAsync("alice", opening.Round.RoundId,
            "casino-war-side-war-0001", CasinoWarTieDecision.GoToWar, Now.AddSeconds(5), default));
        var frozen = await db.Collection(CasinoWarRoundCodec.Rounds).Document(opening.Round.RoundId).GetSnapshotAsync();
        Assert.Equal("awaiting-tie-decision", frozen.GetValue<string>("phase"));
        var replica = new CasinoWarFirestoreStore(db, wallet, wallet);
        var recovery = await replica.RecoverAsync("alice", default);
        Assert.NotNull(recovery.RecoveredRound);
        Assert.Equal("player-war-win", recovery.RecoveredRound!.PrimarySettlement!.Outcome);
        Assert.Empty(recovery.OpenRounds);
        Assert.Equal(11_100, wallet.BalanceCents);
        var retry = await replica.DecideAsync("alice", opening.Round.RoundId, "casino-war-side-war-0001",
            CasinoWarTieDecision.GoToWar, Now.AddMinutes(2), default);
        Assert.Equal(recovery.RecoveredRound, CasinoWarService.ToResponse(retry));
        var openingRetry = await replica.StartAsync("alice", "casino-war-side-opening-0001", 100, 100, OpeningWinDeck(), Now, default);
        Assert.Equal(CasinoWarService.ToResponse(opening), CasinoWarService.ToResponse(openingRetry));
        Assert.Equal(2, wallet.SettlementCount);
        Assert.Equal(300, wallet.Requests.Sum(request => request.WagerCents));
        var history = Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal(300, history.GetValue<long>("wagerCents"));
        Assert.Equal(1_400, history.GetValue<long>("payoutCents"));
        var evidence = Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var immutable = evidence.GetValue<string>("payloadJson");
        using (var capture = JsonDocument.Parse(immutable))
        {
            var proof = capture.RootElement.GetProperty("financialProof");
            Assert.Equal("signed-wallet", proof.GetProperty("provenance").GetString());
            Assert.Equal(2, proof.GetProperty("walletOperations").GetArrayLength());
            var entries = proof.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(-300, entries.Where(entry => entry.GetProperty("amountCents").GetInt64() < 0).Sum(entry => entry.GetProperty("amountCents").GetInt64()));
            Assert.Equal(1_400, entries.Where(entry => entry.GetProperty("amountCents").GetInt64() > 0).Sum(entry => entry.GetProperty("amountCents").GetInt64()));
        }
        await db.Collection(CasinoWarRoundCodec.Rounds).Document(opening.Round.RoundId).UpdateAsync("tieStakeCents", 200L);
        Assert.Equal(immutable, (await evidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        await Assert.ThrowsAsync<CasinoWarRoundConflictException>(() => replica.DecideAsync("alice", opening.Round.RoundId,
            "casino-war-different-decision-0001", CasinoWarTieDecision.Surrender, Now, default));
        await NoDirectWalletWrites(db);
    }

    [CasinoWarEmulatorFact]
    public async Task DeclinedWarDoesNotConsumeTheTieDecisionAndAnotherKeyCanSurrender()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet(100);
        var store = new CasinoWarFirestoreStore(db, wallet, wallet, true);
        var opening = await store.StartAsync("alice", "casino-war-poor-opening-0001", 100, 0, TieDeck(), Now, default);
        await Assert.ThrowsAsync<CasinoWarInsufficientCreditsException>(() => store.DecideAsync("alice", opening.Round.RoundId,
            "casino-war-poor-war-0001", CasinoWarTieDecision.GoToWar, Now, default));
        Assert.Single((await store.RecoverAsync("alice", default)).OpenRounds);
        var surrendered = await store.DecideAsync("alice", opening.Round.RoundId, "casino-war-poor-surrender-0001",
            CasinoWarTieDecision.Surrender, Now, default);
        Assert.Equal(50, surrendered.BalanceCents);
        Assert.Equal(CasinoWarRoundOutcome.PlayerSurrendered, surrendered.Round.Round.Settlement!.Outcome);
        await Assert.ThrowsAsync<CasinoWarInsufficientCreditsException>(() => store.DecideAsync("alice", opening.Round.RoundId,
            "casino-war-poor-war-0001", CasinoWarTieDecision.GoToWar, Now, default));
        Assert.Equal(3, wallet.SettlementCount);
        Assert.Equal(50, wallet.BalanceCents);
    }

    [CasinoWarEmulatorFact]
    public async Task ConcurrentRetriesFreezeOneDealAndChargeOnce()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet();
        var first = new CasinoWarFirestoreStore(db, wallet, wallet, true);
        var replica = new CasinoWarFirestoreStore(db, wallet, wallet, true);
        var rounds = await Task.WhenAll(
            first.StartAsync("alice", "casino-war-concurrent-0001", 100, 0, OpeningWinDeck(), Now, default),
            replica.StartAsync("alice", "casino-war-concurrent-0001", 100, 0, TieDeck(), Now.AddSeconds(1), default));
        Assert.Equal(CasinoWarService.ToResponse(rounds[0]), CasinoWarService.ToResponse(rounds[1]));
        Assert.Equal(1, wallet.SettlementCount);
        Assert.Single((await db.Collection(CasinoWarRoundCodec.Rounds).GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<CasinoWarRoundConflictException>(() => first.StartAsync("alice", "casino-war-concurrent-0001",
            200, 0, OpeningWinDeck(), Now, default));
    }

    [CasinoWarEmulatorFact]
    public async Task LegacyOpeningKeepsItsPaidSideAwardAndOnlyTheNewWarActionUsesTheWallet()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet(10_900);
        const string key = "casino-war-legacy-start-0001";
        var id = CasinoWarRoundCodec.Hash($"alice\n{key}");
        var saved = new CasinoWarSavedRound(id, "alice", 100, 100, TieDeck().Select(CardCode.Format).ToArray(),
            null, key, null, Now);
        var fields = CasinoWarRoundCodec.Write(saved, Now);
        fields.Remove("backendOwner"); fields.Remove("updatedAt");
        await db.Collection(CasinoWarRoundCodec.Rounds).Document(id).SetAsync(fields);
        await db.Collection(CasinoWarRoundCodec.Events).Document(id + "-opening").SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = "alice", ["event"] = "opening", ["idempotencyKey"] = key,
            ["createdAt"] = Timestamp.FromDateTime(Now.UtcDateTime), ["schemaVersion"] = 1L,
        });
        var store = new CasinoWarFirestoreStore(db, wallet, wallet);
        var old = await store.StartAsync("alice", key, 100, 100, OpeningWinDeck(), Now, default);
        Assert.Equal(10_900, old.BalanceCents);
        Assert.Equal(0, wallet.SettlementCount);
        var completed = await store.DecideAsync("alice", id, "casino-war-legacy-war-0001",
            CasinoWarTieDecision.GoToWar, Now.AddSeconds(5), default);
        Assert.Equal(11_100, completed.BalanceCents);
        var request = Assert.Single(wallet.Requests);
        Assert.Equal(100, request.WagerCents);
        Assert.Equal(300, request.PayoutCents);
        var history = Assert.Single(await store.HistoryAsync("alice", 20, default));
        Assert.Equal(id, history.RoundId);
        Assert.Empty(await store.HistoryAsync("someone-else", 20, default));
        Assert.Null(await store.GetAsync("someone-else", id, default));
        var evidence = Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        using (var capture = JsonDocument.Parse(evidence.GetValue<string>("payloadJson")))
        {
            var proof = capture.RootElement.GetProperty("financialProof");
            Assert.Equal("mixed-legacy-and-signed-wallet", proof.GetProperty("provenance").GetString());
            Assert.Equal(2, proof.GetProperty("entries").EnumerateArray().Count(entry => !entry.GetProperty("evidenceAvailable").GetBoolean()));
            Assert.Single(proof.GetProperty("walletOperations").EnumerateArray());
        }
        await NoDirectWalletWrites(db);
    }

    [CasinoWarEmulatorFact]
    public async Task LegacyOpeningLedgerIsFrozenBeforeTheNewDecisionWalletResponseCanBeLost()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet(10_900) { LoseNextResponse = true };
        const string key = "casino-war-protected-legacy-0001";
        var id = CasinoWarRoundCodec.Hash($"alice\n{key}");
        var saved = new CasinoWarSavedRound(id, "alice", 100, 100, TieDeck().Select(CardCode.Format).ToArray(), null, key, null, Now);
        var fields = CasinoWarRoundCodec.Write(saved, Now); fields.Remove("backendOwner");
        await db.Collection(CasinoWarRoundCodec.Rounds).Document(id).SetAsync(fields);
        foreach (var term in new[] { (Suffix: "opening-wager", Kind: "casino-war-opening-wager", Amount: -2d),
                     (Suffix: "tie-payout", Kind: "casino-war-tie-payout", Amount: 11d) })
            await db.Collection("balanceTransactions").Document($"casino-war-{id}-{term.Suffix}").CreateAsync(new Dictionary<string, object>
            {
                ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["type"] = term.Kind, ["amount"] = term.Amount,
            });
        var store = new CasinoWarFirestoreStore(db, wallet, wallet);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.DecideAsync("alice", id, "casino-war-protected-war-0001",
            CasinoWarTieDecision.GoToWar, Now.AddSeconds(5), default));
        await db.Collection("balanceTransactions").Document($"casino-war-{id}-tie-payout").UpdateAsync("amount", 1d);
        await new CasinoWarFirestoreStore(db, wallet, wallet).RecoverAsync("alice", default);
        Assert.Equal(1, wallet.SettlementCount);
        var evidence = Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        using var capture = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var proof = capture.RootElement.GetProperty("financialProof");
        Assert.Equal("mixed-legacy-and-signed-wallet", proof.GetProperty("provenance").GetString());
        Assert.Equal(1_100, Assert.Single(proof.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("kind").GetString() == "casino-war-tie-payout").GetProperty("amountCents").GetInt64());
        Assert.All(proof.GetProperty("entries").EnumerateArray(), entry => Assert.True(entry.GetProperty("evidenceAvailable").GetBoolean()));
    }

    [CasinoWarEmulatorFact]
    public async Task InvalidReceiptAndPausedAdmissionPreserveThePendingActionForRecovery()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet { CorruptNextReceipt = true };
        var store = new CasinoWarFirestoreStore(db, wallet, wallet, true);
        const string key = "casino-war-bad-receipt-0001";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.StartAsync("alice", key, 100, 0,
            TieDeck(), Now, default));
        Assert.Empty((await db.Collection(CasinoWarRoundCodec.Rounds).GetSnapshotAsync()).Documents);
        var paused = new CasinoWarFirestoreStore(db, wallet, wallet);
        var recovered = await paused.RecoverAsync("alice", default);
        Assert.Single(recovered.OpenRounds);
        await Assert.ThrowsAsync<CasinoWarAdmissionPausedException>(() => paused.StartAsync("alice",
            "casino-war-paused-new-0001", 100, 0, OpeningWinDeck(), Now, default));
        var result = await paused.DecideAsync("alice", recovered.OpenRounds[0].RoundId,
            "casino-war-paused-surrender-0001", CasinoWarTieDecision.Surrender, Now, default);
        Assert.Equal(CasinoWarRoundOutcome.PlayerSurrendered, result.Round.Round.Settlement!.Outcome);
        Assert.Equal(2, wallet.SettlementCount);
    }

    [CasinoWarEmulatorFact]
    public async Task HistoricalCompletedDecisionMustReproduceItsStoredSettlementBeforeItCanBeReplayed()
    {
        var db = Database(); var wallet = new CasinoWarTestWallet();
        const string key = "casino-war-corrupt-legacy-0001";
        var id = CasinoWarRoundCodec.Hash($"alice\n{key}");
        var saved = new CasinoWarSavedRound(id, "alice", 100, 0, TieDeck().Select(CardCode.Format).ToArray(),
            "go-to-war", key, "legacy-decision", Now);
        var fields = CasinoWarRoundCodec.Write(saved, Now);
        fields.Remove("backendOwner");
        fields["primaryReturnCents"] = 301L;
        await db.Collection(CasinoWarRoundCodec.Rounds).Document(id).SetAsync(fields);
        var store = new CasinoWarFirestoreStore(db, wallet, wallet);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync("alice", id, default));
        Assert.Equal(0, wallet.SettlementCount);
    }

    internal static FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-casino-war-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build();
    internal static IReadOnlyList<PlayingCard> OpeningWinDeck() => Deck("A|clubs", "K|diamonds");
    internal static IReadOnlyList<PlayingCard> TieDeck() => Deck("5|clubs", "5|diamonds");
    private static IReadOnlyList<PlayingCard> Deck(string player, string dealer) =>
        new[] { player, dealer, "2|clubs", "3|clubs", "4|clubs", "A|spades", "6|clubs", "7|clubs", "8|clubs", "K|hearts" }
            .Select(CardCode.Parse).ToArray();
    private static async Task NoDirectWalletWrites(FirestoreDb database)
    {
        Assert.Empty((await database.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }
}

internal sealed class CasinoWarTestWallet(long startingBalance = 10_000) : IGameWalletClient, IGameWalletAccountReader
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> settled = [];
    internal long BalanceCents { get; private set; } = startingBalance;
    internal bool LoseNextResponse { get; set; }
    internal bool CorruptNextReceipt { get; set; }
    internal int SettlementCount { get { lock (sync) return settled.Count; } }
    internal IReadOnlyList<GameWalletSettlementRequest> Requests { get { lock (sync) return settled.Values.Select(value => value.Request).ToArray(); } }

    public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (settled.TryGetValue(request.OperationId, out var previous))
            {
                if (previous.Request != request) throw new GameWalletConflictException();
                return Task.FromResult(previous.Receipt with { WasAlreadySettled = true });
            }
            var before = BalanceCents;
            var insufficient = before < request.WagerCents;
            var afterWager = insufficient ? before : checked(before - request.WagerCents);
            BalanceCents = insufficient ? before : checked(afterWager + request.PayoutCents);
            var id = GameWalletSettlementIdentity.CreateId("casino-war", accountId, request.OperationId);
            var receipt = new GameWalletSettlementReceipt(id,
                !insufficient && request.WagerCents > 0 ? id + "-wager" : null,
                !insufficient && request.PayoutCents > 0 ? id + "-payout" : null,
                before, afterWager, BalanceCents, CasinoWarBackendTests.Now.AddSeconds(settled.Count + 1), false, insufficient);
            settled.Add(request.OperationId, (request, receipt));
            if (LoseNextResponse) { LoseNextResponse = false; throw new GameWalletUnavailableException(); }
            if (CorruptNextReceipt) { CorruptNextReceipt = false; return Task.FromResult(receipt with { SettlementId = "wrong-receipt" }); }
            return Task.FromResult(receipt);
        }
    }

    public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    {
        lock (sync) return Task.FromResult(new GameWalletAccountState(0, BalanceCents));
    }
}

public sealed class CasinoWarEmulatorFactAttribute : FactAttribute
{
    public CasinoWarEmulatorFactAttribute()
    {
        var host = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (string.IsNullOrWhiteSpace(host)) Skip = "Run against the Firestore emulator to verify paid Casino War ownership.";
        else if (!host.StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase) && !host.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Casino War tests require a localhost Firestore emulator.");
    }
}
