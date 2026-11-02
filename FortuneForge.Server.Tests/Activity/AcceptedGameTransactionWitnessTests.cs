using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.AndarBahar;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.DragonTiger;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Games.Keno;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Games.SicBo;
using FortuneForge.Games.Snake;
using FortuneForge.Games.Solitaire;
using FortuneForge.Games.TexasHoldem;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.Dice.SicBo;
using FortuneForge.Server.Numbers.Keno;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

public sealed class AcceptedGameTransactionWitnessTests
{
    internal static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Pure_builder_accepts_exactly_the_canonical_catalogue_and_preserves_the_owner_key_contract()
    {
        foreach (var game in AdminManagementCatalog.Games)
        {
            var witness = AcceptedGameParticipationDocuments.Create("alice", game.Id, Now);
            Assert.Equal(AcceptedGameActivityReader.Hash("alice"), witness.DocOwnerKey);
            Assert.Equal(game.Id, witness.GameId);
            Assert.Equal(4, witness.Fields.Count);
            Assert.Equal(true, witness.Fields["hasPlayed"]);
            Assert.Equal(1L, witness.Fields["experienceSchemaVersion"]);
            Assert.Equal("canonical-game-commit-v1", witness.Fields["experienceCaptureVersion"]);
            Assert.Equal(Now, witness.Fields["experienceObservedAt"]);
            Assert.DoesNotContain("playedAt", witness.Fields.Keys);
            Assert.DoesNotContain("experienceProvenance", witness.Fields.Keys);
            Assert.DoesNotContain("firstPlayedAtUtc", witness.Fields.Keys);
            Assert.True(Assert.Single(FirestorePlayerGameExperienceStore.ReadActivity(game.Id, witness.Fields)).GameId == game.Id);
        }
        Assert.NotEqual(AcceptedGameParticipationDocuments.Create("alice", "mines", Now).DocOwnerKey,
            AcceptedGameParticipationDocuments.Create("bob", "mines", Now).DocOwnerKey);
        Assert.Throws<ArgumentException>(() => AcceptedGameParticipationDocuments.Create("alice", "invented", Now));
        Assert.Throws<ArgumentException>(() => AcceptedGameParticipationDocuments.Create("alice", "wukong-journey-v1", Now));
        Assert.Throws<ArgumentException>(() => AcceptedGameParticipationDocuments.Create("alice", "mines", DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => AcceptedGameParticipationDocuments.Create("alice\n", "mines", Now));
    }

    [Fact]
    public void All_slot_ids_are_excluded_from_server_transaction_eligibility_even_with_positive_state()
    {
        foreach (var slot in AcceptedGameActivityReader.SlotIds.Values)
            Assert.Empty(Players(slot, new { userId = "alice", totalStake = 1, wagerCents = 100 }, "round-settled"));
        Assert.Empty(Players("invented", new { userId = "alice", wagerCents = 100 }, "round-settled"));
    }

    [Fact]
    public void Actual_opening_deals_count_before_CasinoWar_decision_VideoPoker_draw_and_HouseHoldem_action()
    {
        var aceClubs = new PlayingCard(CardRank.Ace, CardSuit.Clubs);
        var aceDiamonds = new PlayingCard(CardRank.Ace, CardSuit.Diamonds);
        var warDeck = new[] { aceClubs, aceDiamonds }.Concat(StandardDeck.Create()
            .Where(card => card != aceClubs && card != aceDiamonds)).Take(10).ToArray();
        var war = CasinoWarRoundEngine.Start(warDeck, 1m);
        Assert.Equal(CasinoWarRoundPhase.AwaitingTieDecision, war.Phase);
        Assert.Equal(["alice"], Players("casino-war", new CasinoWarStoreRound("war", "alice", war, null, warDeck), "opening"));

        var video = VideoPokerRoundEngine.Deal(StandardDeck.Create(), 1);
        Assert.Empty(video.Results);
        Assert.Equal(["alice"], Players("video-poker", video, "deal"));
        var house = HouseHoldemRules.StartRound(StandardDeck.Create().Select(CardCode.Format).ToArray(), 100);
        Assert.Null(house.Settlement);
        Assert.Equal(["alice"], Players("house-holdem", new { round = house, deck = StandardDeck.Create(), steps = Array.Empty<string>() }, "deal"));

        var blackjack = BlackjackRules.Deal("blackjack", "alice", 100,
            StandardDeck.Create().Select(CardCode.Format).ToArray(), Now);
        Assert.Equal(["alice"], Players("blackjack", blackjack, "deal", ["alice", "observer"]));
        Assert.Empty(Players("casino-war", new { userId = "alice", round = new { primaryStake = 0 } }, "opening"));
        Assert.Empty(Players("video-poker", new { coinsWagered = 0, handCount = 1, initialDeal = new { cards = new[] { 1, 2, 3, 4, 5 } } }, "deal"));
        Assert.Empty(Players("house-holdem", new { round = new { anteCents = 100, playerCards = Array.Empty<string>() } }, "deal"));
    }

    [Fact]
    public void Actual_instant_settlement_models_count_only_their_owner()
    {
        var round = PuntoBancoRoundDealer.Deal(StandardDeck.Create());
        var baccarat = new BaccaratStoreRound("baccarat", "alice", BaccaratBetSide.Player, round,
            PuntoBancoPaytable.Settle(BaccaratBetSide.Player, 1m, round),
            PuntoBancoPaytable.SettlePair(0m, round.PlayerHand), PuntoBancoPaytable.SettlePair(0m, round.BankerHand));
        var keno = new KenoStoreRound("keno", "alice", new KenoTicket([1]),
            new KenoDraw(Enumerable.Range(1, 20)), 1, 100, 250, "fortuneforge-keno-v3");
        var sicBo = new SicBoStoreRound("sic", "alice", [new("small", 100, null, null, null, null)], new SicBoRoll(1, 2, 3));
        foreach (var (game, state) in new (string, object)[] { ("baccarat", baccarat), ("keno", keno), ("sic-bo", sicBo) })
        {
            Assert.Equal(["alice"], Players(game, state, "round-settled", ["alice", "observer"]));
            Assert.Empty(Players(game, state, "read", ["alice"]));
            Assert.Empty(Players(game, state, "round-settled", ["bob"]));
        }
    }

    [Fact]
    public void Blackjack_table_refundable_next_wager_and_waiting_observer_do_not_count_but_actual_dealt_human_does()
    {
        var alice = BlackjackSeat("alice", 0, 100);
        var table = new BlackjackTableState { TableId = "table", CreatedAtUtc = Now, UpdatedAtUtc = Now,
            Players = [alice, BlackjackSeat("observer", 1, 0), BlackjackSeat("managed", 2, 100)] };
        Assert.Empty(Players("blackjack", table, "engine-state", ["alice", "observer"]));
        alice.NextWagerCents = 0; // A cancelled reservation never became a dealt hand.
        Assert.Empty(Players("blackjack", table, "engine-state", ["alice", "observer"]));
        alice.NextWagerCents = 100;
        BlackjackTableEngine.Deal(table, StandardDeck.Create().Select(CardCode.Format).ToArray(), 42, Now);
        Assert.Equal(["alice"], Players("blackjack", table, "engine-state", ["alice", "observer"]));
    }

    [Fact]
    public void ThreeCardPoker_actual_deal_excludes_waiting_seats_and_cancelled_predeal_wagers()
    {
        var alice = ThreeCardSeat("alice", 0, 100);
        var table = new ThreeCardPokerTableState { TableId = "table", CreatedAtUtc = Now, UpdatedAtUtc = Now,
            Players = [alice, ThreeCardSeat("observer", 1, 0)] };
        Assert.Empty(Players("three-card-poker", table, "engine-state", ["alice", "observer"]));
        alice.NextWagerCents = 0;
        Assert.Empty(Players("three-card-poker", table, "engine-state", ["alice", "observer"]));
        alice.NextWagerCents = 100;
        ThreeCardPokerTableEngine.Deal(table, ThreeCardPokerRules.CreateShuffledDeck(), 42, Now);
        Assert.Equal(["alice"], Players("three-card-poker", table, "engine-state", ["alice", "observer"]));
    }

    [Fact]
    public void Texas_only_account_backed_dealt_players_count_not_new_reserved_seats_or_house_players()
    {
        var match = CreditHoldemEngine.Deal("match", new[]
        {
            new CreditHoldemSeatAssignment("alice", "seat-a", "Alice", true, 0, 5000),
            new CreditHoldemSeatAssignment("bob", "seat-b", "Bob", true, 1, 5000),
            new CreditHoldemSeatAssignment("house", "seat-h", "House", false, 2, 5000),
        }, "partition", 42, Now);
        match.Players.Add(new CreditHoldemPlayer { ActorId = "reserved", PublicSeatId = "seat-r", DisplayName = "Reserved",
            IsAccountBacked = true, Seat = 3, StartingStack = 5000, Stack = 5000, HoleCards = [] });
        Assert.Equal(["alice", "bob"], Players("texas-holdem", match, "seat-reserved", ["alice", "bob", "reserved", "house"]));
        match.Players[0].HoleCards.Clear();
        Assert.Equal(["bob"], Players("texas-holdem", match, "seat-cancelled", ["alice", "bob", "reserved", "house"]));
    }

    [Fact]
    public void Paid_Hearts_requires_a_real_dealt_match_and_excludes_queue_cancel_and_managed_players()
    {
        var match = new FortuneForge.Server.Games.Hearts.PaidHeartsMatch { Id = "hearts", EntryCents = 100,
            Players = [new() { UserId = "alice", IsAccountBacked = true }, new() { UserId = "house", IsAccountBacked = false }] };
        Assert.Empty(Players("hearts", match, "engine-state", ["alice", "house"]));
        match.State = HeartsMatchEngine.Start(42);
        Assert.Empty(Players("hearts", match, "engine-state", ["alice", "house"]));
        match.Status = "match";
        Assert.Equal(["alice"], Players("hearts", match, "engine-state", ["alice", "house"]));
        match.Status = "cancelled";
        Assert.Empty(Players("hearts", match, "engine-state", ["alice", "house"]));
    }

    [Fact]
    public void Solitaire_requires_a_paid_owned_deal_or_genuine_moves_while_free_issuance_and_queues_do_not_count()
    {
        var initial = SolitaireEngine.CreateGame(42, 3);
        var alice = new SolitairePlayerState("match", "alice", "Alice", 0, SolitairePlayerStatuses.Playing,
            initial, 1, null, null, 0, false);
        Assert.Empty(Players("solitaire", new { players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { players = new[] { alice } }, "payout-claimed"));
        var paidMatch = new SolitaireMatch("match", 4, 500, 500, 450, 50, 42, Now,
            Now.AddMinutes(10), "playing", ["alice"], ["Alice"], ["ticket-alice"], [Now], null, null)
        { HumanEntryCents = 500, FinancialModel = "house-funded-bot-pool-v1" };
        Assert.Equal(["alice"], Players("solitaire", new { match = paidMatch, players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("free-solitaire", new { match = paidMatch, players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch with { Status = "waiting" }, players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch with { Status = "cancelled" }, players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch with { BuyInCents = 0 }, players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch with { HumanEntryCents = 0 }, players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch with { TicketIds = [""] }, players = new[] { alice } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch, players = new[] { alice with { MatchId = "other" } } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch, players = new[] { alice with { IsAccountBacked = false } } }, "human-entry"));
        Assert.Empty(Players("solitaire", new { match = paidMatch, players = new[] { alice with { Game = initial with { Stock = [] } } } }, "human-entry"));
        var moved = SolitaireEngine.Apply(initial, new(SolitaireCommandTypes.Draw, 1, null, null, null, null));
        var participants = new[] { alice with { Game = moved }, alice with { UserId = "bob" },
            alice with { UserId = "house", Game = moved, IsAccountBacked = false } };
        Assert.Equal(["alice"], Players("solitaire", new { players = participants }, "human-command", ["alice", "bob", "house"]));
        Assert.Empty(Players("free-solitaire", new { game = initial }, "free-replay-complete"));
        Assert.Equal(["alice"], Players("free-solitaire", new { game = moved }, "free-replay-complete"));
    }

    [Fact]
    public void Snake_requires_accepted_movement_and_excludes_seed_turn_before_first_tick_noop_and_reset()
    {
        var initial = SnakeEngine.Start(42, 20, 20);
        Assert.Empty(Players("snake", new { userId = "alice", state = initial }, "started"));
        Assert.Empty(Players("snake", new { userId = "alice", state = initial }, "turned"));
        var moved = SnakeEngine.Tick(initial).State;
        Assert.True(moved.Moves > 0);
        Assert.Equal(["alice"], Players("snake", new { userId = "alice", state = moved }, "moved", ["alice", "observer"]));
        Assert.Empty(Players("snake", new { userId = "alice", state = moved }, "no-op"));
        Assert.Empty(Players("snake", new { userId = "alice", state = moved }, "reset-abandoned"));
    }

    [Theory]
    [InlineData("asteroids")]
    [InlineData("flappy")]
    [InlineData("horse-flight")]
    public void Arcade_issued_free_seed_does_not_count_paid_admission_and_verified_zero_score_completion_do(string game)
    {
        Assert.Empty(Players(game, new { runId = "run", seed = 42 }, "run-started"));
        Assert.Empty(Players(game, new { runId = "run", attemptId = "attempt", entryFeeCents = 0 }, "run-started"));
        Assert.Equal(["alice"], Players(game, new { runId = "run", attemptId = "attempt", entryFeeCents = 100 }, "run-started"));
        Assert.Empty(Players(game, new { runId = "run", score = 0, terminal = "running" }, "run-completed"));
        Assert.Equal(["alice"], Players(game, new { runId = "run", score = 0, terminal = "fell" }, "run-completed"));
    }

    [Fact]
    public void Paid_Craps_only_counts_the_actual_positive_bet_owner_and_demo_requests_are_excluded()
    {
        var state = new { totalWagerCents = 100, state = new { bet = new { playerId = "alice", stake = 1m } } };
        Assert.Equal(["alice"], Players("craps", state, "started", ["alice", "observer"]));
        Assert.Empty(Players("craps", new { totalWagerCents = 0, state = new { bet = new { playerId = "alice", stake = 0 } } }, "started"));
        using var scope = GameSecurityContext.Push(new("request", "alice", null, null, Now,
            "POST", "/api/demo/games/craps/rounds", "craps", null, null, null, null, "empty", "none"));
        Assert.Empty(Players("craps", state, "started"));
    }

    private static BlackjackTablePlayer BlackjackSeat(string id, int seat, long nextWager) => new()
    { ActorId = id, PublicSeatId = "seat-" + id, DisplayName = id, Seat = seat, SessionId = "session-" + id, SessionStartedAtUtc = Now, NextWagerCents = nextWager };
    private static ThreeCardPokerTablePlayer ThreeCardSeat(string id, int seat, long nextWager) => new()
    { ActorId = id, PublicSeatId = "seat-" + id, DisplayName = id, Seat = seat, SessionId = "session-" + id, SessionStartedAtUtc = Now, NextWagerCents = nextWager };
    private static IReadOnlyList<string> Players(string game, object state, string kind, string[]? actors = null) =>
        AcceptedGameTransactionWitness.Players(GameSecurityEventDocuments.Capture(game, "instance", 1, Now, state, actors ?? ["alice"], kind));
}

[Collection("Blackjack table Firestore emulator")]
public sealed class AcceptedGameTransactionWitnessFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static DateTime Now => AcceptedGameTransactionWitnessTests.Now;

    [Fact]
    public async Task Competitive_Solitaire_paid_zero_move_join_witness_commits_with_deal_and_debit_and_retry_is_exact()
    {
        var database = Database(); var wallet = database.Collection("userBalances").Document("alice_slotsCredits");
        await wallet.SetAsync(new Dictionary<string, object> { ["available"] = 100L, ["availableFractionalCents"] = 0L, ["version"] = 1L });
        var store = new FirestoreCompetitiveSolitaireStore(database);
        const string key = "accepted-solitaire-join-0001";
        var result = await store.JoinAsync("alice", "Alice", 4, 500, 3, key, 1001, Now, default);
        var session = Assert.IsType<SolitaireMatchSessionResponse>(result.Session);
        Assert.Equal(SolitaireSessionKinds.Match, session.Kind);
        Assert.Equal(0, session.Game.Moves);
        var saved = await Witness(database, "alice", "solitaire").GetSnapshotAsync();
        Assert.True(saved.GetValue<bool>("hasPlayed"));
        Assert.Equal((await wallet.GetSnapshotAsync()).UpdateTime, saved.UpdateTime);
        var debit = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal("solitaire-buyin", debit.GetValue<string>("type"));
        Assert.Equal(saved.UpdateTime, debit.UpdateTime);
        var dealt = Assert.Single((await database.Collection("solitaireMatchPlayers").GetSnapshotAsync()).Documents);
        Assert.Equal(saved.UpdateTime, dealt.UpdateTime);
        var retried = await store.JoinAsync("alice", "Alice", 4, 500, 3, key, 1001, Now.AddSeconds(1), default);
        Assert.Equal(session.MatchId, Assert.IsType<SolitaireMatchSessionResponse>(retried.Session).MatchId);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.False((await Witness(database, "bob", "solitaire").GetSnapshotAsync()).Exists);
        await database.Collection("userBalances").Document("poor_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 0L, ["availableFractionalCents"] = 0L, ["version"] = 1L });
        await Assert.ThrowsAsync<SolitaireInsufficientCreditsException>(() =>
            store.JoinAsync("poor", "Poor", 4, 500, 3, "accepted-solitaire-poor-0001", 1001, Now, default));
        Assert.False((await Witness(database, "poor", "solitaire").GetSnapshotAsync()).Exists);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Journal_witness_and_state_commit_together_and_a_rejected_transaction_creates_neither()
    {
        var database = Database();
        var state = database.Collection("testAcceptedState").Document("round");
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync<int>(transaction =>
        {
            GameSecurityJournal.Write(transaction, database, "keno", "round", 1, Now,
                new { userId = "alice", wagerCents = 100 }, ["alice"], "round-settled");
            transaction.Create(state, new Dictionary<string, object> { ["accepted"] = true });
            throw new InvalidOperationException("Reject before commit");
        }));
        Assert.False((await Witness(database, "alice", "keno").GetSnapshotAsync()).Exists);
        Assert.False((await state.GetSnapshotAsync()).Exists);
        await database.RunTransactionAsync(transaction =>
        {
            GameSecurityJournal.Write(transaction, database, "keno", "round", 1, Now,
                new { userId = "alice", wagerCents = 100 }, ["alice"], "round-settled");
            transaction.Create(state, new Dictionary<string, object> { ["accepted"] = true });
            return Task.FromResult(true);
        });
        var saved = await Witness(database, "alice", "keno").GetSnapshotAsync();
        Assert.True(saved.GetValue<bool>("hasPlayed"));
        Assert.Equal((await state.GetSnapshotAsync()).UpdateTime, saved.UpdateTime);
        Assert.False((await Witness(database, "bob", "keno").GetSnapshotAsync()).Exists);
    }

    [Fact]
    public async Task Positive_metadata_merges_preserve_existing_provenance_recency_and_private_evidence_hashes()
    {
        var database = Database(); var witness = Witness(database, "alice", "keno");
        await witness.SetAsync(new Dictionary<string, object> { ["experienceProvenance"] = new[] { "legacy-outbox" },
            ["playedAt"] = Timestamp.FromDateTime(Now.AddDays(-1)) });
        var observation = GameSecurityEventDocuments.Capture("keno", "round", 1, Now,
            new { userId = "alice", wagerCents = 100 }, ["alice"], "round-settled");
        await database.RunTransactionAsync(transaction =>
        {
            GameSecurityJournal.Write(transaction, database, "keno", "round", 1, Now,
                new { userId = "alice", wagerCents = 100 }, ["alice"], "round-settled");
            return Task.FromResult(true);
        });
        var saved = await witness.GetSnapshotAsync();
        Assert.Equal(["legacy-outbox"], saved.GetValue<string[]>("experienceProvenance"));
        Assert.Equal(Now.AddDays(-1), saved.GetValue<Timestamp>("playedAt").ToDateTime());
        Assert.Equal(AcceptedGameParticipationDocuments.CaptureVersion, saved.GetValue<string>("experienceCaptureVersion"));
        var evidence = await database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id).GetSnapshotAsync();
        Assert.Equal(observation.Fields["sha256"], evidence.GetValue<string>("sha256"));
        Assert.Equal(observation.Fields["payloadJson"], evidence.GetValue<string>("payloadJson"));
        Assert.Equal(saved.UpdateTime, evidence.UpdateTime);
        Assert.False((await database.Collection("catalogGamePopularity").Document("keno").GetSnapshotAsync()).Exists);
    }

    [Theory]
    [InlineData("dragon-tiger", "dragonTigerRounds")]
    [InlineData("andar-bahar", "andarBaharRounds")]
    [InlineData("hi-lo", "hiLoPlayers")]
    [InlineData("mines", "minesRounds")]
    public async Task Packaged_paid_start_captures_in_the_same_wallet_commit_before_any_later_action(string game, string source)
    {
        var database = Database(); var wallet = database.Collection("userBalances").Document("alice_slotsCredits");
        await wallet.SetAsync(new Dictionary<string, object> { ["available"] = 100L, ["availableFractionalCents"] = 0L, ["version"] = 1L });
        switch (game)
        {
            case "dragon-tiger":
                await new DragonTigerFirestoreStore(database).StartAsync("alice", "accepted-package-start-0001", new(100, 0, 0),
                    Enumerable.Range(0, 8).SelectMany(_ => StandardDeck.Create()).ToArray(), new(Now), default);
                break;
            case "andar-bahar":
                await new AndarBaharFirestoreStore(database).StartAsync("alice", "accepted-package-start-0001", AndarBaharSide.Andar,
                    100, StandardDeck.Create(), new(Now), default);
                break;
            case "hi-lo":
                await new HiLoService(new HiLoFirestoreStore(database), draw: () => new(CardRank.Seven, CardSuit.Clubs))
                    .StartAsync("alice", new("accepted-package-start-0001", 100));
                break;
            case "mines":
                await new MinesService(new FirestoreMinesStore(database)).StartAsync("alice", new("accepted-package-start-0001", 100, 3));
                break;
        }
        var saved = await Witness(database, "alice", game).GetSnapshotAsync();
        Assert.True(saved.GetValue<bool>("hasPlayed"));
        Assert.Equal((await wallet.GetSnapshotAsync()).UpdateTime, saved.UpdateTime);
        var engine = Assert.Single((await database.Collection(source).GetSnapshotAsync()).Documents);
        Assert.Equal(engine.UpdateTime, saved.UpdateTime);
        Assert.False((await Witness(database, "bob", game).GetSnapshotAsync()).Exists);
        if (game is "hi-lo" or "mines")
            Assert.Empty((await database.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
    }

    private static DocumentReference Witness(FirestoreDb database, string user, string game) => database.Collection("catalogPlayerActivity")
        .Document(AcceptedGameActivityReader.Hash(user)).Collection("games").Document(game);
    private FirestoreDb Database() => new FirestoreDbBuilder { ProjectId = "demo-accepted-" + Guid.NewGuid().ToString("N"),
        Endpoint = fixture.Endpoint, ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
}
