using FortuneForge.Server.Activity;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using FortuneForge.Server.Tests.Catalog;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Craps;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Games.Roulette;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

[Collection("Blackjack table Firestore emulator")]
public sealed class PlayerGameExperienceFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
    private static FirestorePlayerGameExperienceStore Store(FirestoreDb database, IConfiguration? configuration = null, TimeProvider? clock = null) =>
        new(database, clock ?? new Clock(), NullLogger<FirestorePlayerGameExperienceStore>.Instance, configuration);

    [Fact]
    public async Task Positive_flag_is_durable_monotonic_idempotent_and_account_scoped()
    {
        var database = Database();
        using var first = Store(database);
        Assert.All((await first.ReadAsync("alice", default)).Games, game => Assert.Null(game.HasPlayed));
        var accepted = new AcceptedGameActivity("mines", "round", "receipt");
        await first.RecordAcceptedAsync("alice", accepted, default);
        await first.RecordAcceptedAsync("alice", accepted, default);
        using var restarted = Store(database);
        Assert.True(Game(await restarted.ReadAsync("alice", default), "mines").HasPlayed);
        var witness = database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("alice"))
            .Collection("games").Document("mines");
        var beforeRepeatedReads = await witness.GetSnapshotAsync();
        Assert.Null(Game(await restarted.ReadAsync("bob", default), "mines").HasPlayed);
        Assert.False((await database.Collection("catalogGamePopularity").Document("mines").GetSnapshotAsync()).Exists);
        Assert.True(Game(await restarted.ReadAsync("alice", default), "mines").HasPlayed);
        Assert.Equal(beforeRepeatedReads.UpdateTime, (await witness.GetSnapshotAsync()).UpdateTime);
    }

    [Fact]
    public async Task Experience_only_witness_does_not_invent_recency_and_genuine_activity_preserves_its_metadata()
    {
        var database = Database(); using var experience = Store(database);
        var activity = new FirestoreGameActivityStore(database);
        var accepted = new AcceptedGameActivity("craps", "round", "receipt");
        await experience.RecordAcceptedAsync("alice", accepted, default);
        Assert.Empty(await activity.ReadAsync("alice", default));
        await activity.RecordAsync("alice", accepted, Now, default);
        Assert.Equal("craps", Assert.Single(await activity.ReadAsync("alice", default)).GameId);
        var saved = await database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("alice"))
            .Collection("games").Document("craps").GetSnapshotAsync();
        Assert.True(saved.GetValue<bool>("hasPlayed"));
        Assert.Contains("accepted-participation", saved.GetValue<string[]>("experienceProvenance"));
        Assert.Equal(1L, (await database.Collection("catalogGamePopularity").Document("craps").GetSnapshotAsync()).GetValue<long>("playCount"));
    }

    [Fact]
    public async Task Bounded_legacy_pages_eventually_backfill_later_games_without_false_or_cross_account_evidence()
    {
        var database = Database(); var batch = database.StartBatch();
        for (var index = 0; index <= FirestorePlayerGameExperienceStore.LegacyPageSize; index++)
            batch.Set(database.Collection("gameAnalyticsSettlementOutbox").Document(index.ToString("D3")), new Dictionary<string, object>
            {
                ["userId"] = "alice", ["gameId"] = index == FirestorePlayerGameExperienceStore.LegacyPageSize ? "keno" : "baccarat",
                ["isHuman"] = true, ["mode"] = "paid", ["completedAt"] = Timestamp.FromDateTime(Now),
            });
        batch.Set(database.Collection("cardGameResults").Document("other-account"), new Dictionary<string, object>
            { ["userId"] = "bob", ["game"] = "house-holdem", ["startedAt"] = Timestamp.FromDateTime(Now) });
        await batch.CommitAsync();
        using var store = Store(database);
        var first = await store.ReadAsync("alice", default);
        Assert.True(Game(first, "baccarat").HasPlayed); Assert.Null(Game(first, "keno").HasPlayed);
        Assert.Null(Game(first, "house-holdem").HasPlayed);
        var account = database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("alice"));
        var progress = await account.GetSnapshotAsync();
        var witness = await account.Collection("games").Document("baccarat").GetSnapshotAsync();
        // One Firestore commit makes cursor advancement inseparable from its
        // positive witness; neither can survive a failed backfill transaction.
        Assert.Equal(progress.UpdateTime, witness.UpdateTime);
        Assert.Equal("031", progress.GetValue<string>("experienceBackfill.gameAnalyticsSettlementOutbox.cursor"));
        PlayerGameExperienceResponse next = first;
        for (var index = 0; index < (int)Math.Ceiling((double)FirestorePlayerGameExperienceStore.LegacySources.Count /
                 FirestorePlayerGameExperienceStore.LegacySourcesPerRead); index++) next = await store.ReadAsync("alice", default);
        Assert.True(Game(next, "keno").HasPlayed);
        Assert.All(next.Games, game => Assert.NotEqual(false, game.HasPlayed));
        using var restarted = Store(database);
        Assert.Contains("legacy-gameAnalyticsSettlementOutbox", Game(await restarted.ReadAsync("alice", default), "keno").Provenance);
        Assert.Null(Game(await restarted.ReadAsync("bob", default), "keno").HasPlayed);
    }

    [Fact]
    public async Task Legacy_runtime_slot_ids_cover_all_twenty_games_in_one_owner_scoped_page()
    {
        var database = Database(); var batch = database.StartBatch();
        foreach (var (runtime, canonical) in AcceptedGameActivityReader.SlotIds)
            batch.Set(database.Collection("slotSpinResults").Document(canonical), new Dictionary<string, object>
            { ["userId"] = "alice", ["gameId"] = runtime, ["spinId"] = "settled-" + canonical, ["createdAt"] = Timestamp.FromDateTime(Now) });
        batch.Set(database.Collection("slotSpinResults").Document("foreign"), new Dictionary<string, object>
            { ["userId"] = "bob", ["gameId"] = "invented", ["spinId"] = "invalid", ["createdAt"] = Timestamp.FromDateTime(Now) });
        await batch.CommitAsync();
        using var store = Store(database); var response = await store.ReadAsync("alice", default);
        var currentSlots = CatalogContract.Entries
            .Where(game => game.Route.StartsWith("/slots/", StringComparison.Ordinal))
            .Select(game => game.Id).ToArray();
        Assert.Equal(20, currentSlots.Length);
        Assert.Equal(20, response.Games.Count(game => game.HasPlayed == true));
        // Retired runtime IDs remain readable as legacy input, not current catalogue games.
        Assert.DoesNotContain(response.Games, game => game.GameId == "jungle-jackpot");
        foreach (var canonical in currentSlots)
        {
            var game = Game(response, canonical);
            Assert.True(game.HasPlayed); Assert.Equal("partial", game.Coverage); Assert.Null(game.FirstPlayedAtUtc);
            Assert.Contains("legacy-slotSpinResults", game.Provenance);
        }
    }

    [Fact]
    public async Task Fresh_attested_absence_proves_newness_without_reusing_old_negative_receipts_or_overwriting_positives()
    {
        var database = Database(); var now = DateTime.UtcNow;
        var activation = database.Collection(AuthoritativeGameExperienceCoverage.ActivationCollection)
            .Document(AuthoritativeGameExperienceCoverage.ActivationDocument);
        await activation.SetAsync(PlayerGameExperienceTests.ActiveCoverage(now));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["K_REVISION"] = "api-current", ["FF_RELEASE_SHA"] = new string('a', 40) }).Build();
        using var store = Store(database, configuration, TimeProvider.System);
        var first = await store.ReadAsync("alice", default);
        Assert.Equal(20, first.Games.Count(game => game.HasPlayed == false));
        Assert.All(first.Games.Where(game => game.HasPlayed == false), game =>
        { Assert.Equal("complete", game.Coverage); Assert.NotEmpty(game.Provenance); });
        var account = database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("alice"));
        var savedCoverage = await account.GetSnapshotAsync();
        Assert.Equal("epoch", savedCoverage.GetValue<string>("experienceCoverage.slotGuards.activationId"));
        Assert.True(savedCoverage.GetValue<Timestamp>("experienceCoverage.slotGuards.asOfUtc").ToDateTime() >= now.AddMinutes(-1));
        Assert.Empty((await account.Collection("games").GetSnapshotAsync()).Documents);

        // An admitted/failed or malformed guard is neither proof of settled
        // actual play nor proof of first-time status, including the old alias.
        var keys = AuthoritativeGameExperienceCoverage.GuardKeys("alice");
        await database.Collection("slotSpinGuards").Document(keys["wukong-journey-to-the-west"][1])
            .SetAsync(new Dictionary<string, object>
                { ["userId"] = "alice", ["activeAdmissionId"] = "pending", ["lastSpinStartedAt"] = Timestamp.FromDateTime(now) });
        await database.Collection("slotSpinGuards").Document(keys["pirates-fortune"][0])
            .SetAsync(new Dictionary<string, object> { ["userId"] = "mismatched-owner" });
        await store.RecordAcceptedAsync("alice", new("candy-carnival", "settled", "receipt"), default);
        var observed = await store.ReadAsync("alice", default);
        Assert.Null(Game(observed, "wukong-journey-to-the-west").HasPlayed);
        Assert.Null(Game(observed, "pirates-fortune").HasPlayed);
        Assert.True(Game(observed, "candy-carnival").HasPlayed);
        Assert.Equal(20, (await store.ReadAsync("bob", default)).Games.Count(game => game.HasPlayed == false));

        // Negative watermark receipts never write hasPlayed=false, including
        // when a genuine positive commits concurrently with a loading request.
        await Task.WhenAll(store.ReadAsync("alice", default),
            store.RecordAcceptedAsync("alice", new("nordic-legends", "settled", "receipt"), default));
        Assert.True((await account.Collection("games").Document("nordic-legends").GetSnapshotAsync()).GetValue<bool>("hasPlayed"));
        await activation.UpdateAsync("status", "paused");
        using var restarted = Store(database, configuration, TimeProvider.System);
        var gap = await restarted.ReadAsync("alice", default);
        Assert.DoesNotContain(gap.Games, game => game.HasPlayed == false);
        Assert.True(Game(gap, "candy-carnival").HasPlayed); Assert.True(Game(gap, "nordic-legends").HasPlayed);
        Assert.Null(Game(gap, "reel-riches").HasPlayed);
        await activation.UpdateAsync("status", "active");
        using var unknownRevision = Store(database);
        Assert.DoesNotContain((await unknownRevision.ReadAsync("charlie", default)).Games, game => game.HasPlayed == false);
    }

    [Fact]
    public async Task Existing_acknowledged_event_and_completion_commits_make_memory_game_positives_durable_without_popularity()
    {
        var database = Database(); var sink = new FirestoreGameSecurityEventSink(database);
        var witness = database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("alice"))
            .Collection("games").Document("2048");
        var started = GameSecurityEventDocuments.Capture("2048", "run", 0, Now,
            new { id = Guid.NewGuid(), state = new { moves = 0, phase = "playing" } }, ["alice"], "started");
        await sink.WriteAsync(started);
        Assert.False((await witness.GetSnapshotAsync()).Exists);
        var state = new { id = Guid.NewGuid(), state = new { moves = 1, phase = "won" } };
        var moved = GameSecurityEventDocuments.Capture("2048", "run", 1, Now, state, ["alice"], "moved");
        await sink.WriteAsync(moved);
        var saved = await witness.GetSnapshotAsync();
        var savedEvent = await database.Collection(GameSecurityEventDocuments.CollectionName).Document(moved.Id).GetSnapshotAsync();
        Assert.True(saved.GetValue<bool>("hasPlayed")); Assert.Equal(savedEvent.UpdateTime, saved.UpdateTime);
        Assert.Equal(AcceptedGameParticipationDocuments.CaptureVersion, saved.GetValue<string>("experienceCaptureVersion"));
        Assert.False(saved.ContainsField("playedAt")); Assert.Equal(moved.Fields["payloadJson"], savedEvent.GetValue<string>("payloadJson"));
        await sink.WriteAsync(moved);
        Assert.Equal(saved.UpdateTime, (await witness.GetSnapshotAsync()).UpdateTime);
        var completedAt = Now.AddSeconds(1);
        var completed = GameSecurityEventDocuments.Capture("2048", "run", 2, completedAt, state, ["alice"], "completed");
        var capture = GameSecurityEventSink.FreeCompletion("2048", "run", "table", 1, Now, completedAt, state, "alice", "test-state-v1");
        await sink.WriteCompletedAsync(completed, capture);
        Assert.Equal((await database.Collection(GameSecurityEventDocuments.CollectionName).Document(completed.Id).GetSnapshotAsync()).UpdateTime,
            (await witness.GetSnapshotAsync()).UpdateTime);
        using var restarted = Store(database);
        Assert.True(Game(await restarted.ReadAsync("alice", default), "2048").HasPlayed);
        Assert.Null(Game(await restarted.ReadAsync("bob", default), "2048").HasPlayed);
        Assert.False((await database.Collection("catalogGamePopularity").Document("2048").GetSnapshotAsync()).Exists);
    }

    [Fact]
    public async Task An_older_out_of_order_read_cannot_return_complete_proofs_that_a_newer_receipt_excluded()
    {
        var database = Database(); var now = DateTime.UtcNow;
        await database.Collection(AuthoritativeGameExperienceCoverage.ActivationCollection)
            .Document(AuthoritativeGameExperienceCoverage.ActivationDocument).SetAsync(PlayerGameExperienceTests.ActiveCoverage(now));
        var account = database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("alice"));
        var newerAsOf = Timestamp.FromDateTime(now.AddMinutes(1));
        await account.SetAsync(new Dictionary<string, object>
        {
            ["experienceCoverage"] = new Dictionary<string, object>
            {
                ["slotGuards"] = new Dictionary<string, object>
                { ["asOfUtc"] = newerAsOf, ["activationId"] = "epoch", ["gameIds"] = new[] { "reel-riches" } },
            },
        });
        var seededAsOf = (await account.GetSnapshotAsync()).GetValue<Timestamp>("experienceCoverage.slotGuards.asOfUtc");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["K_REVISION"] = "api-current", ["FF_RELEASE_SHA"] = new string('a', 40) }).Build();
        using var store = Store(database, configuration, TimeProvider.System);
        await store.RecordAcceptedAsync("alice", new("candy-carnival", "settled", "receipt"), default);
        // Seed the later read's receipt first, then finish this older fresh read.
        // Its proof cannot replace the newer watermark or claim to be persisted.
        var response = await store.ReadAsync("alice", default);
        Assert.DoesNotContain(response.Games, game => game.HasPlayed == false);
        Assert.Null(Game(response, "pirates-fortune").HasPlayed);
        Assert.True(Game(response, "candy-carnival").HasPlayed);
        var saved = await account.GetSnapshotAsync();
        Assert.Equal(seededAsOf, saved.GetValue<Timestamp>("experienceCoverage.slotGuards.asOfUtc"));
        Assert.Equal(new[] { "reel-riches" }, saved.GetValue<string[]>("experienceCoverage.slotGuards.gameIds"));
    }

    [Theory]
    [InlineData("liars-dice")]
    [InlineData("craps")]
    [InlineData("roulette")]
    public async Task Additional_acknowledged_engine_commits_capture_only_actual_human_players(string game)
    {
        var database = Database();
        object state = game switch
        {
            "liars-dice" => LiarsDiceMatchEngine.Start(123, ["you", "managed"]),
            "craps" => CrapsEngine.StartPassLine(new("alice", 1)),
            _ => RouletteEngine.Spin(RouletteEngine.PlaceBet(RouletteEngine.OpenRound("round"),
                new("alice", RouletteBetKind.Red, 1, Numbers: [])), new RoulettePocket(1)).State,
        };
        var observation = GameSecurityEventDocuments.Capture(game, "round", 1, Now,
            new { id = Guid.NewGuid(), state, humanParticipants = new[] { "alice" } }, ["alice"], "accepted");
        await new FirestoreGameSecurityEventSink(database).WriteAsync(observation);
        var witness = await database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("alice"))
            .Collection("games").Document(game).GetSnapshotAsync();
        var savedEvent = await database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id).GetSnapshotAsync();
        Assert.True(witness.GetValue<bool>("hasPlayed")); Assert.Equal(savedEvent.UpdateTime, witness.UpdateTime);
        Assert.Equal(AcceptedGameParticipationDocuments.CaptureVersion, witness.GetValue<string>("experienceCaptureVersion"));
        Assert.False(witness.ContainsField("playedAt")); Assert.Equal(observation.Fields["sha256"], savedEvent.GetValue<string>("sha256"));
        using var restarted = Store(database);
        Assert.True(Game(await restarted.ReadAsync("alice", default), game).HasPlayed);
        Assert.Null(Game(await restarted.ReadAsync("bob", default), game).HasPlayed);
        Assert.False((await database.Collection("catalogGamePopularity").Document(game).GetSnapshotAsync()).Exists);
    }

    [Fact]
    public async Task Creation_origin_is_committed_with_new_account_and_is_never_retrofitted_by_duplicate_registration()
    {
        var database = Database(); var accounts = new FirestoreAccountStore(database);
        var requestStartedAt = DateTime.UtcNow.AddMilliseconds(-1); // Server transform precision may be milliseconds.
        var result = await accounts.CreateAsync("fresh", "Fresh", "FRESH", "fresh@example.test", "test-hash", "pending-email-verification", default);
        Assert.NotNull(result.Value);
        var user = await database.Collection("users").Document("fresh").GetSnapshotAsync();
        Assert.Equal(GameExperienceOrigin.Contract, user.GetValue<string>("gameExperienceOrigin.originContract"));
        Assert.InRange(user.GetValue<Timestamp>("gameExperienceOrigin.committedAtUtc").ToDateTime(), requestStartedAt, DateTime.UtcNow);
        Assert.Equal("fresh", user.GetValue<string>("userId"));
        Assert.Equal("fresh", user.GetValue<string>("firebaseUid"));
        Assert.Equal("Fresh", user.GetValue<string>("playerName"));
        var originId = user.GetValue<string>("gameExperienceOrigin.originId");
        Assert.True(Guid.TryParseExact(originId, "N", out _));
        Assert.False(user.ContainsField("hasPlayed"));
        var repeated = await accounts.CreateAsync("fresh", "Fresh", "FRESH", "fresh@example.test", "other", "pending-email-verification", default);
        Assert.Null(repeated.Value);
        var unchanged = await database.Collection("users").Document("fresh").GetSnapshotAsync();
        Assert.Equal(originId, unchanged.GetValue<string>("gameExperienceOrigin.originId"));
        Assert.Equal(user.UpdateTime, unchanged.UpdateTime);
    }

    [Fact]
    public async Task New_origin_under_complete_epoch_proves_all_forty_five_negatives_but_legacy_and_gaps_stay_unknown()
    {
        var database = Database(); var now = DateTime.UtcNow;
        await SeedEpoch(database, now);
        await database.Collection("users").Document("fresh").SetAsync(new Dictionary<string, object>
        {
            ["gameExperienceOrigin"] = GameExperienceOrigin.Create("api-current", new('a', 40)),
            // Deliberately irrelevant account age: only the explicit server
            // origin commit and continuous capture contract can prove newness.
            ["createdAt"] = Timestamp.FromDateTime(now.AddYears(-10)),
        });
        await database.Collection("users").Document("legacy").SetAsync(new Dictionary<string, object>
            { ["createdAt"] = Timestamp.FromDateTime(now) });
        using var store = Store(database, RuntimeConfiguration(), TimeProvider.System);
        var first = await store.ReadAsync("fresh", default);
        Assert.Equal(45, first.Games.Count); Assert.All(first.Games, game => Assert.False(game.HasPlayed));
        Assert.All(first.Games, game => Assert.Contains(ContinuousGameExperienceCoverage.SourceVersion, game.Provenance));
        Assert.DoesNotContain((await store.ReadAsync("legacy", default)).Games, game => game.HasPlayed == false);
        var positive = AcceptedGameParticipationDocuments.Create("fresh", "keno", DateTime.UtcNow);
        await database.Collection("catalogPlayerActivity").Document(positive.DocOwnerKey).Collection("games")
            .Document(positive.GameId).SetAsync(positive.Fields, SetOptions.MergeAll);
        var played = await store.ReadAsync("fresh", default);
        Assert.True(Game(played, "keno").HasPlayed); Assert.Equal(44, played.Games.Count(game => game.HasPlayed == false));
        Assert.Contains(AcceptedGameParticipationDocuments.CaptureVersion, Game(played, "keno").Provenance);
        var account = await database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("fresh")).GetSnapshotAsync();
        Assert.Equal(ContinuousGameExperienceCoverage.SourceVersion, account.GetValue<string>("experienceCoverage.canonicalCapture.sourceVersion"));
        Assert.NotEmpty(account.GetValue<string>("experienceCoverage.canonicalCapture.originId"));
        await database.Collection("slotSpinGuards")
            .Document(AuthoritativeGameExperienceCoverage.GuardKeys("fresh")["pirates-fortune"][0])
            .SetAsync(new Dictionary<string, object> { ["userId"] = "fresh", ["activeAdmissionId"] = "pending" });
        var uncertainSpin = await store.ReadAsync("fresh", default);
        Assert.Null(Game(uncertainSpin, "pirates-fortune").HasPlayed);
        Assert.True(Game(uncertainSpin, "keno").HasPlayed);

        await database.Collection(ContinuousGameExperienceCoverage.EpochCollection).Document("capture-epoch")
            .UpdateAsync("gapAtUtc", Timestamp.FromDateTime(DateTime.UtcNow));
        var gap = await store.ReadAsync("fresh", default);
        Assert.DoesNotContain(gap.Games, game => game.HasPlayed == false); Assert.True(Game(gap, "keno").HasPlayed);
        // A still-active status cannot reactivate a permanently gapped epoch.
        Assert.Null(Game(gap, "snake").HasPlayed);
    }

    [Fact]
    public async Task A_newer_canonical_receipt_does_not_suppress_independent_fresh_slot_lifetime_proof()
    {
        var database = Database(); var now = DateTime.UtcNow;
        await SeedEpoch(database, now);
        await database.Collection("users").Document("fresh").SetAsync(new Dictionary<string, object>
            { ["gameExperienceOrigin"] = GameExperienceOrigin.Create("api-current", new('a', 40)) });
        await database.Collection(AuthoritativeGameExperienceCoverage.ActivationCollection)
            .Document(AuthoritativeGameExperienceCoverage.ActivationDocument).SetAsync(PlayerGameExperienceTests.ActiveCoverage(now));
        var account = database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash("fresh"));
        await account.SetAsync(new Dictionary<string, object>
        {
            ["experienceCoverage"] = new Dictionary<string, object>
            {
                ["canonicalCapture"] = new Dictionary<string, object>
                    { ["asOfUtc"] = Timestamp.FromDateTime(now.AddMinutes(1)), ["gameIds"] = new[] { "keno" } },
            },
        });
        var seeded = (await account.GetSnapshotAsync()).GetValue<Timestamp>("experienceCoverage.canonicalCapture.asOfUtc");
        using var store = Store(database, RuntimeConfiguration(), TimeProvider.System);
        var response = await store.ReadAsync("fresh", default);
        Assert.Equal(20, response.Games.Count(game => game.HasPlayed == false));
        Assert.Null(Game(response, "keno").HasPlayed);
        Assert.Contains(AuthoritativeGameExperienceCoverage.SourceVersion, Game(response, "pirates-fortune").Provenance);
        var saved = await account.GetSnapshotAsync();
        Assert.Equal(seeded, saved.GetValue<Timestamp>("experienceCoverage.canonicalCapture.asOfUtc"));
        Assert.Equal(AuthoritativeGameExperienceCoverage.SourceVersion, saved.GetValue<string>("experienceCoverage.slotGuards.sourceVersion"));
    }

    private static IConfiguration RuntimeConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["K_REVISION"] = "api-current", ["FF_RELEASE_SHA"] = new string('a', 40) }).Build();
    private static async Task SeedEpoch(FirestoreDb database, DateTime now)
    {
        await database.Collection(ContinuousGameExperienceCoverage.PointerCollection)
            .Document(ContinuousGameExperienceCoverage.PointerDocument).SetAsync(PlayerGameExperienceTests.EpochPointer());
        await database.Collection(ContinuousGameExperienceCoverage.EpochCollection)
            .Document("capture-epoch").SetAsync(PlayerGameExperienceTests.ActiveEpoch(now));
    }

    private static PlayerGameExperience Game(PlayerGameExperienceResponse response, string id) => Assert.Single(response.Games.Where(game => game.GameId == id));
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-experience-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
    }.Build();
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
