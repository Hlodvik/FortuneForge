using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class RequestEngineJournalFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 26, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Request_receipts_are_durable_idempotent_and_reject_conflicting_overwrites()
    {
        var db = Database(); var store = new FirestoreGameSecurityRequestStore(db);
        var request = Observation("alice", "mines");
        await store.CreateIntentAsync(request, default); await store.CreateIntentAsync(request, default);
        var result = new GameRequestResult(request.RequestId, Now.AddSeconds(1), 409, "http-response", 1000);
        await store.CreateResultAsync(result, default); await store.CreateResultAsync(result, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateResultAsync(result with { HttpStatusCode = 200 }, default));
        Assert.Single((await db.Collection(FirestoreGameSecurityRequestStore.Intents).GetSnapshotAsync()).Documents);
        var saved = Assert.Single((await db.Collection(FirestoreGameSecurityRequestStore.Results).GetSnapshotAsync()).Documents);
        Assert.Equal(409L, saved.GetValue<long>("httpStatusCode"));
    }

    [Fact]
    public async Task Database_auditor_finds_a_missing_command_intent_without_attributing_player_cheating()
    {
        var db = Database();
        var id = Guid.NewGuid().ToString("N");
        var request = Observation("alice", "mines");
        RoundAuditCapture capture;
        using (GameSecurityContext.Push(request))
        {
            capture = RoundAuditCapture.Create("mines", id, id, 1, Now, Now.AddSeconds(1),
                new { state = "cashed-out" }, "test-v1", "test-auditor-v1",
                [new RoundAuditParticipant("alice", "Alice", true, 100, 150, 50)], []);
            await db.RunTransactionAsync(tx =>
            {
                RoundAuditFirestore.Write(tx, db, capture);
                GameSecurityJournal.Write(tx, db, "mines", id, 1, Now, new { state = "cashed-out" }, ["alice"]);
                return Task.FromResult(true);
            });
        }
        var first = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Equal(1, first.JournalEventCount);
        Assert.Contains(first.Findings, finding => finding.Code == "engine-request-intent-missing" && finding.Severity == "error");

        var receipts = new FirestoreGameSecurityRequestStore(db);
        await receipts.CreateIntentAsync(request, default);
        await receipts.CreateResultAsync(new GameRequestResult(request.RequestId, Now.AddSeconds(2), 200,
            "http-response", 2_000), default);
        var repaired = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Empty(repaired.Findings);
        Assert.Equal("fingerprints-requests-and-manifest-checked", repaired.JournalCoverage);
        await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request.RequestId)
            .UpdateAsync("ipAddress", "198.51.100.8");
        var changedEnvelope = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Contains(changedEnvelope.Findings, finding => finding.Code == "engine-request-intent-changed");
        await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request.RequestId)
            .UpdateAsync("ipAddress", request.IpAddress);
        await db.Collection(FirestoreGameSecurityRequestStore.Results).Document(request.RequestId)
            .UpdateAsync("httpStatusCode", 500L);
        var changedResult = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Contains(changedResult.Findings, finding => finding.Code == "engine-request-result-changed");
        await db.Collection(FirestoreGameSecurityRequestStore.Results).Document(request.RequestId)
            .UpdateAsync("httpStatusCode", 200L);
        await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request.RequestId)
            .UpdateAsync("observationJson", "{}");
        var changed = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Contains(changed.Findings, finding => finding.Code == "engine-request-intent-changed" && finding.Severity == "error");
    }

    [Fact]
    public async Task Missing_linked_http_result_keeps_round_pending_then_becomes_an_integrity_finding_after_grace()
    {
        var db = Database();
        var id = Guid.NewGuid().ToString("N");
        var request = Observation("alice", "mines");
        await new FirestoreGameSecurityRequestStore(db).CreateIntentAsync(request, default);
        RoundAuditCapture capture;
        using (GameSecurityContext.Push(request))
        {
            capture = RoundAuditCapture.Create("mines", id, id, 1, Now, Now.AddSeconds(1),
                new { state = "settled" }, "test-v1", "test-auditor-v1",
                [new RoundAuditParticipant("alice", "Alice", true, 100, 100, 0)], []);
            await db.RunTransactionAsync(tx =>
            {
                RoundAuditFirestore.Write(tx, db, capture);
                GameSecurityJournal.Write(tx, db, "mines", id, 1, Now.AddSeconds(1), new { state = "settled" }, ["alice"]);
                return Task.FromResult(true);
            });
        }

        var early = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default, Now.AddMinutes(1));
        Assert.Contains(early.Findings, finding => finding.Code == "engine-request-result-pending" && finding.Severity == "warning");
        Assert.DoesNotContain(early.Findings, finding => finding.Code == "engine-request-result-missing");
        var late = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default, Now.AddMinutes(3));
        Assert.Contains(late.Findings, finding => finding.Code == "engine-request-result-missing" && finding.Severity == "error");
    }

    [Fact]
    public async Task Mines_preserves_private_initial_and_each_intermediate_version_and_retry_does_not_duplicate()
    {
        var db = Database(); await Seed(db, "alice");
        var service = new MinesService(new FirestoreMinesStore(db), new MinesEngine(new FixedBoard()));
        using var scope = GameSecurityContext.Push(Observation("alice", "mines"));
        var started = await service.StartAsync("alice", new(Guid.NewGuid().ToString("N"), 123, 3));
        var id = started.Round!.RoundId;
        var first = new RevealMinesRequest(Guid.NewGuid().ToString("N"), 24);
        await service.RevealAsync("alice", id, first);
        await service.RevealAsync("alice", id, new(Guid.NewGuid().ToString("N"), 23));
        var before = await Events(db, "mines", id);
        Assert.Equal(3, before.Length);
        Assert.Equal(3, before.Select(item => item.GetValue<long>("version")).Distinct().Count());
        Assert.All(before, item =>
        {
            Assert.Equal(new[] { "alice" }, item.GetValue<string[]>("participantIds"));
            Assert.Contains("minePositions", item.GetValue<string>("payloadJson"), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(GameSecurityContext.Current!.RequestId, item.GetValue<string>("requestId"));
        });
        await service.RevealAsync("alice", id, first);
        Assert.Equal(before.Length, (await Events(db, "mines", id)).Length);
    }

    [Fact]
    public async Task Mines_database_financial_pass_reconciles_source_result_and_wallet_entries()
    {
        var db = Database(); await Seed(db, "alice");
        var service = new MinesService(new FirestoreMinesStore(db), new MinesEngine(new FixedBoard()));
        var started = await service.StartAsync("alice", new(Guid.NewGuid().ToString("N"), 100, 3));
        var id = started.Round!.RoundId;
        await service.RevealAsync("alice", id, new(Guid.NewGuid().ToString("N"), 3));
        await service.CashOutAsync("alice", id, new(Guid.NewGuid().ToString("N")));
        var auditId = GameSecurityEventDocuments.Hash($"mines\n{id}");
        var summary = FirestoreGameRoundAuditStore.Summary(
            await db.Collection(RoundAuditDocuments.Summaries).Document(auditId).GetSnapshotAsync());
        var evidence = await db.Collection(RoundAuditDocuments.Evidence).Document(auditId).GetSnapshotAsync();
        var payload = evidence.GetValue<string>("payloadJson");

        Assert.Empty(await MinesSequenceReplayAudit.InspectFinancialAsync(db, summary, payload, default));
        var entries = await db.Collection("balanceTransactions").WhereEqualTo("roundId", id).GetSnapshotAsync();
        var payout = Assert.Single(entries.Documents, item => item.GetValue<string>("type") == "mines-payout");
        await payout.Reference.UpdateAsync("amountCents", payout.GetValue<long>("amountCents") + 1);
        Assert.Contains(await MinesSequenceReplayAudit.InspectFinancialAsync(db, summary, payload, default),
            finding => finding.Code == "mines-ledger-mismatch");
        await payout.Reference.UpdateAsync("amountCents", payout.GetValue<long>("amountCents"));
        await payout.Reference.UpdateAsync("amount", payout.GetValue<double>("amount") + 0.01d);
        Assert.Contains(await MinesSequenceReplayAudit.InspectFinancialAsync(db, summary, payload, default),
            finding => finding.Code == "mines-ledger-mismatch");
    }

    [Fact]
    public async Task Blackjack_keeps_deal_and_nonterminal_hit_after_later_settlement()
    {
        var db = Database(); await Seed(db, "alice");
        var store = new FirestoreBlackjackStore(db);
        string[] prefix = ["2|clubs", "9|clubs", "3|hearts", "8|diamonds", "4|spades"];
        var deck = prefix.Concat(BlackjackRules.CreateShuffledDeck().Where(card => !prefix.Contains(card))).ToArray();
        var dealt = await store.StartAsync("alice", Guid.NewGuid().ToString("N"), 100, deck, Now, default);
        var hit = await store.ActAsync("alice", dealt.Game.GameId, Guid.NewGuid().ToString("N"), dealt.Game.Version, BlackjackActions.Hit, Now.AddSeconds(1), default);
        Assert.Equal(BlackjackStatuses.Active, hit.Game.Status);
        await store.ActAsync("alice", dealt.Game.GameId, Guid.NewGuid().ToString("N"), hit.Game.Version, BlackjackActions.Stand, Now.AddSeconds(2), default);
        var events = await Events(db, "blackjack", dealt.Game.GameId);
        Assert.Contains(events, item => item.GetValue<string>("eventKind") == "deal");
        Assert.Contains(events, item => item.GetValue<string>("eventKind") == BlackjackActions.Hit);
        var initial = Assert.Single(events, item => item.GetValue<string>("eventKind") == "deal");
        using var json = JsonDocument.Parse(initial.GetValue<string>("payloadJson"));
        Assert.Equal(2, json.RootElement.GetProperty("state").GetProperty("playerCards").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("state").GetProperty("dealerCards").GetArrayLength());
        Assert.Equal(52, json.RootElement.GetProperty("state").GetProperty("deck").GetArrayLength());
    }

    [Fact]
    public async Task Competitive_solitaire_keeps_both_seats_and_each_board_between_commands()
    {
        var db = Database(); await Seed(db, "alice"); await Seed(db, "bob");
        var store = new FirestoreCompetitiveSolitaireStore(db, new CompetitiveSolitaireOptions { AllowSingleHumanBotFill = false });
        var alice = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            Guid.NewGuid().ToString("N"), 1001, Now, default)).Session);
        var bob = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("bob", "Bob", 4, 500, 3,
            Guid.NewGuid().ToString("N"), 1001, Now.AddSeconds(1), default)).Session);
        Assert.Equal(alice.MatchId, bob.MatchId);
        using var scope = GameSecurityContext.Push(Observation("alice", "solitaire"));
        var key = Guid.NewGuid().ToString("N");
        var command = new SolitaireCommandRequest(SolitaireCommandTypes.Draw, alice.Version, null, null, null, null);
        var first = Assert.IsType<SolitaireMatchSessionResponse>((await store.CommandAsync("alice", alice.MatchId, command,
            key, Now.AddSeconds(2), default)).Session);
        var count = (await Events(db, "solitaire", alice.MatchId)).Length;
        await store.CommandAsync("alice", alice.MatchId, command, key, Now.AddSeconds(2), default);
        Assert.Equal(count, (await Events(db, "solitaire", alice.MatchId)).Length);
        await store.CommandAsync("alice", alice.MatchId, command with { ExpectedVersion = first.Version },
            Guid.NewGuid().ToString("N"), Now.AddSeconds(3), default);
        var commands = (await Events(db, "solitaire", alice.MatchId)).Where(item => item.GetValue<string>("eventKind") == "human-command").ToArray();
        Assert.Equal(2, commands.Length);
        var moves = new List<int>();
        foreach (var item in commands)
        {
            Assert.Equal(new[] { "alice", "bob" }, item.GetValue<string[]>("participantIds"));
            using var json = JsonDocument.Parse(item.GetValue<string>("payloadJson"));
            var players = json.RootElement.GetProperty("state").GetProperty("players");
            Assert.Equal(2, players.GetArrayLength());
            moves.Add(players.EnumerateArray().Single(player => player.GetProperty("userId").GetString() == "alice")
                .GetProperty("game").GetProperty("moves").GetInt32());
        }
        Assert.Equal(new[] { 1, 2 }, moves.Order().ToArray());
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-request-engine-" + Guid.NewGuid().ToString("N"),
        Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();
    private static Task Seed(FirestoreDb db, string user) => db.Collection("userBalances").Document(user + "_slotsCredits")
        .SetAsync(new Dictionary<string, object> { ["available"] = 100L, ["availableFractionalCents"] = 0L });
    private static async Task<DocumentSnapshot[]> Events(FirestoreDb db, string game, string instance) =>
        (await db.Collection(GameSecurityEventDocuments.CollectionName).WhereEqualTo("gameId", game).WhereEqualTo("instanceId", instance).GetSnapshotAsync()).Documents.ToArray();
    private static GameRequestObservation Observation(string user, string game) => new(Guid.NewGuid().ToString("N"), user,
        "session-hash", "203.0.113.10", Now, "POST", "/api/games/" + game, game, null, null, null, "{}", "sanitized", "connection");
    private sealed class FixedBoard : IMinesBoardGenerator { public ImmutableArray<int> Generate(int mineCount) => [0, 1, 2]; }
}
