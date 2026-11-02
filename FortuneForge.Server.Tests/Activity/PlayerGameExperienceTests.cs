using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Craps;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Games.Roulette;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Admin.Management;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

public sealed class PlayerGameExperienceTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Empty_or_failed_coverage_never_turns_absence_into_false_for_any_playable_game()
    {
        foreach (var available in new[] { false, true })
        {
            var response = PlayerGameExperienceProjection.Create("alice", Now, [], available);
            Assert.Equal(45, response.Games.Count);
            Assert.Equal(AdminManagementCatalog.Games.Select(game => game.Id).Order(), response.Games.Select(game => game.GameId));
            Assert.Equal(20, response.Games.Count(game => AcceptedGameActivityReader.SlotIds.Values.Contains(game.GameId)));
            Assert.All(response.Games, game =>
            {
                Assert.Equal("unknown", game.State); Assert.Null(game.HasPlayed); Assert.Null(game.FirstPlayedAtUtc);
                Assert.Equal(available ? "partial" : "unavailable", game.Coverage);
            });
            foreach (var hidden in new[] { "asteroids", "flappy", "snake", "horse-flight", "liars-dice", "free-solitaire", "dragon-hoard" })
                Assert.Contains(response.Games, game => game.GameId == hidden);
        }
    }

    [Fact]
    public void Positive_evidence_is_canonical_deduplicated_and_does_not_claim_a_first_time_or_complete_history()
    {
        var response = PlayerGameExperienceProjection.Create("alice", Now,
            [new("mines", "accepted-activity"), new("mines", "accepted-activity"), new("mines", "legacy-cardGameResults"), new("invented", "client")], true);
        var game = Assert.Single(response.Games.Where(game => game.GameId == "mines"));
        Assert.True(game.HasPlayed); Assert.Equal("known", game.State); Assert.Equal("partial", game.Coverage);
        Assert.Equal(new[] { "accepted-activity", "legacy-cardGameResults" }, game.Provenance);
        Assert.Null(game.FirstPlayedAtUtc); Assert.DoesNotContain(response.Games, game => game.GameId == "invented");
    }

    [Fact]
    public void Matched_participation_is_distinct_from_queue_only_and_does_not_change_popularity_allowlist()
    {
        const string path = "/api/cards/texas-holdem/credit/queue";
        Assert.Null(AcceptedGameActivityReader.GameFor("POST", path));
        Assert.Equal("texas-holdem", AcceptedGameParticipation.GameFor("POST", path));
        Assert.Null(AcceptedGameParticipation.Read("texas-holdem", path, "{\"session\":{\"kind\":\"queue\",\"ticketId\":\"reserved\"}}", "join"));
        var accepted = AcceptedGameParticipation.Read("texas-holdem", path,
            "{\"session\":{\"kind\":\"match\",\"table\":{\"matchId\":\"dealt\",\"handNumber\":1}}}", "join");
        Assert.Equal("dealt", accepted?.RoundKey);
    }

    [Theory]
    [InlineData("asteroids", "/api/arcade-competitions/asteroids/free/runs/run/replay", "terminal")]
    [InlineData("flappy", "/api/arcade-competitions/flappy/daily/runs/run/replay", "terminal")]
    [InlineData("horse-flight", "/api/arcade-competitions/horse-flight/free/runs/run/replay", "phase")]
    [InlineData("horse-flight", "/api/games/horse-flight/runs/run/complete", "phase")]
    public void Validated_arcade_completions_count_even_with_zero_score(string game, string path, string phaseField)
    {
        Assert.Equal(game, AcceptedGameParticipation.GameFor("POST", path));
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["runId"] = "owned-run", ["score"] = 0, [phaseField] = "collision" });
        Assert.Equal("owned-run", AcceptedGameParticipation.Read(game, path, json, null)?.RoundKey);
        Assert.Null(AcceptedGameParticipation.Read(game, path, "{\"runId\":\"issued-only\",\"seed\":1}", null));
    }

    [Theory]
    [InlineData("/api/arcade-competitions/flappy/free/runs")]
    [InlineData("/api/slots/demo/spins")]
    [InlineData("/api/games/activity/presence")]
    [InlineData("/api/games/snake/games")]
    [InlineData("/api/games/snake/games/game/reset")]
    [InlineData("/api/solitaire/free/runs")]
    public void Navigation_presence_issued_courses_and_resets_do_not_count(string path) =>
        Assert.Null(AcceptedGameParticipation.GameFor("POST", path));

    [Fact]
    public void Snake_requires_a_real_move_and_craps_requires_an_accepted_stake()
    {
        const string snake = "/api/games/snake/games/id/tick";
        Assert.Null(AcceptedGameParticipation.Read("snake", snake, "{\"gameId\":\"g\",\"moves\":0,\"lastEvent\":\"started\"}", null));
        Assert.Null(AcceptedGameParticipation.Read("snake", snake, "{\"gameId\":\"g\",\"moves\":3,\"lastEvent\":\"no-op\"}", null));
        Assert.Equal("snake", AcceptedGameParticipation.Read("snake", snake, "{\"gameId\":\"g\",\"moves\":1,\"lastEvent\":\"moved\"}", null)?.GameId);
        Assert.Null(AcceptedGameParticipation.Read("craps", "/api/games/craps/rounds", "{\"roundId\":\"r\",\"stake\":0,\"totalWagerCents\":0}", null));
        Assert.Equal("craps", AcceptedGameParticipation.Read("craps", "/api/games/craps/rounds", "{\"roundId\":\"r\",\"stake\":1,\"totalWagerCents\":100}", null)?.GameId);
    }

    [Fact]
    public void Legacy_sources_require_owned_positive_evidence_not_missing_or_unfinished_data()
    {
        var source = new GameExperienceLegacySource("flappyFreeRuns", "playerId", "flappy");
        var fields = new Dictionary<string, object> { ["playerId"] = "alice", ["status"] = "started", ["startedAt"] = Timestamp.FromDateTime(Now), ["completedAt"] = null! };
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(source, "alice", fields));
        fields["completedAt"] = Timestamp.FromDateTime(Now); fields["status"] = "completed";
        Assert.Equal("flappy", FirestorePlayerGameExperienceStore.ReadLegacy(source, "alice", fields)?.GameId);
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(source, "bob", fields));
    }

    [Fact]
    public void Zero_analytics_and_experience_only_docs_never_invent_recent_play()
    {
        var fields = new Dictionary<string, object> { ["games"] = new Dictionary<string, object>
            { ["mines"] = new Dictionary<string, object> { ["humanRounds"] = 0L }, ["craps"] = new Dictionary<string, object> { ["humanRounds"] = 1L } } };
        Assert.Equal("craps", Assert.Single(FirestorePlayerGameExperienceStore.ReadAnalytics(fields)).GameId);
        var evidence = FirestorePlayerGameExperienceStore.ReadActivity("mines", new Dictionary<string, object>
            { ["hasPlayed"] = true, ["experienceProvenance"] = new object[] { "analytics-completion" } }).ToArray();
        Assert.Equal("analytics-completion", Assert.Single(evidence).Provenance);
        Assert.Empty(FirestorePlayerGameExperienceStore.ReadActivity("mines", new Dictionary<string, object>()));
        Assert.Single(FirestorePlayerGameExperienceStore.ReadActivity("mines", new Dictionary<string, object>
            { ["hasPlayed"] = true, ["experienceProvenance"] = Array.Empty<object>() }));
    }

    [Fact]
    public void Backfill_enriches_missing_metadata_once_and_only_writes_new_provenance_afterward()
    {
        var evidence = new PlayerGameExperienceEvidence[]
            { new("mines", "accepted-activity"), new("mines", "analytics-completion"), new("mines", "analytics-completion") };
        var fields = new Dictionary<string, object> { ["playedAt"] = Timestamp.FromDateTime(Now) };
        var existing = new Dictionary<string, IReadOnlyDictionary<string, object>> { ["mines"] = fields };
        Assert.Equal(2, FirestorePlayerGameExperienceStore.MissingEvidence(evidence, existing).Count);
        fields["hasPlayed"] = true; fields["experienceSchemaVersion"] = 1L;
        fields["experienceProvenance"] = new object[] { "accepted-activity" };
        Assert.Equal("analytics-completion", Assert.Single(FirestorePlayerGameExperienceStore.MissingEvidence(evidence, existing)).Provenance);
        fields["experienceProvenance"] = new object[] { "accepted-activity", "analytics-completion" };
        Assert.Empty(FirestorePlayerGameExperienceStore.MissingEvidence(evidence, existing));
        fields["hasPlayed"] = false;
        Assert.Equal(2, FirestorePlayerGameExperienceStore.MissingEvidence(evidence, existing).Count);
    }

    [Fact]
    public void Legacy_hearts_history_and_paid_arcade_entries_use_their_actual_server_timestamps()
    {
        var hearts = new GameExperienceLegacySource("heartsPaidHistory", "userId", "hearts");
        var history = new Dictionary<string, object>
            { ["userId"] = "alice", ["status"] = "result", ["updatedAt"] = Timestamp.FromDateTime(Now), ["entryCents"] = 100L };
        Assert.Equal("hearts", FirestorePlayerGameExperienceStore.ReadLegacy(hearts, "alice", history)?.GameId);
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(hearts, "bob", history));
        var attempt = new GameExperienceLegacySource("arcadeCompetitionAttempts", "playerId", "attempt");
        var entered = new Dictionary<string, object>
        {
            ["playerId"] = "alice", ["gameId"] = "flappy", ["status"] = "started", ["entryFeeCents"] = 100L,
            ["enteredAt"] = Timestamp.FromDateTime(Now), ["completedAt"] = null!,
        };
        Assert.Equal("flappy", FirestorePlayerGameExperienceStore.ReadLegacy(attempt, "alice", entered)?.GameId);
        entered["entryFeeCents"] = 0L;
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(attempt, "alice", entered));
        entered["status"] = "completed"; entered.Remove("enteredAt"); entered["submittedAt"] = Timestamp.FromDateTime(Now);
        Assert.Equal("flappy", FirestorePlayerGameExperienceStore.ReadLegacy(attempt, "alice", entered)?.GameId);
    }

    [Theory]
    [InlineData("baccaratRounds", "baccarat", "stakeCents")]
    [InlineData("casinoWarRounds", "casino-war", "primaryStakeCents")]
    [InlineData("videoPokerRounds", "video-poker", "coinsWagered")]
    [InlineData("kenoRounds", "keno", "wagerCents")]
    [InlineData("sicBoRounds", "sic-bo", "totalStakeCents")]
    public void Legacy_round_receipts_prove_accepted_stakes_without_requiring_a_later_decision(string collection, string game, string stakeField)
    {
        var source = new GameExperienceLegacySource(collection, "userId", game + "-round");
        var fields = new Dictionary<string, object>
            { ["userId"] = "alice", ["roundId"] = "accepted", [stakeField] = 1L, ["createdAt"] = Timestamp.FromDateTime(Now) };
        Assert.Equal(game, FirestorePlayerGameExperienceStore.ReadLegacy(source, "alice", fields)?.GameId);
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(source, "bob", fields));
        fields[stakeField] = 0L;
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(source, "alice", fields));
        fields[stakeField] = 1L; fields.Remove("roundId");
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(source, "alice", fields));
    }

    [Theory]
    [InlineData("asteroidsRuns", "asteroids")]
    [InlineData("flappyRuns", "flappy")]
    public void Legacy_paid_arcade_run_queries_use_the_persisted_player_owner_field(string collection, string game)
    {
        var source = Assert.Single(FirestorePlayerGameExperienceStore.LegacySources.Where(source => source.Collection == collection));
        var fields = new Dictionary<string, object>
            { ["playerId"] = "alice", ["runId"] = "accepted", ["completedAt"] = Timestamp.FromDateTime(Now) };
        Assert.Equal("playerId", source.OwnerField);
        Assert.Equal(game, FirestorePlayerGameExperienceStore.ReadLegacy(source, "alice", fields)?.GameId);
        Assert.Null(FirestorePlayerGameExperienceStore.ReadLegacy(source, "bob", fields));
    }

    [Fact]
    public void Complete_source_proof_can_return_false_but_positive_evidence_always_wins()
    {
        var proof = new GameExperienceCoverageProof("pirates-fortune", "verified-epoch", Now, "api-current", new('a', 40), Now.AddHours(1));
        var fresh = PlayerGameExperienceProjection.Create("alice", Now, [], true, [proof]);
        var game = Assert.Single(fresh.Games.Where(item => item.GameId == proof.GameId));
        Assert.False(game.HasPlayed); Assert.Equal("known", game.State); Assert.Equal("complete", game.Coverage);
        Assert.NotEmpty(game.Provenance); Assert.Null(game.FirstPlayedAtUtc);
        Assert.Null(Assert.Single(fresh.Games.Where(item => item.GameId == "hearts")).HasPlayed);
        var played = PlayerGameExperienceProjection.Create("alice", Now, [new(proof.GameId, "accepted-activity")], true, [proof]);
        Assert.True(Assert.Single(played.Games.Where(item => item.GameId == proof.GameId)).HasPlayed);
        var expired = PlayerGameExperienceProjection.Create("alice", Now.AddHours(1), [], true, [proof]);
        Assert.Null(Assert.Single(expired.Games.Where(item => item.GameId == proof.GameId)).HasPlayed);
    }

    [Fact]
    public void Full_slot_coverage_reads_all_twenty_canonical_keys_and_the_historical_wukong_alias()
    {
        var keys = AuthoritativeGameExperienceCoverage.GuardKeys("alice");
        Assert.Equal(20, keys.Count); Assert.Equal(21, keys.Values.Sum(value => value.Count));
        Assert.Equal(2, keys["wukong-journey-to-the-west"].Count);
        Assert.Contains("alice_" + AcceptedGameActivityReader.Hash("classic-demo-v1"), keys["wukong-journey-to-the-west"]);
        Assert.DoesNotContain(keys.Values.SelectMany(value => value), key => key.StartsWith("bob_", StringComparison.Ordinal));
        Assert.Equal("epoch", AuthoritativeGameExperienceCoverage.ValidateActivation(ActiveCoverage(Now), "api-current", new('a', 40), Now, Now));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing-source")]
    [InlineData("wrong-proof")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("unattested-serving-revision")]
    [InlineData("wrong-current-commit")]
    [InlineData("incomplete-games")]
    [InlineData("malformed-games")]
    [InlineData("overlong-validity")]
    [InlineData("invalid-epoch")]
    public void Coverage_gaps_stale_or_malformed_attestations_never_prove_newness(string defect)
    {
        var fields = ActiveCoverage(Now);
        switch (defect)
        {
            case "inactive": fields["status"] = "paused"; break;
            case "missing-source": fields.Remove("sourceVersion"); break;
            case "wrong-proof": fields["sourceProofSha256"] = new string('0', 64); break;
            case "stale": fields["validUntilUtc"] = Timestamp.FromDateTime(Now.AddSeconds(-1)); break;
            case "future": fields["verifiedAtUtc"] = Timestamp.FromDateTime(Now.AddSeconds(1)); break;
            case "unattested-serving-revision": fields["servingApiRevisions"] = new[] { "api-current", "unknown-old-api" }; break;
            case "wrong-current-commit": fields["compatibleApiRevisions"] = new object[] { new Dictionary<string, object>
                { ["apiRevision"] = "api-current", ["sourceVersion"] = AuthoritativeGameExperienceCoverage.SourceVersion, ["sourceRevision"] = new string('b', 40) } }; break;
            case "incomplete-games": fields["gameIds"] = new[] { "pirates-fortune" }; break;
            case "malformed-games": fields["gameIds"] = AcceptedGameActivityReader.SlotIds.Values.Cast<object>().Append(1L).ToArray(); break;
            case "overlong-validity": fields["validUntilUtc"] = Timestamp.FromDateTime(Now.AddDays(2)); break;
            case "invalid-epoch": fields["activationId"] = "bad\n"; break;
        }
        Assert.Null(AuthoritativeGameExperienceCoverage.ValidateActivation(fields, "api-current", new('a', 40), Now, Now));
    }

    [Theory]
    [InlineData("2048")]
    [InlineData("drop-merge")]
    [InlineData("hearts")]
    public void Existing_durable_security_commits_capture_actual_participation_and_exclude_empty_moves(string game)
    {
        var started = GameSecurityEventDocuments.Capture(game, "run", 0, Now,
            new { id = Guid.NewGuid(), state = new { moves = 0, phase = "playing" } }, ["alice"], "started");
        if (game == "hearts") Assert.Equal("alice", Assert.Single(GameExperienceSecurityWitness.Players(started)));
        else Assert.Empty(GameExperienceSecurityWitness.Players(started));
        var moved = GameSecurityEventDocuments.Capture(game, "run", 1, Now,
            new { id = Guid.NewGuid(), state = new { moves = 1, phase = "playing" } }, ["alice", "alice"], "moved");
        var before = (string)moved.Fields["payloadJson"];
        Assert.Equal("alice", Assert.Single(GameExperienceSecurityWitness.Players(moved)));
        Assert.Equal(before, moved.Fields["payloadJson"]);
        Assert.Equal(moved.Id, moved.Fields["sha256"]);
    }

    [Fact]
    public void Dealt_liars_dice_state_is_real_participation_before_its_first_bid()
    {
        var state = LiarsDiceMatchEngine.Start(123, ["you", "managed"]);
        var accepted = GameSecurityEventDocuments.Capture("liars-dice", "match", 1, Now,
            new { id = Guid.NewGuid(), state }, ["alice"], "started");
        Assert.Equal("alice", Assert.Single(GameExperienceSecurityWitness.Players(accepted)));
        var invalid = GameSecurityEventDocuments.Capture("liars-dice", "match", 1, Now,
            new { id = Guid.NewGuid(), state = state with { RoundNumber = 0 } }, ["alice"], "started");
        Assert.Empty(GameExperienceSecurityWitness.Players(invalid));
    }

    [Fact]
    public void Practice_craps_witness_belongs_only_to_the_accepted_positive_stake_player()
    {
        var state = CrapsEngine.StartPassLine(new CrapsPassLineBet("alice", 1));
        var accepted = GameSecurityEventDocuments.Capture("craps", "round", 1, Now,
            new { id = Guid.NewGuid(), state }, ["alice", "observer"], "started");
        Assert.Equal("alice", Assert.Single(GameExperienceSecurityWitness.Players(accepted)));
        var invalid = GameSecurityEventDocuments.Capture("craps", "round", 1, Now,
            new { id = Guid.NewGuid(), state = state with { Bet = new("alice", 0) } }, ["alice"], "started");
        Assert.Empty(GameExperienceSecurityWitness.Players(invalid));
    }

    [Fact]
    public void Roulette_requires_an_actual_spun_human_bet_and_excludes_cancelled_bets_bots_and_observers()
    {
        var state = RouletteEngine.OpenRound("round");
        foreach (var player in new[] { "alice", "cancelled", "managed" })
            state = RouletteEngine.PlaceBet(state, new(player, RouletteBetKind.Red, 1, Numbers: []));
        state = RouletteEngine.RemoveBet(state, 1);
        var humans = new[] { "alice", "cancelled" };
        var actors = new[] { "alice", "cancelled", "observer" };
        var unspun = GameSecurityEventDocuments.Capture("roulette", "round", 1, Now,
            new { id = Guid.NewGuid(), state, humanParticipants = humans }, actors, "bet-placed");
        Assert.Empty(GameExperienceSecurityWitness.Players(unspun));
        var settled = RouletteEngine.Spin(state, new RoulettePocket(1)).State;
        var spun = GameSecurityEventDocuments.Capture("roulette", "round", 2, Now,
            new { id = Guid.NewGuid(), state = settled, humanParticipants = humans }, actors, "spun");
        Assert.Equal("alice", Assert.Single(GameExperienceSecurityWitness.Players(spun)));
        var noActualBet = GameSecurityEventDocuments.Capture("roulette", "round", 3, Now,
            new { id = Guid.NewGuid(), state = settled with { Bets = [] }, humanParticipants = humans }, actors, "observed");
        Assert.Empty(GameExperienceSecurityWitness.Players(noActualBet));
    }

    [Fact]
    public void Explicit_creation_origin_and_continuous_capture_can_prove_false_for_every_canonical_game()
    {
        var origin = ValidOrigin(Now);
        var eligible = Assert.IsType<ContinuousGameExperienceEligibility>(ContinuousGameExperienceCoverage.Validate(
            origin, EpochPointer(), ActiveEpoch(Now), "api-current", new('a', 40), Now));
        Assert.Equal(45, eligible.GameIds.Count);
        var response = PlayerGameExperienceProjection.Create("alice", Now, [], true, eligible.Proofs(Now, Now));
        Assert.All(response.Games, game =>
        {
            Assert.False(game.HasPlayed); Assert.Equal("complete", game.Coverage);
            Assert.Contains(ContinuousGameExperienceCoverage.SourceVersion, game.Provenance);
            Assert.Contains(game.Provenance, source => source.StartsWith("creation-origin:", StringComparison.Ordinal));
        });
        Assert.Empty(eligible.Proofs(Now.AddMinutes(-5), Now));
        Assert.Empty(eligible.Proofs(Now, Now.AddHours(2)));
        var stamped = GameExperienceOrigin.Create("api-current", new('a', 40));
        Assert.Equal(GameExperienceOrigin.Contract, stamped["originContract"]);
        Assert.Equal(FieldValue.ServerTimestamp, stamped["committedAtUtc"]);
        Assert.True(Guid.TryParseExact((string)stamped["originId"], "N", out _));
        Assert.DoesNotContain("createdAt", stamped.Keys); Assert.DoesNotContain("hasPlayed", stamped.Keys);
    }

    [Fact]
    public void Epoch_closure_is_proven_per_game_for_every_writer_not_just_the_current_revision()
    {
        var epoch = ActiveEpoch(Now);
        var writers = (object[])epoch["writerRevisions"];
        var old = (Dictionary<string, object>)writers[0];
        ((Dictionary<string, object>)old["gameCaptureVersions"]).Remove("keno");
        ((Dictionary<string, object>)epoch["gameCaptureVersions"]).Remove("hearts");
        var eligible = Assert.IsType<ContinuousGameExperienceEligibility>(ContinuousGameExperienceCoverage.Validate(
            ValidOrigin(Now), EpochPointer(), epoch, "api-current", new('a', 40), Now));
        Assert.Equal(43, eligible.GameIds.Count);
        var response = PlayerGameExperienceProjection.Create("alice", Now, [new("mines", "accepted-activity")], true, eligible.Proofs(Now, Now));
        Assert.Null(Assert.Single(response.Games.Where(game => game.GameId == "keno")).HasPlayed);
        Assert.Null(Assert.Single(response.Games.Where(game => game.GameId == "hearts")).HasPlayed);
        Assert.True(Assert.Single(response.Games.Where(game => game.GameId == "mines")).HasPlayed);
        Assert.False(Assert.Single(response.Games.Where(game => game.GameId == "snake")).HasPlayed);
    }

    [Theory]
    [InlineData("legacy-no-origin")]
    [InlineData("missing-origin-identity")]
    [InlineData("unknown-origin-commit")]
    [InlineData("origin-before-cutover")]
    [InlineData("future-origin")]
    [InlineData("missing-origin-server-stamp")]
    [InlineData("inactive-pointer")]
    [InlineData("retired")]
    [InlineData("retired-but-reactivated")]
    [InlineData("gap")]
    [InlineData("expired")]
    [InlineData("wrong-source-proof")]
    [InlineData("wrong-current-commit")]
    [InlineData("unverified-serving-revision")]
    [InlineData("incomplete-catalogue")]
    public void Missing_legacy_or_discontinuous_coverage_never_becomes_a_newbie_claim(string defect)
    {
        var origin = ValidOrigin(Now); var pointer = EpochPointer(); var epoch = ActiveEpoch(Now);
        switch (defect)
        {
            case "legacy-no-origin": origin.Clear(); break;
            case "missing-origin-identity": origin["sourceRevision"] = ""; break;
            case "unknown-origin-commit": origin["sourceRevision"] = new string('c', 40); break;
            case "origin-before-cutover": origin["committedAtUtc"] = Timestamp.FromDateTime(Now.AddDays(-1)); break;
            case "future-origin": origin["committedAtUtc"] = Timestamp.FromDateTime(Now.AddSeconds(1)); break;
            case "missing-origin-server-stamp": origin.Remove("committedAtUtc"); break;
            case "inactive-pointer": pointer["status"] = "paused"; break;
            case "retired": epoch["status"] = "retired"; break;
            case "retired-but-reactivated": epoch["retiredAtUtc"] = Timestamp.FromDateTime(Now.AddMinutes(-1)); break;
            case "gap": epoch["gapAtUtc"] = Timestamp.FromDateTime(Now.AddMinutes(-1)); break;
            case "expired": epoch["validUntilUtc"] = Timestamp.FromDateTime(Now.AddSeconds(-1)); break;
            case "wrong-source-proof": epoch["sourceProofSha256"] = new string('0', 64); break;
            case "wrong-current-commit": ((Dictionary<string, object>)((object[])epoch["writerRevisions"])[1])["sourceRevision"] = new string('c', 40); break;
            case "unverified-serving-revision": epoch["servingApiRevisions"] = new[] { "api-current", "old-unreviewed" }; break;
            case "incomplete-catalogue": epoch["canonicalGameIds"] = new[] { "keno" }; break;
        }
        Assert.Null(ContinuousGameExperienceCoverage.Validate(origin, pointer, epoch, "api-current", new('a', 40), Now));
    }

    internal static Dictionary<string, object> ValidOrigin(DateTime now)
    {
        var origin = GameExperienceOrigin.Create("api-original", new('b', 40));
        origin["committedAtUtc"] = Timestamp.FromDateTime(now.AddMinutes(-1));
        return origin;
    }
    internal static Dictionary<string, object> EpochPointer() => new()
        { ["schemaVersion"] = 1L, ["status"] = "active", ["epochId"] = "capture-epoch" };
    internal static Dictionary<string, object> ActiveEpoch(DateTime now)
    {
        Dictionary<string, object> Versions() => PlayerGameExperienceProjection.GameIds
            .ToDictionary(game => game, _ => (object)AcceptedGameParticipationDocuments.CaptureVersion, StringComparer.Ordinal);
        Dictionary<string, object> Writer(string api, char commit) => new()
        {
            ["apiRevision"] = api, ["sourceRevision"] = new string(commit, 40),
            ["originContract"] = GameExperienceOrigin.Contract, ["gameCaptureVersions"] = Versions(),
        };
        return new()
        {
            ["schemaVersion"] = 1L, ["status"] = "active", ["epochId"] = "capture-epoch",
            ["sourceVersion"] = ContinuousGameExperienceCoverage.SourceVersion,
            ["sourceProofSha256"] = ContinuousGameExperienceCoverage.SourceProofSha256,
            ["captureVersion"] = AcceptedGameParticipationDocuments.CaptureVersion,
            ["originContract"] = GameExperienceOrigin.Contract,
            ["continuousFromUtc"] = Timestamp.FromDateTime(now.AddHours(-2)),
            ["verifiedAtUtc"] = Timestamp.FromDateTime(now.AddMinutes(-10)),
            ["validUntilUtc"] = Timestamp.FromDateTime(now.AddHours(1)),
            ["canonicalGameIds"] = PlayerGameExperienceProjection.GameIds.ToArray(),
            ["gameCaptureVersions"] = Versions(), ["servingApiRevisions"] = new[] { "api-current" },
            ["writerRevisions"] = new object[] { Writer("api-original", 'b'), Writer("api-current", 'a') },
        };
    }

    internal static Dictionary<string, object> ActiveCoverage(DateTime now) => new()
    {
        ["schemaVersion"] = 1L, ["status"] = "active", ["activationId"] = "epoch", ["lifetimeCoverage"] = true,
        ["sourceVersion"] = AuthoritativeGameExperienceCoverage.SourceVersion,
        ["sourceOrigin"] = AuthoritativeGameExperienceCoverage.SourceOrigin,
        ["sourceProofSha256"] = AuthoritativeGameExperienceCoverage.SourceProofSha256,
        ["activatedAtUtc"] = Timestamp.FromDateTime(now.AddMinutes(-2)),
        ["verifiedAtUtc"] = Timestamp.FromDateTime(now.AddMinutes(-1)),
        ["validUntilUtc"] = Timestamp.FromDateTime(now.AddHours(1)),
        ["gameIds"] = AcceptedGameActivityReader.SlotIds.Values.ToArray(),
        ["servingApiRevisions"] = new[] { "api-current" },
        ["compatibleApiRevisions"] = new object[] { new Dictionary<string, object>
        {
            ["apiRevision"] = "api-current", ["sourceRevision"] = new string('a', 40),
            ["sourceVersion"] = AuthoritativeGameExperienceCoverage.SourceVersion,
        } },
    };
}
