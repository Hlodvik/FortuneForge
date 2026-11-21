using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.HiLo.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.HiLo.Server.Tests;

[CollectionDefinition("Hi-Lo Firestore", DisableParallelization = true)]
public sealed class HiLoFirestoreCollection : ICollectionFixture<HiLoEmulator>;

[Collection("Hi-Lo Firestore")]
public sealed class FirestoreTests(HiLoEmulator emulator)
{
    private FirestoreDb Database() => new FirestoreDbBuilder { ProjectId = "demo-hilo-" + Guid.NewGuid().ToString("N"), Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None }.Build();
    private static HiLoService Service(FirestoreDb db, TestWallet wallet, CardRank rank = CardRank.Seven, bool enabled = true, bool noDraw = false) =>
        new(new HiLoFirestoreStore(db, wallet, wallet), draw: () => noDraw ? throw new InvalidOperationException("No RNG on recovery") : PracticeTests.Card(rank), allowNewMutations: enabled);
    private static string Key() => Guid.NewGuid().ToString("N");
    private static DocumentReference Head(FirestoreDb db, string user = "player") => db.Collection("hiLoPlayers").Document(HiLoService.Hash(user));
    private static Task SeedRound(FirestoreDb db, HiLoRound round, string user = "player") => Head(db, user).SetAsync(new Dictionary<string, object>
    {
        ["schemaVersion"] = 1L, ["userId"] = user, ["roundJson"] = JsonSerializer.Serialize(round),
        ["updatedAt"] = Timestamp.FromDateTime(DateTime.UtcNow), ["roundStartedAt"] = Timestamp.FromDateTime(DateTime.UtcNow),
    });

    [Fact]
    public async Task PaidHttpRoutes_RecoverLostWalletRepliesAndRetainAccountScopeWhilePaused()
    {
        var db = Database(); var wallet = new TestWallet { LoseNextReply = GameWalletSettlementPhase.Reserve };
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration["Features:HiLoEnabled"] = "true";
        builder.Services.AddSingleton(db).AddSingleton<IGameWalletClient>(wallet).AddSingleton<IGameWalletAccountReader>(wallet);
        builder.Services.AddHiLoServer();
        await using var app = builder.Build();
        app.MapHiLo((context, _) => Task.FromResult<HiLoAccount?>(new(context.Request.Headers["Test-Account"].FirstOrDefault() ?? "player")));
        await app.StartAsync();
        var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-FortuneForge-Game", "hi-lo");
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var operation = Key();
        var request = new { operationId = operation, wagerCents = 100, userId = "victim", nextCard = "king", payoutCents = 999999 };
        var interrupted = await client.PostAsJsonAsync("/api/games/hi-lo/rounds", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, interrupted.StatusCode);
        Assert.DoesNotContain("currentCard", await interrupted.Content.ReadAsStringAsync());
        var restored = await client.GetFromJsonAsync<HiLoSessionResponse>("/api/games/hi-lo/session");
        Assert.Equal(9900, restored!.WalletBalanceCents); Assert.False(restored.Practice);
        var retry = await client.PostAsJsonAsync("/api/games/hi-lo/rounds", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(restored.Round!.RoundId, (await retry.Content.ReadFromJsonAsync<HiLoSessionResponse>())!.Round!.RoundId);
        Assert.DoesNotContain("victim", await retry.Content.ReadAsStringAsync());
        app.Services.GetRequiredService<IConfiguration>()["Features:HiLoEnabled"] = "false";
        wallet.LoseNextReply = GameWalletSettlementPhase.CompleteReservation;
        var cashout = new HiLoActionRequest(Key(), 0, "cashout");
        var path = $"/api/games/hi-lo/rounds/{restored.Round.RoundId}/actions";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(path, cashout)).StatusCode);
        var collected = await client.GetFromJsonAsync<HiLoSessionResponse>("/api/games/hi-lo/session");
        Assert.Equal("cashedOut", collected!.Round!.Status); Assert.False(collected.Enabled);
        Assert.Equal(10000, collected.WalletBalanceCents);
        var paid = await client.PostAsJsonAsync(path, cashout);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.False((await paid.Content.ReadFromJsonAsync<HiLoSessionResponse>())!.Enabled);
        Assert.Equal(1, wallet.Debits); Assert.Equal(1, wallet.Credits);
        var bob = app.GetTestClient(); bob.DefaultRequestHeaders.Add("Test-Account", "bob");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await bob.GetAsync("/api/games/hi-lo/session")).StatusCode);
        Assert.Null((await new HiLoFirestoreStore(db, wallet, wallet).GetSessionAsync("bob")).Round);
        Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task DuplicateStartsAndCashouts_ChargeAndPayOnce_WithDurableHistoryAndExactResponses()
    {
        var db = Database(); var wallet = new TestWallet();
        // The game has no authority to change either this platform wallet or its unrelated fields.
        var canonical = db.Collection("userBalances").Document("player_slotsCredits");
        await canonical.SetAsync(new Dictionary<string, object> { ["available"] = 777L, ["unrelated"] = "preserve" });
        var start = new HiLoStartRequest(Key(), 100);
        var starts = await Task.WhenAll(Service(db, wallet).StartAsync("player", start), Service(db, wallet).StartAsync("player", start));
        Assert.Equal(JsonSerializer.Serialize(starts[0]), JsonSerializer.Serialize(starts[1]));
        var round = starts[0].Round!;
        Assert.Equal(9900, starts[0].WalletBalanceCents);
        Assert.Single(wallet.Requests);
        Assert.Equal(GameWalletSettlementPhase.Reserve, wallet.Requests.Single().Phase);
        Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<HiLoException>(() => Service(db, wallet).StartAsync("player", start with { WagerCents = 200 }))).Code);
        var won = await Service(db, wallet, CardRank.King).ActAsync("player", round.RoundId, new(Key(), 0, "higher"));
        Assert.Equal(214, won.Round!.CurrentReturnCents);
        var cashout = new HiLoActionRequest(Key(), 1, "cashout");
        var payouts = await Task.WhenAll(Service(db, wallet).ActAsync("player", round.RoundId, cashout), Service(db, wallet).ActAsync("player", round.RoundId, cashout));
        Assert.All(payouts, response => Assert.Equal(10114, response.WalletBalanceCents));
        Assert.Equal(JsonSerializer.Serialize(payouts[0]), JsonSerializer.Serialize(payouts[1]));
        Assert.Equal(1, wallet.Debits); Assert.Equal(1, wallet.Credits);
        var completion = Assert.Single(wallet.Requests, request => request.Phase == GameWalletSettlementPhase.CompleteReservation);
        Assert.Equal(wallet.Requests.First().OperationId, completion.OperationId);
        Assert.Equal(wallet.Requests.First().OutcomeSha256, completion.ReservationSha256);
        var history = Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal("hi-lo-" + round.RoundId, history.Id);
        Assert.Equal(100L, history.GetValue<long>("wagerCents")); Assert.Equal(214L, history.GetValue<long>("payoutCents"));
        Assert.False(history.ContainsField("roundJson"));
        await history.Reference.UpdateAsync("seenAt", Timestamp.FromDateTime(DateTime.UtcNow));
        var replay = await Service(db, wallet, enabled: false, noDraw: true).ActAsync("player", round.RoundId, cashout);
        Assert.Equal(JsonSerializer.Serialize(payouts[0]), JsonSerializer.Serialize(replay));
        Assert.True((await history.Reference.GetSnapshotAsync()).ContainsField("seenAt"));
        Assert.Equal(3, (await db.Collection("hiLoEvents").GetSnapshotAsync()).Count);
        Assert.Equal(3, (await db.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Count);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(777L, (await canonical.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Null((await Service(db, wallet).GetSessionAsync("other")).Round);
    }

    [Theory]
    [InlineData(GameWalletSettlementPhase.Reserve)]
    [InlineData(GameWalletSettlementPhase.CompleteReservation)]
    public async Task LostWalletReply_ResumesOriginalIntentAfterRestartAndPause_WithoutDrawingOrPayingTwice(GameWalletSettlementPhase failurePhase)
    {
        var db = Database(); var wallet = new TestWallet();
        var start = new HiLoStartRequest(Key(), 100);
        if (failurePhase == GameWalletSettlementPhase.Reserve) wallet.LoseNextReply = failurePhase;
        HiLoSessionResponse initial;
        if (failurePhase == GameWalletSettlementPhase.Reserve)
        {
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", start));
            Assert.False((await Head(db).GetSnapshotAsync()).ContainsField("roundJson"));
            Assert.Equal("operation_pending", (await Assert.ThrowsAsync<HiLoException>(() => Service(db, wallet).StartAsync("player", new(Key(), 100)))).Code);
            initial = await Service(db, wallet, enabled: false, noDraw: true).GetSessionAsync("player");
            Assert.Equal(9900, initial.WalletBalanceCents);
            Assert.Equal(initial.Round!.RoundId, (await Service(db, wallet, enabled: false, noDraw: true).StartAsync("player", start)).Round!.RoundId);
        }
        else initial = await Service(db, wallet).StartAsync("player", start);
        Assert.Equal("hi_lo_disabled", (await Assert.ThrowsAsync<HiLoException>(() => Service(db, wallet, enabled: false, noDraw: true).ActAsync("player", initial.Round!.RoundId, new(Key(), 0, "higher")))).Code);
        var command = new HiLoActionRequest(Key(), 0, "cashout");
        if (failurePhase == GameWalletSettlementPhase.CompleteReservation)
        {
            wallet.LoseNextReply = failurePhase;
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).ActAsync("player", initial.Round!.RoundId, command));
            Assert.Empty((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
            var restored = await Service(db, wallet, enabled: false, noDraw: true).GetSessionAsync("player");
            Assert.Equal("cashedOut", restored.Round!.Status);
        }
        var paid = await Service(db, wallet, enabled: false, noDraw: true).ActAsync("player", initial.Round!.RoundId, command);
        Assert.Equal(10000, paid.WalletBalanceCents);
        Assert.Equal(failurePhase == GameWalletSettlementPhase.CompleteReservation, paid.Enabled);
        Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal(1, wallet.Debits); Assert.Equal(1, wallet.Credits);
    }

    [Fact]
    public async Task RecoveryAuditRetainsTheOriginalAcceptedRequest()
    {
        var db = Database(); var wallet = new TestWallet { LoseNextReply = GameWalletSettlementPhase.Reserve };
        var start = new HiLoStartRequest(Key(), 100);
        var observation = new GameRequestObservation("original-wager-request", "player", null, null, DateTime.UtcNow,
            "POST", "/api/games/hi-lo/rounds", "hi-lo", null, start.OperationId, null, null, "none", "test");
        using (GameSecurityContext.Push(observation))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", start));
        using (GameSecurityContext.Push(observation with { RequestId = "recovery-read", Method = "GET", Path = "/api/games/hi-lo/session" }))
            await Service(db, wallet, noDraw: true).GetSessionAsync("player");
        var captured = Assert.Single((await db.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal("original-wager-request", captured.GetValue<string>("requestId"));
        Assert.DoesNotContain("recovery-read", captured.GetValue<string>("payloadJson"));
    }

    [Fact]
    public async Task ConcurrentDifferentCommands_CannotCreateSecondRoundOrAdvanceSameRevisionTwice()
    {
        var db = Database(); var wallet = new TestWallet();
        async Task<HiLoSessionResponse?> Try(Func<Task<HiLoSessionResponse>> action)
        {
            try { return await action(); }
            catch (HiLoException exception) when (exception.Code is "round_active" or "operation_pending" or "stale_revision") { return null; }
        }
        var starts = await Task.WhenAll(Try(() => Service(db, wallet).StartAsync("player", new(Key(), 100))), Try(() => Service(db, wallet).StartAsync("player", new(Key(), 100))));
        var round = Assert.Single(starts, value => value is not null)!.Round!;
        var guesses = await Task.WhenAll(Try(() => Service(db, wallet, CardRank.King).ActAsync("player", round.RoundId, new(Key(), 0, "higher"))),
            Try(() => Service(db, wallet, CardRank.King).ActAsync("player", round.RoundId, new(Key(), 0, "higher"))));
        Assert.Equal(1, Assert.Single(guesses, value => value is not null)!.Round!.Revision);
        Assert.Equal(1, wallet.Debits);
        Assert.Equal(9900, wallet.Balance);
    }

    [Fact]
    public async Task Loss_ClosesReservationWithZeroPayout_AndRemainsReplayable()
    {
        var db = Database(); var wallet = new TestWallet();
        var initial = await Service(db, wallet).StartAsync("player", new(Key(), 100));
        var request = new HiLoActionRequest(Key(), 0, "higher");
        var loss = await Service(db, wallet, CardRank.Ace).ActAsync("player", initial.Round!.RoundId, request);
        Assert.Equal("lost", loss.Round!.Status); Assert.Equal(0, loss.Round.CurrentReturnCents);
        Assert.Equal(0, Assert.Single(wallet.Requests, value => value.Phase == GameWalletSettlementPhase.CompleteReservation).PayoutCents);
        Assert.Equal(JsonSerializer.Serialize(loss), JsonSerializer.Serialize(await Service(db, wallet, noDraw: true).ActAsync("player", initial.Round.RoundId, request)));
        Assert.Equal(1, wallet.Debits); Assert.Equal(0, wallet.Credits);
        Assert.Equal(0L, Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents).GetValue<long>("payoutCents"));
    }

    [Fact]
    public async Task LegacyOddAndLargeSavedWagers_CashOutWithoutAnotherWager_AndLegacyJournalReplaysWithoutWallet()
    {
        var db = Database(); var wallet = new TestWallet();
        var round = HiLoEngine.RebuildInitial(Guid.NewGuid(), 1000m, PracticeTests.Card(CardRank.Seven));
        await SeedRound(db, round);
        var active = await Service(db, wallet).GetSessionAsync("player");
        Assert.Equal(100000, active.Round!.WagerCents);
        var request = new HiLoActionRequest(Key(), 0, "cashout");
        var settled = await Service(db, wallet, enabled: false, noDraw: true).ActAsync("player", active.Round.RoundId, request);
        Assert.Equal(110000, settled.WalletBalanceCents);
        Assert.Equal(0, wallet.Debits); Assert.Equal(0, wallet.Requests.Single().WagerCents);
        Assert.Equal(GameWalletSettlementPhase.Direct, wallet.Requests.Single().Phase);
        // A historical schema-one response remains byte-for-byte stable and requires no wallet availability.
        var legacyCommand = new HiLoCommand(Key(), "start", 100000);
        var hash = HiLoService.Hash("legacy\n" + legacyCommand.OperationId);
        var legacy = active with { WalletBalanceCents = 12345, Limits = active.Limits with { MaxWagerCents = 100000 } };
        await db.Collection("hiLoOperations").Document(hash).SetAsync(new Dictionary<string, object>
        { ["schemaVersion"] = 1L, ["userId"] = "legacy", ["operationId"] = legacyCommand.OperationId,
            ["fingerprint"] = HiLoService.Fingerprint(legacyCommand), ["responseJson"] = JsonSerializer.Serialize(legacy), ["walletDeltaCents"] = -100000L });
        await db.Collection("hiLoEvents").Document(hash).SetAsync(new Dictionary<string, object>
        { ["userId"] = "legacy", ["fingerprint"] = HiLoService.Fingerprint(legacyCommand), ["roundId"] = active.Round.RoundId });
        var ledger = db.Collection("balanceTransactions").Document("hi-lo-" + hash);
        await ledger.SetAsync(new Dictionary<string, object>
        { ["transactionId"] = ledger.Id, ["userId"] = "legacy", ["currencyId"] = "slotsCredits", ["idempotencyKey"] = legacyCommand.OperationId,
            ["type"] = "hi-lo-wager", ["amountCents"] = -100000L, ["balanceAfterCents"] = 12345L, ["amount"] = -1000d, ["balanceAfter"] = 123.45d });
        wallet.Unavailable = true;
        Assert.Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(await Service(db, wallet, enabled: false, noDraw: true).StartAsync("legacy", new(legacyCommand.OperationId, 100000))));
        await ledger.UpdateAsync("amountCents", -999L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, wallet, noDraw: true).StartAsync("legacy", new(legacyCommand.OperationId, 100000)));
        wallet.Unavailable = false;
        await SeedRound(db, HiLoEngine.RebuildInitial(Guid.NewGuid(), 1.37m, PracticeTests.Card(CardRank.Seven)), "odd");
        var odd = await Service(db, wallet, noDraw: true).GetSessionAsync("odd");
        Assert.Equal(137, (await Service(db, wallet, noDraw: true).ActAsync("odd", odd.Round!.RoundId, new(Key(), 0, "cashout"))).Round!.CurrentReturnCents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsufficientFunds_IsDurableAndNeverRevealsCard_WhileNewOperationCanProceedAfterTopup(bool staleAccountRead)
    {
        var db = Database(); var wallet = new TestWallet { Balance = 0, ReadBalance = staleAccountRead ? 10000 : 0 };
        var start = new HiLoStartRequest(Key(), 100);
        Assert.Equal("insufficient_funds", (await Assert.ThrowsAsync<HiLoException>(() => Service(db, wallet, noDraw: !staleAccountRead).StartAsync("player", start))).Code);
        Assert.False((await Head(db).GetSnapshotAsync()).ContainsField("roundJson"));
        Assert.Empty((await db.Collection("hiLoEvents").GetSnapshotAsync()).Documents);
        wallet.Balance = 10000;
        wallet.ReadBalance = null;
        Assert.Equal("insufficient_funds", (await Assert.ThrowsAsync<HiLoException>(() => Service(db, wallet, noDraw: true).StartAsync("player", start))).Code);
        Assert.Equal(9900, (await Service(db, wallet).StartAsync("player", new(Key(), 100))).WalletBalanceCents);
        Assert.Equal(1, wallet.Debits);
    }

    [Fact]
    public async Task MissingOperationWithLegacyWitnessCannotBeChargedAgain()
    {
        foreach (var collection in new[] { "balanceTransactions", "hiLoEvents" })
        {
            var db = Database(); var wallet = new TestWallet(); var request = new HiLoStartRequest(Key(), 100);
            var hash = HiLoService.Hash("player\n" + request.OperationId);
            await db.Collection(collection).Document(collection == "balanceTransactions" ? "hi-lo-" + hash : hash)
                .SetAsync(new Dictionary<string, object> { ["userId"] = "player" });
            var draws = 0;
            var service = new HiLoService(new HiLoFirestoreStore(db, wallet, wallet), draw: () => { draws++; return PracticeTests.Card(CardRank.Seven); });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", request));
            Assert.Equal(0, draws); Assert.Empty(wallet.Requests); Assert.Equal(10000, wallet.Balance);
        }
    }

    [Fact]
    public async Task ExistingTerminalHistoryFailsBeforeAnyLegacyRoundCredit()
    {
        var db = Database(); var wallet = new TestWallet();
        var round = HiLoEngine.RebuildInitial(Guid.NewGuid(), 1.37m, PracticeTests.Card(CardRank.Seven));
        await SeedRound(db, round);
        await db.Collection("cardGameResults").Document("hi-lo-" + round.Id.ToString("N")).SetAsync(new Dictionary<string, object> { ["userId"] = "player" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, wallet, noDraw: true).ActAsync("player", round.Id.ToString("N"), new(Key(), 0, "cashout")));
        Assert.Empty(wallet.Requests); Assert.Equal(0, wallet.Credits);
        Assert.Empty((await db.Collection("hiLoOperations").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CorruptRevealedStateOrSavedReceipt_FailsClosed()
    {
        var db = Database(); var wallet = new TestWallet(); var start = new HiLoStartRequest(Key(), 100);
        var response = await Service(db, wallet).StartAsync("player", start);
        var head = await Head(db).GetSnapshotAsync();
        await Head(db).UpdateAsync("roundJson", head.GetValue<string>("roundJson").Replace("\"Multiplier\":1,", "\"Multiplier\":900,"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, wallet).GetSessionAsync("player"));
        await Head(db).UpdateAsync("roundJson", head.GetValue<string>("roundJson").Replace("\"Rank\":7", "\"Rank\":99"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, wallet).GetSessionAsync("player"));
        await Head(db).UpdateAsync("roundJson", "truncated-json");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, wallet).GetSessionAsync("player"));
        var operation = db.Collection("hiLoOperations").Document(HiLoService.Hash("player\n" + start.OperationId));
        var saved = await operation.GetSnapshotAsync();
        await operation.UpdateAsync("walletReceiptJson", saved.GetValue<string>("walletReceiptJson").Replace("\"BalanceAfterCents\":9900", "\"BalanceAfterCents\":9999"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, wallet, noDraw: true).StartAsync("player", start));
        Assert.Equal(1, wallet.Debits);
    }
}

/// <summary>An independent wallet boundary with durable operation semantics across game-store instances.</summary>
internal sealed class TestWallet : IGameWalletClient, IGameWalletAccountReader
{
    private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
    private readonly object sync = new();
    public long Balance { get; set; } = 10000;
    public long? ReadBalance { get; set; }
    public int Debits { get; private set; }
    public int Credits { get; private set; }
    public bool Unavailable { get; set; }
    public GameWalletSettlementPhase? LoseNextReply { get; set; }
    public IReadOnlyList<GameWalletSettlementRequest> Requests { get { lock (sync) return receipts.Values.Select(value => value.Request).ToArray(); } }
    public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    { if (Unavailable) throw new GameWalletUnavailableException(); lock (sync) return Task.FromResult(new GameWalletAccountState(0, ReadBalance ?? Balance)); }
    public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new GameWalletUnavailableException();
        lock (sync)
        {
            var id = request.Phase == GameWalletSettlementPhase.CompleteReservation ? GameWalletSettlementIdentity.CreateCompletionId("hi-lo", accountId, request.OperationId)
                : GameWalletSettlementIdentity.CreateId("hi-lo", accountId, request.OperationId);
            if (receipts.TryGetValue(id, out var prior))
            {
                if (prior.Request != request) throw new GameWalletConflictException();
                return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
            }
            if (request.Phase == GameWalletSettlementPhase.CompleteReservation &&
                (!receipts.TryGetValue(GameWalletSettlementIdentity.CreateId("hi-lo", accountId, request.OperationId), out var hold) ||
                 hold.Request.Phase != GameWalletSettlementPhase.Reserve || hold.Receipt.InsufficientFunds || hold.Request.OutcomeSha256 != request.ReservationSha256)) throw new GameWalletConflictException();
            var before = Balance;
            var insufficient = Balance < request.WagerCents;
            var afterWager = insufficient ? Balance : checked(Balance - request.WagerCents);
            Balance = insufficient ? Balance : checked(afterWager + request.PayoutCents);
            var debit = !insufficient && request.WagerCents > 0;
            var credit = !insufficient && request.PayoutCents > 0;
            if (debit) Debits++;
            if (credit) Credits++;
            var receipt = new GameWalletSettlementReceipt(id, debit ? id + "-wager" : null, credit ? id + "-payout" : null,
                before, afterWager, Balance, DateTimeOffset.UtcNow, false, insufficient);
            receipts.Add(id, (request, receipt));
            if (LoseNextReply == request.Phase) { LoseNextReply = null; throw new GameWalletUnavailableException(); }
            return Task.FromResult(receipt);
        }
    }
}
