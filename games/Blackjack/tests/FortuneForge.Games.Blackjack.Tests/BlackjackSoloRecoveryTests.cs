using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Abstractions.Auditing;
using System.Text.Json;
using FortuneForge.Games.Blackjack;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Blackjack.Tests;

[Collection("Blackjack table Firestore emulator")]
public sealed class BlackjackSoloRecoveryTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("natural", 100, 250)]
    [InlineData("double", 200, 400)]
    [InlineData("bust", 100, 0)]
    public async Task Terminal_round_has_one_verified_history_record_and_replayable_evidence(string scenario, long wager, long payout)
    {
        var db = Database(); var wallet = new Wallet(); var store = new BlackjackSoloStore(db, wallet, wallet);
        var deck = scenario switch
        {
            "natural" => Deck("A|spades", "9|clubs", "K|hearts", "7|diamonds"),
            "double" => Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "10|clubs", "6|clubs"),
            _ => Deck("10|spades", "9|clubs", "9|hearts", "7|diamonds", "K|clubs")
        };
        var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.StartAsync("alice", "start-key", 100, deck, Now, default)));
        var game = starts[0].Game;
        if (scenario != "natural")
        {
            Assert.Empty((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
            var action = scenario == "double" ? BlackjackActions.Double : BlackjackActions.Hit;
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.ActAsync("alice", game.GameId, "action-key", game.Version, action, Now.AddSeconds(5), default)));
        }
        var outbox = Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        Assert.Equal(wager, outbox.GetValue<long>("wagerCents")); Assert.Equal(payout, outbox.GetValue<long>("payoutCents"));
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        var evidence = Assert.Single((await db.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        using var payload = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        Assert.Equal(52, payload.RootElement.GetProperty("data").GetProperty("game").GetProperty("deck").GetArrayLength());
        var proof = payload.RootElement.GetProperty("financialProof");
        Assert.Equal(BlackjackBackend.Owner, proof.GetProperty("backendOwner").GetString());
        Assert.Equal("signed-wallet", proof.GetProperty("provenance").GetString());
        var entries = proof.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(wager, -entries.Where(entry => entry.GetProperty("amountCents").GetInt64() < 0).Sum(entry => entry.GetProperty("amountCents").GetInt64()));
        Assert.Equal(payout, entries.Where(entry => entry.GetProperty("amountCents").GetInt64() > 0).Sum(entry => entry.GetProperty("amountCents").GetInt64()));
        Assert.All(entries, entry => { Assert.True(entry.GetProperty("evidenceAvailable").GetBoolean()); Assert.NotEmpty(entry.GetProperty("documentId").GetString()!); });
        Assert.Equal(scenario == "natural" ? 1 : 2, proof.GetProperty("walletOperations").GetArrayLength());
        if (scenario == "double") Assert.Contains(entries, entry => entry.GetProperty("kind").GetString() == "blackjack-double");
        var recovered = await store.GetAsync("alice", game.GameId, default);
        Assert.Equal(10000 - wager + payout, recovered!.BalanceCents);
        Assert.True(BlackjackSoloReplay.Verify(recovered.Game));
    }

    [Fact]
    public async Task Lost_deal_response_recovers_frozen_deck_and_charges_once_after_restart()
    {
        var db = Database(); var wallet = new Wallet { LoseNextReply = true };
        var store = new BlackjackSoloStore(db, wallet, wallet);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.StartAsync("alice", "start-key", 100,
            Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "10|clubs", "6|clubs"), Now, default));
        var id = BlackjackSoloStore.Hash("alice\nstart-key");
        var recovered = await new BlackjackSoloStore(db, wallet, wallet).GetAsync("alice", id, default);
        Assert.Equal(9900, recovered!.BalanceCents);
        Assert.Equal(new[] { "5|spades", "6|hearts" }, recovered.Game.PlayerCards);
        var retry = await store.StartAsync("alice", "start-key", 100, BlackjackRules.CreateShuffledDeck(), Now.AddHours(1), default);
        Assert.Equal(recovered.Game.Deck, retry.Game.Deck);
        Assert.Equal(1, wallet.Applied);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Lost_double_response_and_concurrent_retries_settle_once_and_keep_original_reply()
    {
        var db = Database(); var wallet = new Wallet(); var store = new BlackjackSoloStore(db, wallet, wallet);
        var dealt = await store.StartAsync("alice", "start-key", 100,
            Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "10|clubs", "6|clubs"), Now, default);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.ActAsync("alice", dealt.Game.GameId,
            "double-key", 1, "double", Now.AddSeconds(1), default));
        var recovered = await new BlackjackSoloStore(db, wallet, wallet).GetAsync("alice", dealt.Game.GameId, default);
        Assert.Equal(BlackjackStatuses.Completed, recovered!.Game.Status);
        Assert.Equal(10200, recovered.BalanceCents);
        var retries = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.ActAsync("alice", dealt.Game.GameId,
            "double-key", 1, "double", Now.AddHours(1), default)));
        Assert.All(retries, result => Assert.Equal(JsonSerializer.Serialize(recovered.Game), JsonSerializer.Serialize(result.Game)));
        Assert.Equal(2, wallet.Applied);
        var startRetry = await store.StartAsync("alice", "start-key", 100, BlackjackRules.CreateShuffledDeck(), Now, default);
        Assert.Equal(1, startRetry.Game.Version);
        Assert.Equal(9900, startRetry.BalanceCents);
        Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<BlackjackConflictException>(() => store.ActAsync("alice", dealt.Game.GameId,
            "double-key", 2, "double", Now, default));
    }

    [Fact]
    public async Task Finalized_action_replay_does_not_depend_on_a_newer_pending_wallet_action()
    {
        var db = Database(); var wallet = new Wallet(); var store = new BlackjackSoloStore(db, wallet, wallet);
        var dealt = await store.StartAsync("alice", "start-key", 100,
            Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "3|clubs", "6|clubs"), Now, default);
        var hit = await store.ActAsync("alice", dealt.Game.GameId, "hit-key", 1, "hit", Now.AddSeconds(1), default);
        wallet.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.ActAsync("alice", dealt.Game.GameId,
            "stand-key", hit.Game.Version, "stand", Now.AddSeconds(2), default));
        var applied = wallet.Applied;
        wallet.Unavailable = true;
        var replay = await new BlackjackSoloStore(db, wallet, wallet).ActAsync("alice", dealt.Game.GameId,
            "hit-key", 1, "hit", Now.AddHours(1), default);
        Assert.Equal(JsonSerializer.Serialize(hit), JsonSerializer.Serialize(replay));
        await Assert.ThrowsAsync<BlackjackConflictException>(() => store.ActAsync("alice", dealt.Game.GameId,
            "hit-key", 2, "hit", Now.AddHours(1), default));
        Assert.Equal(applied, wallet.Applied);
        Assert.Equal(BlackjackStatuses.Active, replay.Game.Status);
        var pending = await db.Collection(BlackjackBackend.SoloGames).Document(dealt.Game.GameId).GetSnapshotAsync();
        Assert.NotEmpty(pending.GetValue<string>("pendingWalletCommand"));
    }

    [Fact]
    public async Task Declined_double_keeps_hand_playable_and_its_decline_is_immutable()
    {
        var db = Database(); var wallet = new Wallet { Balance = 150 }; var store = new BlackjackSoloStore(db, wallet, wallet);
        var dealt = await store.StartAsync("alice", "start-key", 100,
            Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "10|clubs", "6|clubs"), Now, default);
        await Assert.ThrowsAsync<BlackjackInsufficientCreditsException>(() => store.ActAsync("alice", dealt.Game.GameId,
            "double-key", 1, "double", Now.AddSeconds(1), default));
        wallet.Balance = 5000;
        await Assert.ThrowsAsync<BlackjackInsufficientCreditsException>(() => store.ActAsync("alice", dealt.Game.GameId,
            "double-key", 1, "double", Now.AddSeconds(2), default));
        var recovered = await store.GetAsync("alice", dealt.Game.GameId, default);
        Assert.Equal(1, recovered!.Game.Version);
        Assert.Equal(BlackjackStatuses.Active, recovered.Game.Status);
        Assert.Null(await store.GetAsync("bob", dealt.Game.GameId, default));
        await Assert.ThrowsAsync<BlackjackConflictException>(() => store.ActAsync("alice", dealt.Game.GameId,
            "stand-key", 2, "stand", Now, default));
        var stood = await store.ActAsync("alice", dealt.Game.GameId, "stand-key", 1, "stand", Now.AddSeconds(3), default);
        Assert.Equal(BlackjackStatuses.Completed, stood.Game.Status);
        Assert.Equal(5200, wallet.Balance);
    }

    [Fact]
    public async Task Historical_active_hand_retains_deck_and_paid_wager_and_historical_action_retries_do_not_pay_again()
    {
        var db = Database(); var wallet = new Wallet { Balance = 9900 }; var store = new BlackjackSoloStore(db, wallet, wallet);
        const string user = "alice"; const string key = "historical-key";
        var id = BlackjackSoloStore.Hash($"{user}\n{key}");
        var legacy = BlackjackRules.Deal(id, user, 100,
            Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "10|clubs", "6|clubs"), Now);
        await db.Collection("blackjackGames").Document(id).CreateAsync(BlackjackSoloCodec.Create(legacy, key));
        var replay = await store.StartAsync(user, key, 100, BlackjackRules.CreateShuffledDeck(), Now, default);
        Assert.Equal(legacy.Deck, replay.Game.Deck);
        Assert.Equal(0, wallet.Applied);
        var completed = await store.ActAsync(user, id, "finish-old-hand", 1, "double", Now.AddSeconds(1), default);
        Assert.Equal(10200, completed.BalanceCents);
        Assert.Equal(1, wallet.Applied);
        var evidence = Assert.Single((await db.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        using var payload = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        var proof = payload.RootElement.GetProperty("financialProof");
        Assert.Equal("mixed-legacy-and-signed-wallet", proof.GetProperty("provenance").GetString());
        Assert.Contains(proof.GetProperty("entries").EnumerateArray(), entry => !entry.GetProperty("evidenceAvailable").GetBoolean());
        var audit = Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Equal("incomplete", audit.GetValue<string>("auditStatus"));
        // A truly legacy completed action predates the new wallet command collection.
        await db.Collection(BlackjackSoloStore.Commands).Document(BlackjackSoloStore.Hash($"{user}\naction\nfinish-old-hand")).DeleteAsync();
        var oldRetry = await store.ActAsync(user, id, "finish-old-hand", 1, "double", Now.AddHours(1), default);
        Assert.Equal(completed.Game.Deck, oldRetry.Game.Deck);
        Assert.Equal(1, wallet.Applied);
    }

    [Fact]
    public async Task Paused_admissions_allow_exact_retries_and_existing_hand_completion()
    {
        var db = Database(); var wallet = new Wallet(); var allowed = true;
        var store = new BlackjackSoloStore(db, wallet, wallet, () => allowed);
        var start = await store.StartAsync("alice", "start-key", 100,
            Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds", "10|clubs", "6|clubs"), Now, default);
        allowed = false;
        await store.StartAsync("alice", "start-key", 100, BlackjackRules.CreateShuffledDeck(), Now, default);
        await Assert.ThrowsAsync<BlackjackAdmissionPausedException>(() => store.StartAsync("alice", "new-key", 100,
            BlackjackRules.CreateShuffledDeck(), Now, default));
        var finish = await store.ActAsync("alice", start.Game.GameId, "finish-key", 1, "stand", Now.AddSeconds(1), default);
        Assert.Equal(BlackjackStatuses.Completed, finish.Game.Status);
    }

    [Fact]
    public async Task Recovery_keeps_original_signed_request_evidence_and_rejects_corrupt_historical_payout()
    {
        var db = Database(); var wallet = new Wallet { LoseNextReply = true }; var store = new BlackjackSoloStore(db, wallet, wallet);
        using (GameSecurityContext.Push(new("original-request", "alice", null, null, Now, "POST", "/api/cards/blackjack/games",
            "blackjack", null, "start-key", null, null, "observed", "unknown")))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.StartAsync("alice", "start-key", 100,
                Deck("A|spades", "9|clubs", "K|hearts", "7|diamonds"), Now, default));
        var id = BlackjackSoloStore.Hash("alice\nstart-key");
        using (GameSecurityContext.Push(new("recovery-request", "alice", null, null, Now.AddSeconds(1), "GET", "/api/cards/blackjack/games/" + id,
            "blackjack", id, null, null, null, "observed", "unknown")))
            await store.GetAsync("alice", id, default);
        var observation = Assert.Single((await db.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
        Assert.Equal("original-request", observation.GetValue<string>("requestId"));
        await db.Collection("blackjackGames").Document(id).UpdateAsync(new Dictionary<string, object> { ["payoutCents"] = 999L });
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync("alice", id, default));
        Assert.Equal(1, wallet.Applied);
    }

    [Fact]
    public async Task Prepared_command_from_before_proof_extension_keeps_its_original_wallet_terms()
    {
        var db = Database(); var wallet = new Wallet { Unavailable = true };
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => new BlackjackSoloStore(db, wallet, wallet).StartAsync("alice", "open", 100,
            Deck("5|spades", "9|clubs", "6|hearts", "7|diamonds"), Now, default));
        var command = Assert.Single((await db.Collection(BlackjackBackend.SoloCommands).GetSnapshotAsync()).Documents);
        var json = System.Text.Json.Nodes.JsonNode.Parse(command.GetValue<string>("payloadJson"))!.AsObject();
        Assert.True(json.Remove("priorFinancialEvidence"));
        var original = json.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await command.Reference.UpdateAsync(new Dictionary<string, object> { ["payloadJson"] = original, ["sha256"] = BlackjackSoloStore.Hash(original) });
        wallet.Unavailable = false;
        var recovered = await new BlackjackSoloStore(db, wallet, wallet).StartAsync("alice", "open", 100, BlackjackRules.CreateShuffledDeck(), Now, default);
        Assert.Equal(9900, recovered.BalanceCents); Assert.Equal(1, wallet.Applied);
        Assert.Equal(new[] { "5|spades", "6|hearts" }, recovered.Game.PlayerCards);
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-blackjack-package-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();

    private static IReadOnlyList<string> Deck(params string[] prefix) => prefix.Concat(
        BlackjackRules.CreateShuffledDeck().Where(card => !prefix.Contains(card)).Order(StringComparer.Ordinal)).ToArray();

    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance { get; set; } = 10000;
        public bool LoseNextReply { get; set; }
        public bool Unavailable { get; set; }
        public int Applied { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(0, Balance));

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
        {
            lock (receipts)
            {
                if (Unavailable) throw new GameWalletUnavailableException();
                if (receipts.TryGetValue(request.OperationId, out var saved))
                {
                    if (saved.Request != request) throw new GameWalletConflictException();
                    return Task.FromResult(saved.Receipt with { WasAlreadySettled = true });
                }
                var before = Balance; var declined = before < request.WagerCents;
                var afterWager = declined ? before : before - request.WagerCents;
                Balance = declined ? before : afterWager + request.PayoutCents;
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("blackjack", accountId, request.OperationId),
                    !declined && request.WagerCents > 0 ? request.OperationId + "-wager" : null,
                    !declined && request.PayoutCents > 0 ? request.OperationId + "-payout" : null,
                    before, afterWager, Balance, new DateTimeOffset(Now), false, declined);
                receipts.Add(request.OperationId, (request, receipt)); Applied++;
                if (LoseNextReply) { LoseNextReply = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}
