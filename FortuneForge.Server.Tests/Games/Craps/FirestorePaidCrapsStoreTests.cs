using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Dice;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Craps;

[Collection("Blackjack table Firestore emulator")]
public sealed class FirestorePaidCrapsStoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task StartDebitsAllUniqueBetsOnceAndTerminalRollCreditsExactlyOnce()
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var store = new FirestorePaidCrapsStore(db, () => Dice(3, 4));
        var operationId = Key();
        var request = new StartCrapsRoundRequest(1m,
            [new("field", 1m), new("any-seven", 1m)], operationId);

        var started = await store.StartAsync("alice", request);
        Assert.Equal(9700, started.WalletBalanceCents);
        var replayedStart = await store.StartAsync("alice", request);
        Assert.Equal(started.RoundId, replayedStart.RoundId);
        Assert.Equal(started.WalletBalanceCents, replayedStart.WalletBalanceCents);
        Assert.Equal(started.RoundId, (await store.ActiveAsync("alice"))!.RoundId);
        await Assert.ThrowsAsync<CrapsAccessException>(() => store.GetAsync("bob", started.RoundId));
        Assert.Equal(9700, (await store.StatusAsync("alice")).WalletBalanceCents);

        var roll = RollRequest(started);
        var settled = await store.RollAsync("alice", started.RoundId, roll);
        Assert.Equal("resolved", settled.Phase);
        Assert.Equal(2m, settled.LastOutcome!.TotalReturn);
        Assert.Equal(5m, settled.ExtraBets.Single(bet => bet.Kind == "any-seven").TotalReturn);
        Assert.Equal(10400, settled.WalletBalanceCents);
        var replayedRoll = await store.RollAsync("alice", started.RoundId, roll);
        Assert.Equal(settled.RoundId, replayedRoll.RoundId);
        Assert.Equal(settled.WalletBalanceCents, replayedRoll.WalletBalanceCents);
        Assert.Equal(settled.Rolls, replayedRoll.Rolls);
        Assert.Null(await store.ActiveAsync("alice"));
        Assert.Equal(10400, (await store.GetAsync("alice", started.RoundId)).WalletBalanceCents);
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        var summary = await db.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync();
        Assert.Single(summary.Documents);
        Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task OddsAreDebitedOnceAndTrueOddsPaidAtPointResolution()
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var draws = new Queue<DicePair>([Dice(1, 3), Dice(1, 3)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var started = await store.StartAsync("alice", new(1m, null, Key()));
        var pointed = await store.RollAsync("alice", started.RoundId, RollRequest(started));
        Assert.Equal("point", pointed.Phase);
        Assert.Equal(99_00, pointed.WalletBalanceCents);
        var oddsRequest = new PlaceCrapsOddsRequest(2m, Key(), pointed.Revision, pointed.Rolls.Count + 1);
        var odds = await store.PlaceOddsAsync("alice", started.RoundId, oddsRequest);
        Assert.Equal(97_00, odds.WalletBalanceCents);
        var replayedOdds = await store.PlaceOddsAsync("alice", started.RoundId, oddsRequest);
        Assert.Equal(odds.RoundId, replayedOdds.RoundId);
        Assert.Equal(odds.WalletBalanceCents, replayedOdds.WalletBalanceCents);
        var settled = await store.RollAsync("alice", started.RoundId, RollRequest(odds));
        Assert.Equal("point-hit", settled.LastOutcome!.Result);
        Assert.Equal(6m, settled.ExtraBets.Single(bet => bet.Kind == "odds").TotalReturn);
        Assert.Equal(105_00, settled.WalletBalanceCents);
        Assert.Equal(3, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task WinningOneRollSideBetPaysBeforePassLineResolves()
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var draws = new Queue<DicePair>([Dice(1, 3), Dice(3, 4)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var started = await store.StartAsync("alice", new(1m, [new("field", 1m)], Key()));
        Assert.Equal(9800, started.WalletBalanceCents);

        var firstRoll = RollRequest(started);
        var pointed = await store.RollAsync("alice", started.RoundId, firstRoll);
        Assert.Equal("point", pointed.Phase);
        Assert.Equal(2m, pointed.ExtraBets.Single().TotalReturn);
        Assert.Equal(10000, pointed.WalletBalanceCents);
        Assert.Equal(10000, (await store.RollAsync("alice", started.RoundId, firstRoll)).WalletBalanceCents);

        var settled = await store.RollAsync("alice", started.RoundId, RollRequest(pointed));
        Assert.Equal("resolved", settled.Phase);
        Assert.Equal("seven-out", settled.LastOutcome!.Result);
        Assert.Equal(10000, settled.WalletBalanceCents);
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task RejectsDuplicatesConflictAndInsufficientCreditsWithoutWalletChange()
    {
        var db = Database();
        await Seed(db, "alice", 1m);
        var store = new FirestorePaidCrapsStore(db, () => Dice(3, 4));
        var duplicate = await Assert.ThrowsAsync<CrapsPaidException>(() => store.StartAsync("alice", new(1m,
            [new("field", 1m), new("field", 1m)], Key())));
        Assert.Equal("craps-invalid-bets", duplicate.Code);
        var insufficient = await Assert.ThrowsAsync<CrapsPaidException>(() => store.StartAsync("alice", new(1m,
            [new("field", 1m)], Key())));
        Assert.Equal("craps-insufficient-credits", insufficient.Code);
        var id = Key();
        var started = await store.StartAsync("alice", new(1m, null, id));
        Assert.Equal(0, started.WalletBalanceCents);
        var conflict = await Assert.ThrowsAsync<CrapsPaidException>(() => store.StartAsync("alice", new(2m, null, id)));
        Assert.Equal("craps-operation-conflict", conflict.Code);
        Assert.Equal(0, (await store.StatusAsync("alice")).WalletBalanceCents);
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task RepeatedOddsAndNextRollChangesRefundNetStakeAndPreserveEverySettledWager()
    {
        var db = Database();
        await Seed(db, "alice", 1000m);
        var draws = new Queue<DicePair>([Dice(1, 3), Dice(2, 3), Dice(1, 3)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var round = await store.StartAsync("alice", new(10m,
            [new("field", 2m), new("any-craps", 3m), new("any-seven", 4m)], Key()));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        var originalOutcomes = round.ExtraBets.ToArray();
        Assert.Equal(98500, round.WalletBalanceCents);
        foreach (var amount in new[] { 20m, 30m, 5m, 0m, 10m })
            round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "odds", amount));
        Assert.Equal(97500, round.WalletBalanceCents);
        foreach (var amount in new[] { 6m, 7m, 2m, 0m, 3m })
            round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "field", amount));
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "any-seven", 4m));
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "any-craps", 5m));
        Assert.Equal(96300, round.WalletBalanceCents);
        var roll = RollRequest(round);
        round = await store.RollAsync("alice", round.RoundId, roll);
        Assert.Equal("point", round.Phase);
        Assert.Equal(96300, round.WalletBalanceCents);
        Assert.Equal(96300, (await store.RollAsync("alice", round.RoundId, roll)).WalletBalanceCents);
        Assert.Single(round.ExtraBets, extra => !extra.Resolved);
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "field", 1m));
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "any-seven", 2m));
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "any-craps", 3m));
        Assert.Equal(95700, round.WalletBalanceCents);
        var finalRequest = RollRequest(round);
        var final = await store.RollAsync("alice", round.RoundId, finalRequest);
        Assert.Equal("point-hit", final.LastOutcome!.Result);
        Assert.Equal(100900, final.WalletBalanceCents);
        Assert.Equal(4700, final.TotalWagerCents);
        Assert.Equal(5600, final.TotalReturnCents);
        Assert.Equal(originalOutcomes, final.ExtraBets.Take(3));
        Assert.True(final.ExtraBets.Count > 4);
        Assert.Equal(final.ExtraBets.Count, final.ExtraBets.Select(extra => extra.WagerId).Distinct().Count());
        Assert.All(final.ExtraBets.Where(extra => extra.Cancelled), extra =>
        {
            Assert.True(extra.Resolved);
            Assert.Equal(0m, extra.TotalReturn);
            Assert.Null(extra.ResolvedOnRoll);
        });
        Assert.Equal(final.ExtraBets, (await store.GetAsync("alice", round.RoundId)).ExtraBets);
        Assert.Equal(100900, (await store.RollAsync("alice", round.RoundId, finalRequest)).WalletBalanceCents);
        Assert.Null(await store.ActiveAsync("alice"));
        Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("field", 1, 1, 300)]
    [InlineData("field", 6, 6, 300)]
    [InlineData("field", 1, 2, 200)]
    [InlineData("field", 2, 3, 0)]
    [InlineData("any-seven", 3, 4, 500)]
    [InlineData("any-craps", 1, 1, 800)]
    [InlineData("any-craps", 1, 2, 800)]
    [InlineData("any-craps", 6, 6, 800)]
    public async Task NextRollBetsSettleOnceAtExistingRatios(string kind, int first, int second, long expectedReturn)
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var draws = new Queue<DicePair>([Dice(1, 3), Dice(first, second), Dice(3, 4)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var round = await store.StartAsync("alice", new(1m, null, Key()));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, kind, 1m));
        Assert.Equal(2, round.ExtraBets.Single().PlacedForRoll);
        var operation = RollRequest(round);
        round = await store.RollAsync("alice", round.RoundId, operation);
        Assert.Equal(expectedReturn, round.TotalReturnCents);
        Assert.Equal(expectedReturn / 100m, round.ExtraBets.Single().TotalReturn);
        Assert.Equal(2, round.ExtraBets.Single().ResolvedOnRoll);
        Assert.Equal(9800 + expectedReturn, round.WalletBalanceCents);
        Assert.Equal(round.WalletBalanceCents, (await store.RollAsync("alice", round.RoundId, operation)).WalletBalanceCents);
        if (round.Phase != "resolved")
        {
            var settled = await store.RollAsync("alice", round.RoundId, RollRequest(round));
            Assert.Equal(expectedReturn, settled.TotalReturnCents);
            Assert.Equal(round.ExtraBets, settled.ExtraBets);
        }
    }

    [Fact]
    public async Task ConcurrentTabsAndRollAdjustmentRaceAcceptOnlyOneRevisionAndRetryCannotMoveToAnotherRoll()
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var store = new FirestorePaidCrapsStore(db, () => Dice(2, 3));
        var other = new FirestorePaidCrapsStore(db, () => Dice(1, 2));
        var round = await store.StartAsync("alice", new(1m, null, Key()));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        var first = WagerRequest(round, "odds", 2m);
        var second = WagerRequest(round, "odds", 3m);
        var results = await Task.WhenAll(Capture(() => store.SetWagerAsync("alice", round.RoundId, first)),
            Capture(() => other.SetWagerAsync("alice", round.RoundId, second)));
        Assert.Single(results, result => result.Round is not null);
        Assert.Equal("craps-round-stale", Assert.Single(results, result => result.Error is not null).Error!.Code);
        round = (await store.ActiveAsync("alice"))!;
        Assert.Single(round.ExtraBets, extra => !extra.Resolved);
        var adjustment = WagerRequest(round, "field", 1m);
        var rollRequest = RollRequest(round);
        results = await Task.WhenAll(Capture(() => store.SetWagerAsync("alice", round.RoundId, adjustment)),
            Capture(() => other.RollAsync("alice", round.RoundId, rollRequest)));
        Assert.Single(results, result => result.Round is not null);
        Assert.Equal("craps-round-stale", Assert.Single(results, result => result.Error is not null).Error!.Code);
        var accepted = Assert.Single(results, result => result.Round is not null).Round!;
        var replay = accepted.Rolls.Count == round.Rolls.Count
            ? await store.SetWagerAsync("alice", round.RoundId, adjustment)
            : await other.RollAsync("alice", round.RoundId, rollRequest);
        Assert.Equal(accepted.Revision, replay.Revision);
        round = (await store.ActiveAsync("alice"))!;
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        var stale = await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId,
            new("any-craps", 1m, Key(), adjustment.ExpectedRevision, adjustment.ExpectedRollNumber)));
        Assert.Equal("craps-round-stale", stale.Code);
        Assert.Equal(round.Revision, (await store.GetAsync("alice", round.RoundId)).Revision);
    }

    [Fact]
    public async Task ValidationInsufficientBalanceAndIllegalPhasesDoNotWriteOrUnlockPrincipal()
    {
        var db = Database();
        await Seed(db, "alice", 3m);
        var store = new FirestorePaidCrapsStore(db, () => Dice(1, 3));
        var round = await store.StartAsync("alice", new(1m, null, Key()));
        var unavailable = await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "field", 1m)));
        Assert.Equal("craps-wager-unavailable", unavailable.Code);
        var required = await Assert.ThrowsAsync<CrapsPaidException>(() => store.RollAsync("alice", round.RoundId, new(Key())));
        Assert.Equal("craps-revision-required", required.Code);
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        foreach (var invalid in new[] { -1m, 0.5m, 101m })
            Assert.Equal("craps-invalid-wager", (await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "odds", invalid)))).Code);
        Assert.Equal("craps-invalid-bets", (await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "pass-line", 2m)))).Code);
        Assert.Equal("craps-insufficient-credits", (await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "odds", 3m)))).Code);
        Assert.Equal("craps-wager-unchanged", (await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "field", 0m)))).Code);
        Assert.Equal(2, (await db.Collection("crapsPaidOperations").GetSnapshotAsync()).Count);
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        var oddsRequest = WagerRequest(round, "odds", 2m);
        var odds = await store.SetWagerAsync("alice", round.RoundId, oddsRequest);
        Assert.Equal(0, odds.WalletBalanceCents);
        Assert.Equal("craps-operation-conflict", (await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId, oddsRequest with { Stake = 1m }))).Code);
        var removed = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(odds, "odds", 0m));
        Assert.Equal(200, removed.WalletBalanceCents);
        Assert.Equal(100, removed.TotalWagerCents);
        var final = await store.RollAsync("alice", round.RoundId, RollRequest(removed));
        Assert.Equal(1m, final.Stake);
        Assert.Equal("craps-wager-unavailable", (await Assert.ThrowsAsync<CrapsPaidException>(() => store.SetWagerAsync("alice", round.RoundId, WagerRequest(final, "field", 1m)))).Code);
    }

    [Fact]
    public async Task HistoricalFiveFieldExtrasRemainReadableAndCanContinueWithoutRewritingOutcomes()
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var draws = new Queue<DicePair>([Dice(1, 3), Dice(2, 3), Dice(3, 4)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var round = await store.StartAsync("alice", new(1m, [new("field", 1m)], Key()));
        var reference = db.Collection("crapsPaidRounds").Document(round.RoundId.ToString("N"));
        var json = JsonNode.Parse((await reference.GetSnapshotAsync()).GetValue<string>("roundJson"))!.AsObject();
        json.Remove("Actions");
        json.Remove("SchemaVersion");
        foreach (var extra in json["Extras"]!.AsArray())
            foreach (var name in new[] { "WagerId", "PlacedForRoll", "ResolvedOnRoll", "Cancelled" }) extra!.AsObject().Remove(name);
        await reference.UpdateAsync(new Dictionary<string, object> { ["roundJson"] = json.ToJsonString(), ["schemaVersion"] = 1L });
        round = await store.GetAsync("alice", round.RoundId);
        Assert.Null(round.ExtraBets.Single().WagerId);
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        var historical = round.ExtraBets.Single();
        Assert.Equal(2m, historical.TotalReturn);
        Assert.Null(historical.ResolvedOnRoll);
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "field", 2m));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        Assert.Equal(historical, round.ExtraBets[0]);
        Assert.Equal(2, round.ExtraBets[1].ResolvedOnRoll);
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        Assert.Equal("resolved", round.Phase);
        Assert.Equal(historical, (await store.GetAsync("alice", round.RoundId)).ExtraBets[0]);
    }

    [Theory]
    [InlineData(1, 3, 100, 30000)]
    [InlineData(2, 3, 1, 250)]
    [InlineData(2, 4, 1, 220)]
    public async Task OddsEndpointPreservesHundredRandLimitTrueOddsAndRefundSemantics(int first, int second, decimal stake, long oddsReturn)
    {
        var db = Database();
        await Seed(db, "alice", 1000m);
        var draws = new Queue<DicePair>([Dice(first, second), Dice(first, second)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var round = await store.StartAsync("alice", new(1m, null, Key()));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        var request = new PlaceCrapsOddsRequest(stake, Key(), round.Revision, round.Rolls.Count + 1);
        round = await store.PlaceOddsAsync("alice", round.RoundId, request);
        Assert.Equal(99900 - stake * 100m, round.WalletBalanceCents);
        Assert.Equal(round.WalletBalanceCents, (await store.PlaceOddsAsync("alice", round.RoundId, request)).WalletBalanceCents);
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        Assert.Equal(oddsReturn / 100m, round.ExtraBets.Single().TotalReturn);
        Assert.Equal(99900 - stake * 100m + oddsReturn + 200, round.WalletBalanceCents);
    }

    [Fact]
    public async Task HistoricalOperationFingerprintsAndFiveFieldResponsesReplayAfterRoundCompletion()
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var draws = new Queue<DicePair>([Dice(1, 3), Dice(1, 3)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var start = new StartCrapsRoundRequest(1m, [new("field", 2m)], Key());
        var round = await store.StartAsync("alice", start);
        var startReference = Receipt(db, "alice", start.OperationId!);
        var legacyStartFingerprint = Hash(JsonSerializer.Serialize(new object[]
            { "start", 1m, new[] { new LegacyCrapsExtra("field", 2m, false, false, null) } }));
        Assert.Equal(legacyStartFingerprint, (await startReference.GetSnapshotAsync()).GetValue<string>("fingerprint"));
        await startReference.UpdateAsync("responseJson", LegacyResponse(round));
        var firstRoll = RollRequest(round);
        round = await store.RollAsync("alice", round.RoundId, firstRoll);
        var pointed = round;
        var odds = new PlaceCrapsOddsRequest(2m, Key(), round.Revision, round.Rolls.Count + 1);
        round = await store.PlaceOddsAsync("alice", round.RoundId, odds);
        var placed = round;
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        var ledgerCount = (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count;
        var firstReceipt = Receipt(db, "alice", firstRoll.OperationId!);
        await firstReceipt.UpdateAsync(new Dictionary<string, object>
        {
            ["fingerprint"] = Hash(JsonSerializer.Serialize(new object[] { "roll", round.RoundId })),
            ["responseJson"] = LegacyResponse(pointed),
        });
        var oddsReceipt = Receipt(db, "alice", odds.OperationId!);
        await oddsReceipt.UpdateAsync(new Dictionary<string, object>
        {
            ["fingerprint"] = Hash(JsonSerializer.Serialize(new object[] { "odds", round.RoundId, 2m })),
            ["responseJson"] = LegacyResponse(placed),
        });
        Assert.Equal("come-out", (await store.StartAsync("alice", start)).Phase);
        var oldRoll = await store.RollAsync("alice", round.RoundId, new(firstRoll.OperationId));
        Assert.Equal(pointed.WalletBalanceCents, oldRoll.WalletBalanceCents);
        Assert.Equal(pointed.Revision, oldRoll.Revision);
        Assert.Equal(pointed.TotalWagerCents, oldRoll.TotalWagerCents);
        Assert.Equal(pointed.TotalReturnCents, oldRoll.TotalReturnCents);
        Assert.Null(oldRoll.ExtraBets.Single().WagerId);
        var oldOdds = await store.PlaceOddsAsync("alice", round.RoundId, new(2m, odds.OperationId));
        Assert.Equal(placed.WalletBalanceCents, oldOdds.WalletBalanceCents);
        Assert.Equal(placed.Revision, oldOdds.Revision);
        Assert.Equal(placed.TotalWagerCents, oldOdds.TotalWagerCents);
        Assert.Equal(placed.TotalReturnCents, oldOdds.TotalReturnCents);
        Assert.Equal(round.Revision, (await store.GetAsync("alice", round.RoundId)).Revision);
        Assert.Equal(round.WalletBalanceCents, (await store.StatusAsync("alice")).WalletBalanceCents);
        Assert.Equal(ledgerCount, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Empty(draws);
    }

    [Fact]
    public async Task ReloadsAndWagerChangesPreserveAcceptedDiceAndRejectLostHistoricalDiceWithoutRepair()
    {
        var db = Database();
        await Seed(db, "alice", 100m);
        var draws = new Queue<DicePair>([Dice(2, 2), Dice(1, 2), Dice(2, 2)]);
        var store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        var round = await store.StartAsync("alice", new(1m, null, Key()));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        Assert.Equal((2, 2), (round.Rolls[0].First, round.Rolls[0].Second));
        store = new FirestorePaidCrapsStore(db, draws.Dequeue);
        round = await store.GetAsync("alice", round.RoundId);
        Assert.Equal((2, 2), (round.Rolls[0].First, round.Rolls[0].Second));
        round = await store.PlaceOddsAsync("alice", round.RoundId, new(2m, Key(), round.Revision, 2));
        Assert.Equal((2, 2), (round.LastOutcome!.First, round.LastOutcome.Second));
        round = await store.SetWagerAsync("alice", round.RoundId, WagerRequest(round, "field", 1m));
        round = (await store.ActiveAsync("alice"))!;
        Assert.Equal((2, 2), (round.Rolls[0].First, round.Rolls[0].Second));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        Assert.Equal(new[] { (2, 2), (1, 2) }, round.Rolls.Select(roll => (roll.First, roll.Second)));
        round = await store.GetAsync("alice", round.RoundId);
        Assert.Equal((1, 2), (round.LastOutcome!.First, round.LastOutcome.Second));
        round = await store.RollAsync("alice", round.RoundId, RollRequest(round));
        round = await store.GetAsync("alice", round.RoundId);
        Assert.Equal(new[] { (2, 2), (1, 2), (2, 2) }, round.Rolls.Select(roll => (roll.First, roll.Second)));
        var reference = db.Collection("crapsPaidRounds").Document(round.RoundId.ToString("N"));
        var json = JsonNode.Parse((await reference.GetSnapshotAsync()).GetValue<string>("roundJson"))!.AsObject();
        json["State"]!["Rolls"]![0]!["First"]!["Value"] = 0;
        var corrupt = json.ToJsonString();
        await reference.UpdateAsync("roundJson", corrupt);
        var ledgerCount = (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync("alice", round.RoundId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RollAsync("alice", round.RoundId, RollRequest(round)));
        Assert.Equal(corrupt, (await reference.GetSnapshotAsync()).GetValue<string>("roundJson"));
        Assert.Equal(ledgerCount, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Empty(draws);
    }

    private static string LegacyResponse(CrapsRoundResponse round) => JsonSerializer.Serialize(new
    {
        round.RoundId, round.Stake, round.Phase, round.Point, round.Rolls, round.LastOutcome,
        ExtraBets = round.ExtraBets.Select(extra => new LegacyCrapsExtra(extra.Kind, extra.Stake, extra.Resolved, extra.Won, extra.TotalReturn)).ToArray(),
        round.WalletBalanceCents,
    });
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DocumentReference Receipt(FirestoreDb db, string user, string operationId) =>
        db.Collection("crapsPaidOperations").Document(Hash(user + "\n" + operationId));
    private sealed record LegacyCrapsExtra(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn);

    private static CrapsOperationRequest RollRequest(CrapsRoundResponse round) => new(Key(), round.Revision, round.Rolls.Count + 1);
    private static SetCrapsWagerRequest WagerRequest(CrapsRoundResponse round, string kind, decimal amount) => new(kind, amount, Key(), round.Revision, round.Rolls.Count + 1);
    private static async Task<(CrapsRoundResponse? Round, CrapsPaidException? Error)> Capture(Func<Task<CrapsRoundResponse>> action)
    {
        try { return (await action(), null); }
        catch (CrapsPaidException error) { return (null, error); }
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-craps-paid-" + Guid.NewGuid().ToString("N"),
        Endpoint = fixture.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure,
        EmulatorDetection = EmulatorDetection.None,
    }.Build();

    private static async Task Seed(FirestoreDb db, string userId, decimal balance) =>
        await db.Collection("userBalances").Document(userId + "_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = (long)balance, ["availableFractionalCents"] = 0L,
        });
    private static DicePair Dice(int first, int second) => new(new DieValue(first), new DieValue(second));
    private static string Key() => Guid.NewGuid().ToString("N");
}
