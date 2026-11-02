using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class PaidCrapsReplayAuditTests
{
    [Fact]
    public void Paid_craps_replays_pass_side_bets_and_odds_from_every_accepted_action()
    {
        var sample = Sample();
        Assert.Empty(PaidCrapsReplayAudit.Inspect(sample.Audit.Summary, sample.Audit.PayloadJson, sample.Events));
        var reconciled = SecurityAuditInvestigationService.ReconcileRoundFindings(sample.Audit.Summary, []);
        Assert.Equal("passed", reconciled.AuditStatus);
        Assert.DoesNotContain(reconciled.Findings, finding => finding.Code == "outcome-replay-pending");

        Assert.Contains(PaidCrapsReplayAudit.Inspect(sample.Audit.Summary, sample.Audit.PayloadJson,
            [sample.Events[0], sample.Events[2], sample.Events[3]]),
            finding => finding.Code == "craps-paid-action-sequence-mismatch");

        var altered = sample.States[3] with { Extras =
            [..sample.States[3].Extras[..^1], sample.States[3].Extras[^1] with { TotalReturn = 100m }] };
        var events = sample.Events.ToArray();
        events[3] = Event(altered, 3, sample.Started.AddSeconds(3), "rolled", sample.RoundId, sample.UserId);
        Assert.Contains(PaidCrapsReplayAudit.Inspect(sample.Audit.Summary, sample.Audit.PayloadJson, events),
            finding => finding.Code == "craps-paid-roll-mismatch");
    }

    [Theory]
    [InlineData(3, 4, true, 3500)]
    [InlineData(1, 2, false, 0)]
    public void Paid_craps_replays_immediate_win_or_loss(int first, int second, bool sideBet, long expectedReturn)
    {
        var id = Guid.NewGuid();
        const string user = "alice";
        var started = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc);
        var state = CrapsEngine.StartPassLine(new CrapsPassLineBet(user, 10m));
        PaidCrapsExtra[] extras = sideBet ? [new("any-seven", 3m, false, false, null)] : [];
        var wager = sideBet ? 1300 : 1000;
        var initial = new PaidCrapsRound(id, user, state, extras, wager, 0, started, null, 0);
        var settled = CrapsEngine.Roll(state, Dice(first, second)).State;
        var result = initial with { State = settled,
            Extras = sideBet ? [new("any-seven", 3m, true, true, 15m)] : [],
            TotalReturnCents = expectedReturn, CompletedAtUtc = started.AddSeconds(1), Revision = 1 };
        var audit = SinglePlayerRoundRecord.Create("craps", id.ToString("N"), user,
            wager, expectedReturn, started, started.AddSeconds(1), "crapsPaidRounds", id.ToString("N"),
            result, "craps-private-dice-and-bets-v1").Audit;
        Assert.Empty(PaidCrapsReplayAudit.Inspect(audit.Summary, audit.PayloadJson,
            [Event(initial, 0, started, "started", id, user),
                Event(result, 1, started.AddSeconds(1), "rolled", id, user)]));
    }

    [Fact]
    public void Historical_shapes_remain_replayable_and_migration_preserves_the_old_journal_and_outcomes()
    {
        var sample = Sample();
        Assert.Empty(PaidCrapsReplayAudit.Inspect(sample.Audit.Summary, Historical(sample.Audit.PayloadJson!),
            sample.Events.Select(Historical).ToArray()));
        var migrated = MigratedSample();
        Assert.Empty(PaidCrapsReplayAudit.Inspect(migrated.Audit.Summary, migrated.Audit.PayloadJson, migrated.Events));
        var altered = JsonNode.Parse(migrated.Events[3])!;
        altered["state"]!["extras"]![0]!["totalReturn"] = 4.01m;
        migrated.Events[3] = altered.ToJsonString();
        Assert.Contains(PaidCrapsReplayAudit.Inspect(migrated.Audit.Summary, migrated.Audit.PayloadJson, migrated.Events),
            finding => finding.Code == "craps-paid-wager-mismatch");
    }

    internal static PaidCrapsSample MigratedSample()
    {
        var sample = Sample();
        var point = sample.States[2];
        var operation = Guid.NewGuid().ToString("N");
        var removed = point with { Extras = [..point.Extras[..^1], point.Extras[^1] with {
                Resolved = true, Cancelled = true, TotalReturn = 0m }],
            TotalWagerCents = 1500, Revision = 3, SchemaVersion = 2,
            Actions = [new(operation, "wager", "odds", 0m, 3, 2, 2, 10_000, 10_500)] };
        var final = removed with { State = sample.States[^1].State, TotalReturnCents = 2400,
            CompletedAtUtc = sample.Started.AddSeconds(4), Revision = 4,
            Actions = [..removed.Actions!, new(Guid.NewGuid().ToString("N"), "roll", null, null,
                4, 3, 2, 10_500, 12_500)] };
        var audit = Capture(final);
        var events = new[] { Historical(sample.Events[0]), Historical(sample.Events[1]), Historical(sample.Events[2]),
            Event(removed, 3, sample.Started.AddSeconds(3), "wager-adjusted", sample.RoundId, sample.UserId),
            Event(final, 4, sample.Started.AddSeconds(4), "rolled", sample.RoundId, sample.UserId) };
        return new(sample.RoundId, sample.UserId, sample.Started,
            [..sample.States[..3], removed, final], audit, events);
    }

    private static string Historical(string json)
    {
        var root = JsonNode.Parse(json)!;
        Strip(root);
        return root.ToJsonString();
        static void Strip(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(item => item.Key).ToArray())
                    if (key.Equals("actions", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("wagerId", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("placedForRoll", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("resolvedOnRoll", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("cancelled", StringComparison.OrdinalIgnoreCase)) obj.Remove(key);
                    else if (obj[key] is { } child) Strip(child);
            }
            else if (node is JsonArray array)
                foreach (var child in array.OfType<JsonNode>()) Strip(child);
        }
    }

    internal static RoundAuditCapture Capture(PaidCrapsRound round) =>
        SinglePlayerRoundRecord.Create("craps", round.Id.ToString("N"), round.UserId,
            round.TotalWagerCents, round.TotalReturnCents, round.StartedAtUtc, round.CompletedAtUtc!.Value,
            "crapsPaidRounds", round.Id.ToString("N"), round, "craps-private-dice-and-bets-v1").Audit;

    internal static PaidCrapsSample Sample()
    {
        var id = Guid.NewGuid();
        const string user = "alice";
        var started = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var opening = CrapsEngine.StartPassLine(new CrapsPassLineBet(user, 10m));
        var initial = new PaidCrapsRound(id, user, opening,
            [new("field", 2m, false, false, null), new("any-craps", 3m, false, false, null)],
            1500, 0, started, null, 0);
        var point = CrapsEngine.Roll(opening, Dice(2, 2)).State;
        var afterPoint = initial with { State = point, Extras =
            [new("field", 2m, true, true, 4m), new("any-craps", 3m, true, false, 0m)],
            TotalReturnCents = 400, Revision = 1 };
        var afterOdds = afterPoint with { Extras = [..afterPoint.Extras, new("odds", 5m, false, false, null)],
            TotalWagerCents = 2000, Revision = 2 };
        var terminal = CrapsEngine.Roll(point, Dice(1, 3)).State;
        var final = afterOdds with { State = terminal, Extras =
            [..afterPoint.Extras, new("odds", 5m, true, true, 15m)],
            TotalReturnCents = 3900, CompletedAtUtc = started.AddSeconds(3), Revision = 3 };
        var states = new[] { initial, afterPoint, afterOdds, final };
        var audit = SinglePlayerRoundRecord.Create("craps", id.ToString("N"), user, 2000, 3900,
            started, started.AddSeconds(3), "crapsPaidRounds", id.ToString("N"), final,
            "craps-private-dice-and-bets-v1").Audit;
        var events = new[]
        {
            Event(initial, 0, started, "started", id, user),
            Event(afterPoint, 1, started.AddSeconds(1), "rolled", id, user),
            Event(afterOdds, 2, started.AddSeconds(2), "odds-placed", id, user),
            Event(final, 3, started.AddSeconds(3), "rolled", id, user),
        };
        return new(id, user, started, states, audit, events);
    }

    private static DicePair Dice(int first, int second) => new(new DieValue(first), new DieValue(second));
    private static string Event(PaidCrapsRound round, long revision, DateTime when, string kind, Guid id, string user) =>
        (string)GameSecurityEventDocuments.Capture("craps", id.ToString("N"), revision, when,
            new { round.State, round.Extras, round.TotalWagerCents, round.TotalReturnCents, round.Actions, round.SchemaVersion }, [user], kind)
            .Fields["payloadJson"];
}

internal sealed record PaidCrapsSample(Guid RoundId, string UserId, DateTime Started,
    PaidCrapsRound[] States, RoundAuditCapture Audit, string[] Events);

[Collection("Blackjack table Firestore emulator")]
public sealed class PaidCrapsFinancialAuditTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task Historical_prefix_and_new_action_suffix_reconcile_together_without_inventing_old_identities()
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = "demo-craps-migrated-audit-" + Guid.NewGuid().ToString("N"),
            Endpoint = fixture.Endpoint, ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
        }.Build();
        var sample = PaidCrapsReplayAuditTests.MigratedSample();
        var id = sample.RoundId.ToString("N");
        var final = sample.States[^1];
        await db.Collection("crapsPaidRounds").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = sample.UserId, ["phase"] = "Resolved",
            ["roundJson"] = JsonSerializer.Serialize(final), ["revision"] = final.Revision,
        });
        foreach (var payload in sample.Events)
        {
            var digest = GameSecurityEventDocuments.Hash(payload);
            await db.Collection(GameSecurityEventDocuments.CollectionName).Document(digest).SetAsync(new Dictionary<string, object>
            {
                ["gameId"] = "craps", ["instanceId"] = id, ["payloadJson"] = payload, ["sha256"] = digest,
            });
        }
        var prefix = $"craps-{id}-";
        var ledger = db.Collection("balanceTransactions");
        await ledger.Document(prefix + "entry").SetAsync(Ledger(prefix + "entry", "craps-wager", -1500));
        foreach (var (suffix, type, amount) in new[] { ("payout", "craps-payout", 400L), ("odds", "craps-odds", -500L) })
        {
            var key = Key();
            var ledgerId = prefix + key + "-" + suffix;
            await ledger.Document(ledgerId).SetAsync(Ledger(ledgerId, type, amount, key));
        }
        foreach (var action in final.Actions!)
        {
            var suffix = action.Kind == "roll" ? "payout" : action.Kind;
            var type = action.Kind == "roll" ? "craps-payout" : "craps-wager-adjustment";
            var ledgerId = prefix + action.OperationId + "-" + suffix;
            var fields = Ledger(ledgerId, type, action.WalletAfterCents - action.WalletBeforeCents, action.OperationId);
            fields["balanceAfter"] = action.WalletAfterCents / 100d;
            await ledger.Document(ledgerId).SetAsync(fields);
        }
        Assert.Empty(await PaidCrapsReplayAudit.InspectFinancialAsync(db, sample.Audit.Summary, sample.Audit.PayloadJson, default));
        var missing = ledger.Document(prefix + final.Actions![0].OperationId + "-wager");
        await missing.DeleteAsync();
        Assert.Contains(await PaidCrapsReplayAudit.InspectFinancialAsync(db, sample.Audit.Summary, sample.Audit.PayloadJson, default),
            finding => finding.Code == "craps-paid-ledger-set-mismatch");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Continuation_reconciles_every_adjustment_refund_roll_and_ledger_beyond_ten(bool pointHit)
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = "demo-craps-continuation-audit-" + Guid.NewGuid().ToString("N"),
            Endpoint = fixture.Endpoint, ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
        }.Build();
        await db.Collection("userBalances").Document("alice_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = 1000L, ["availableFractionalCents"] = 0L,
        });
        var draws = new Queue<DicePair>([Dice(1, 3), Dice(1, 5), Dice(2, 3),
            pointHit ? Dice(2, 2) : Dice(3, 4)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var current = await store.StartAsync("alice", new(10m, [new("field", 1m), new("any-craps", 2m)], Key()));
        await Roll();
        current = await store.PlaceOddsAsync("alice", current.RoundId,
            new(10m, Key(), current.Revision, current.Rolls.Count + 1));
        foreach (var target in new[] { 20m, 4m, 0m, 3m }) await Set("odds", target);
        foreach (var target in new[] { 1m, 2m, 0m, 3m }) await Set("field", target);
        await Set("any-seven", 1m);
        await Set("any-craps", 1m);
        await Roll();
        await Set("field", 3m);
        await Set("any-craps", 1m);
        await Roll();
        await Set("field", 1m);
        await Set("any-seven", 1m);
        await Roll();
        Assert.Equal("resolved", current.Phase);
        Assert.Equal(pointHit ? "point-hit" : "seven-out", current.LastOutcome!.Result);
        var id = current.RoundId.ToString("N");
        var summaries = await db.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync();
        var summary = FirestoreGameRoundAuditStore.Summary(Assert.Single(summaries.Documents));
        var evidence = await db.Collection(RoundAuditDocuments.Evidence).Document(summary.Id).GetSnapshotAsync();
        var audit = new RoundAuditCapture(summary, evidence.GetValue<string>("payloadJson"));
        var observations = (await db.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync())
            .Documents.OrderBy(item => item.GetValue<long>("version"))
            .Select(item => item.GetValue<string>("payloadJson")).ToArray();
        Assert.Empty(PaidCrapsReplayAudit.Inspect(audit.Summary, audit.PayloadJson, observations));
        Assert.Empty(await Financial());
        var ledgers = await db.Collection("balanceTransactions").GetSnapshotAsync();
        Assert.True(ledgers.Count > 10);
        var refund = ledgers.Documents.First(item => item.GetValue<double>("amount") > 0 &&
            item.GetValue<string>("type") == "craps-wager-adjustment");
        var amount = refund.GetValue<double>("amount");
        var after = refund.GetValue<double>("balanceAfter");
        await refund.Reference.UpdateAsync("amount", amount + .01d);
        Assert.Contains(await Financial(), item => item.Code == "craps-paid-ledger-mismatch");
        await refund.Reference.UpdateAsync("amount", amount);
        await refund.Reference.UpdateAsync("balanceAfter", after + .01d);
        Assert.Contains(await Financial(), item => item.Code == "craps-paid-ledger-mismatch");
        await refund.Reference.UpdateAsync("balanceAfter", after);
        var operation = refund.GetValue<string>("idempotencyKey");
        await refund.Reference.UpdateAsync("idempotencyKey", Key());
        Assert.Contains(await Financial(), item => item.Code == "craps-paid-ledger-mismatch");
        await refund.Reference.UpdateAsync("idempotencyKey", operation);
        await refund.Reference.UpdateAsync("type", "craps-payout");
        Assert.Contains(await Financial(), item => item.Code == "craps-paid-ledger-mismatch");
        await refund.Reference.UpdateAsync("type", "craps-wager-adjustment");

        var extra = db.Collection("balanceTransactions").Document($"craps-{id}-zzzz-extra");
        await extra.SetAsync(Ledger(extra.Id, "craps-payout", 1));
        Assert.Contains(await Financial(), item => item.Code == "craps-paid-ledger-set-mismatch");
        await extra.DeleteAsync();
        Assert.Empty(await Financial());

        // Alter each class of accepted state independently; the terminal receipt remains genuine.
        AssertRejected(2, state => state["actions"]!.AsArray()[^1]!["expectedRevision"] = -1L);
        AssertRejected(2, state => state["extras"]!.AsArray()[^1]!["placedForRoll"] = 3);
        AssertRejected(3, state => state["extras"]!.AsArray()[2]!["cancelled"] = false);
        AssertRejected(2, state => state["actions"]!.AsArray()[^1]!["walletAfterCents"] = 0L);
        AssertRejected(observations.Length - 1, state => state["extras"]![0]!["totalReturn"] = 4m);
        Assert.NotEmpty(PaidCrapsReplayAudit.Inspect(audit.Summary, audit.PayloadJson,
            observations.Where((_, index) => index != 3).ToArray()));

        async Task Roll() => current = await store.RollAsync("alice", current.RoundId,
            new(Key(), current.Revision, current.Rolls.Count + 1));
        async Task Set(string kind, decimal target) => current = await store.SetWagerAsync("alice", current.RoundId,
            new(kind, target, Key(), current.Revision, current.Rolls.Count + 1));
        Task<IReadOnlyList<RoundAuditFinding>> Financial() =>
            PaidCrapsReplayAudit.InspectFinancialAsync(db, audit.Summary, audit.PayloadJson, default);
        void AssertRejected(int index, Action<JsonNode> alter)
        {
            var changed = observations.ToArray();
            var node = JsonNode.Parse(changed[index])!;
            alter(node["state"]!);
            changed[index] = node.ToJsonString();
            Assert.NotEmpty(PaidCrapsReplayAudit.Inspect(audit.Summary, audit.PayloadJson, changed));
        }
    }

    [Fact]
    public async Task Paid_craps_reconciles_source_and_every_wallet_ledger()
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = "demo-craps-audit-" + Guid.NewGuid().ToString("N"),
            Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
        }.Build();
        var sample = PaidCrapsReplayAuditTests.Sample();
        var id = sample.RoundId.ToString("N");
        var round = sample.States[^1];
        await db.Collection("crapsPaidRounds").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = sample.UserId, ["phase"] = "Resolved",
            ["roundJson"] = JsonSerializer.Serialize(round), ["revision"] = round.Revision,
        });
        var ledgers = db.Collection("balanceTransactions");
        await ledgers.Document($"craps-{id}-entry").SetAsync(Ledger($"craps-{id}-entry", "craps-wager", -1500));
        var oddsKey = Guid.NewGuid().ToString("N");
        await ledgers.Document($"craps-{id}-{oddsKey}-odds").SetAsync(Ledger($"craps-{id}-{oddsKey}-odds", "craps-odds", -500, oddsKey));
        var firstPayoutKey = Guid.NewGuid().ToString("N");
        var finalPayoutKey = Guid.NewGuid().ToString("N");
        await ledgers.Document($"craps-{id}-{firstPayoutKey}-payout").SetAsync(
            Ledger($"craps-{id}-{firstPayoutKey}-payout", "craps-payout", 400, firstPayoutKey));
        await ledgers.Document($"craps-{id}-{finalPayoutKey}-payout").SetAsync(
            Ledger($"craps-{id}-{finalPayoutKey}-payout", "craps-payout", 3500, finalPayoutKey));

        Assert.Empty(await PaidCrapsReplayAudit.InspectFinancialAsync(db, sample.Audit.Summary, sample.Audit.PayloadJson, default));
        await ledgers.Document($"craps-{id}-{finalPayoutKey}-payout").UpdateAsync("amount", 35.01d);
        Assert.Contains(await PaidCrapsReplayAudit.InspectFinancialAsync(db, sample.Audit.Summary, sample.Audit.PayloadJson, default),
            finding => finding.Code == "craps-paid-ledger-mismatch");
        await ledgers.Document($"craps-{id}-{finalPayoutKey}-payout").UpdateAsync("amount", 35d);
        await db.Collection("crapsPaidRounds").Document(id).UpdateAsync("roundJson", JsonSerializer.Serialize(round with { TotalReturnCents = 3901 }));
        Assert.Contains(await PaidCrapsReplayAudit.InspectFinancialAsync(db, sample.Audit.Summary, sample.Audit.PayloadJson, default),
            finding => finding.Code == "craps-paid-source-mismatch");
    }

    private static Dictionary<string, object> Ledger(string id, string type, long cents, string? key = null) => new()
    {
        ["transactionId"] = id, ["userId"] = "alice", ["currencyId"] = "slotsCredits",
        ["type"] = type, ["amount"] = cents / 100d, ["balanceAfter"] = 100d,
        ["idempotencyKey"] = key ?? Guid.NewGuid().ToString("N"),
    };
    private static string Key() => Guid.NewGuid().ToString("N");
    private static DicePair Dice(int first, int second) => new(new DieValue(first), new DieValue(second));
}
