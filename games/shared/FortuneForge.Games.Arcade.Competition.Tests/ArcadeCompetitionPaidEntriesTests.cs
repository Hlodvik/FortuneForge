using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Arcade.Competition;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Arcade.Competition.Tests;

public sealed class ArcadeCompetitionPaidEntriesTests
{
    private const string GameId = "test-arcade";
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
    private static readonly ArcadeCompetitionRulesOptions Rules = new();

    [ArcadeFirestoreFact]
    public async Task AckLossKeepsTheOriginalEntryFencedAndRolloverReplaysOneDebit()
    {
        var database = Database();
        var store = new FirestoreArcadeCompetitionStore(database);
        var wallet = new TestWallet { FailAfterCharging = true };
        var adapter = new TestAdapter();
        var entries = new ArcadeCompetitionPaidEntries<TestRun>(database, GameId, Rules, wallet, adapter);
        var window = Window(0);
        var request = Request(window) with { EnteredAtUtc = window.StartsAtUtc.AddMinutes(4).AddTicks(7) };
        var planner = new ArcadeCompetitionSettlementPlanningService(store, new FrozenTime(window.EndsAtUtc), Rules);

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => entries.StartAsync(request, default));
        Assert.Empty(await store.LoadAttemptsAsync(window, default));
        await Assert.ThrowsAsync<ArcadeCompetitionEntryPendingException>(() => planner.PlanAsync(window, default));
        await Assert.ThrowsAsync<ArcadeCompetitionEntryConflictException>(() => entries.StartAsync(
            request with { AuthenticatedPlayerId = "different-account" }, default));

        var next = Window(1);
        var retry = request with { Competition = next, EnteredAtUtc = next.StartsAtUtc.AddMinutes(20),
            RequestBodySha256 = GameSecurityEventDocuments.Hash("{}") };
        var accepted = await entries.StartAsync(retry, allowNewEntries: false, default);
        var exact = await entries.StartAsync(retry, default);
        Assert.Equal(window.StartsAtUtc.AddMinutes(4), exact.Attempt.EnteredAtUtc);
        Assert.Equal(window, exact.Attempt.Competition);
        Assert.Equal(accepted.Run, exact.Run);
        Assert.True(exact.WasAlreadyRecorded);
        Assert.Equal(1, adapter.Generated);
        Assert.Equal(1, wallet.Debits);
        Assert.Equal(2, wallet.ChargeCalls);
        Assert.Empty(await store.LoadAttemptsAsync(next, default));
        Assert.Equal(window, (await entries.ReadAttemptAsync(accepted.Attempt.DocumentId, "alice", default)).Competition);
        await Assert.ThrowsAsync<ArcadeCompetitionEntryConflictException>(() => entries.ReadAttemptAsync(
            accepted.Attempt.DocumentId, "not-alice", default));
        await Assert.ThrowsAsync<ArcadeCompetitionEntryPausedException>(() => entries.StartAsync(
            retry with { AttemptId = "another-attempt" }, allowNewEntries: false, default));

        var finished = window.EndsAtUtc.AddMinutes(-1);
        var firstScore = await entries.CompleteAsync(window, accepted.Attempt.DocumentId, "alice", finished.AddTicks(9),
            (_, _) => new ArcadeCompetitionRunCompletion<long>(25, attempt => attempt.Score!.Value,
                (transaction, reference, _) => transaction.Update(reference, "score", 25L)), default);
        Assert.Equal(25, firstScore);
        Assert.Equal(finished, (await entries.ReadAttemptAsync(accepted.Attempt.DocumentId, "alice", default)).CompletedAtUtc);
        var frozen = await planner.PlanAsync(window, default);
        Assert.Equal(Rules.EntryFeeCents, Assert.Single(frozen.Instructions).AmountCents);
        Assert.Equal(ArcadeCompetitionPayoutKind.Refund, frozen.Instructions[0].Kind);

        var exactScore = await entries.CompleteAsync(window, accepted.Attempt.DocumentId, "alice", next.EndsAtUtc,
            (_, run) => new ArcadeCompetitionRunCompletion<long>(run.GetValue<long>("score"), attempt => attempt.Score!.Value), default);
        Assert.Equal(firstScore, exactScore);
        await Assert.ThrowsAsync<ArcadeCompetitionSettlementCompletedException>(() => entries.StartAsync(
            request with { AttemptId = "new-after-settlement" }, default));
        Assert.Equal(1, wallet.Debits);
    }

    [ArcadeFirestoreFact]
    public async Task MatchedBodyRecoversAfterRolloverAndChangedExpectedWindowBodyConflicts()
    {
        var database = Database();
        var wallet = new TestWallet { FailAfterCharging = true };
        var entries = new ArcadeCompetitionPaidEntries<TestRun>(database, GameId, Rules, wallet, new TestAdapter());
        var original = Request(Window(0)) with { RequestBodySha256 = new string('a', 64) };

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => entries.StartAsync(original, default));
        var next = Window(1);
        var retry = original with { Competition = next, EnteredAtUtc = next.StartsAtUtc, RequestBodySha256 = new string('A', 64) };
        await Assert.ThrowsAsync<ArcadeCompetitionEntryConflictException>(() => entries.StartAsync(
            retry with { RequestBodySha256 = new string('b', 64) }, allowNewEntries: false, default));
        var accepted = await entries.StartAsync(retry, allowNewEntries: false, default);
        Assert.Equal(original.Competition, accepted.Attempt.Competition);
        await Assert.ThrowsAsync<ArcadeCompetitionEntryConflictException>(() => entries.StartAsync(
            retry with { RequestBodySha256 = null }, default));
        Assert.Equal(1, wallet.Debits);
    }

    [ArcadeFirestoreFact]
    public async Task ADeclineStaysFrozenAndClearsThePlanningFence()
    {
        var database = Database();
        var wallet = new TestWallet { Balance = 50 };
        var entries = new ArcadeCompetitionPaidEntries<TestRun>(database, GameId, Rules, wallet, new TestAdapter());
        var store = new FirestoreArcadeCompetitionStore(database);
        var window = Window(0);
        var request = Request(window);

        var original = await Assert.ThrowsAsync<ArcadeCompetitionInsufficientCreditsException>(() => entries.StartAsync(request, default));
        wallet.Balance = 500;
        var replay = await Assert.ThrowsAsync<ArcadeCompetitionInsufficientCreditsException>(() => entries.StartAsync(request, default));
        Assert.Equal(original.AvailableCents, replay.AvailableCents);
        Assert.Equal(1, wallet.ChargeCalls);
        Assert.Equal(0, wallet.Debits);
        Assert.Empty(await store.LoadAttemptsAsync(window, default));
        var planner = new ArcadeCompetitionSettlementPlanningService(store, new FrozenTime(window.EndsAtUtc), Rules);
        Assert.Empty((await planner.PlanAsync(window, default)).Instructions);
    }

    [ArcadeFirestoreFact]
    public async Task EarlierAttemptUsesTheSignedReadOnlyReceiptWithoutRecharging()
    {
        var database = Database();
        var adapter = new TestAdapter();
        var request = Request(Window(0));
        var store = new FirestoreArcadeCompetitionStore(database);
        var start = await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart(request.AttemptId,
            request.Competition, request.AuthenticatedPlayerId, Rules.EntryFeeCents, request.EnteredAtUtc), default);
        await adapter.RunDocument(database, start.Attempt).SetAsync(new Dictionary<string, object> { ["seed"] = 391L });
        var wallet = new TestWallet { LegacyVerified = true };
        var entries = new ArcadeCompetitionPaidEntries<TestRun>(database, GameId, Rules, wallet, adapter);

        var next = Window(1);
        var recovered = await entries.StartAsync(request with { Competition = next, EnteredAtUtc = next.StartsAtUtc }, default);
        Assert.Equal(new TestRun(391), recovered.Run);
        Assert.True(recovered.WasAlreadyRecorded);
        Assert.Equal($"arcade-competition-{start.Attempt.DocumentId}-entry", wallet.LegacyEntry?.TransactionId);
        Assert.Equal(0, wallet.ChargeCalls);
        wallet.LegacyVerified = false;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => entries.StartAsync(request, default));
        Assert.Equal(0, wallet.ChargeCalls);
    }

    [ArcadeFirestoreFact]
    public async Task AHistoricalPaidFeeRatherThanTheCurrentFeeSetsTheRefund()
    {
        var database = Database();
        var store = new FirestoreArcadeCompetitionStore(database);
        var window = Window(0);
        await store.StartAttemptAsync(new ArcadeCompetitionAttemptStart("legacy-75c", window,
            "alice", 75, window.StartsAtUtc.AddMinutes(1)), default);

        var plan = await new ArcadeCompetitionSettlementPlanningService(store, new FrozenTime(window.EndsAtUtc), Rules)
            .PlanAsync(window, default);
        Assert.Equal(75, plan.VisibleJackpotCents);
        Assert.Equal(75, Assert.Single(plan.Instructions).AmountCents);
    }

    [ArcadeFirestoreFact]
    public async Task AWindowKeepsItsOwnFeeAndPrizeTermsAcrossConfigurationChanges()
    {
        var database = Database();
        var window = Window(0);
        var original = Rules with { EntryFeeCents = 75, HouseCutCapBasisPoints = 0,
            PrizeSchedules = Rules.PrizeSchedules.SetItem(2, [8_000, 2_000]) };
        var adapter = new TestAdapter();
        var wallet = new TestWallet { Balance = 1_000 };
        var before = new ArcadeCompetitionPaidEntries<TestRun>(database, GameId, original, wallet, adapter);
        await before.StartAsync(Request(window) with { EntryFeeCents = 75 }, default);
        var after = new ArcadeCompetitionPaidEntries<TestRun>(database, GameId, Rules, wallet, adapter);
        foreach (var actor in new[] { "bob", "carl", "diana", "elena" })
        {
            var accepted = await after.StartAsync(Request(window) with { AttemptId = $"entry-{actor}", AuthenticatedPlayerId = actor }, default);
            Assert.Equal(75, accepted.Attempt.EntryFeeCents);
        }

        var store = new FirestoreArcadeCompetitionStore(database);
        var clock = new FrozenTime(window.EndsAtUtc);
        var plan = await new ArcadeCompetitionSettlementPlanningService(store, clock, Rules).PlanAsync(window, default);
        Assert.Equal(375, plan.VisibleJackpotCents);
        Assert.Equal(new long[] { 300, 75 }, plan.Instructions.Select(instruction => instruction.AmountCents));
        var standings = await new ArcadeCompetitionService(store, clock, Rules).GetSnapshotAsync(window, default);
        Assert.Equal(plan.VisibleJackpotCents, standings.VisibleJackpotCents);
        Assert.Equal(0, standings.HouseCutCents);
        Assert.False(standings.IsAcceptingAttempts);
    }

    private static ArcadeCompetitionIdentity Window(int offset) => new(GameId, ArcadeCompetitionWindowKind.Daily,
        Start.AddDays(offset), Start.AddDays(offset + 1));

    private static ArcadeCompetitionPaidEntryRequest Request(ArcadeCompetitionIdentity window) =>
        new("account-entry-request", window, "alice", Rules.EntryFeeCents, window.StartsAtUtc.AddMinutes(4));

    private static FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-arcade-entry-{Guid.NewGuid():N}",
        EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build();

    private sealed record TestRun(long Seed);

    private sealed class TestAdapter : IArcadeCompetitionRunAdapter<TestRun>
    {
        public int Generated { get; private set; }
        public DocumentReference RunDocument(FirestoreDb database, ArcadeCompetitionAttemptRecord attempt) =>
            database.Collection("testArcadeRuns").Document(attempt.DocumentId);
        public TestRun PrepareRun(ArcadeCompetitionAttemptRecord _) => new(++Generated);
        public TestRun ReadRun(DocumentSnapshot snapshot, ArcadeCompetitionAttemptRecord _) => new(snapshot.GetValue<long>("seed"));
        public void WriteRun(Transaction transaction, FirestoreDb _, DocumentReference document,
            ArcadeCompetitionAttemptRecord attempt, TestRun run, string entryLedgerId) => transaction.Create(document,
                new Dictionary<string, object> { ["seed"] = run.Seed, ["feeLedgerId"] = entryLedgerId, ["playerId"] = attempt.PlayerId });
    }

    private sealed class TestWallet : IArcadeCompetitionEntryWallet
    {
        private readonly Dictionary<string, (ArcadeCompetitionEntryCharge Charge, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance { get; set; } = 500;
        public bool FailAfterCharging { get; set; }
        public bool LegacyVerified { get; set; }
        public int ChargeCalls { get; private set; }
        public int Debits { get; private set; }
        public ArcadeCompetitionEntryDebit? LegacyEntry { get; private set; }

        public Task<GameWalletSettlementReceipt> ChargeAsync(ArcadeCompetitionEntryCharge charge, CancellationToken _)
        {
            ChargeCalls++;
            var entry = charge.Entry;
            var id = GameWalletSettlementIdentity.CreateId(entry.GameId, entry.PlayerId, entry.AttemptDocumentId);
            if (receipts.TryGetValue(id, out var prior))
            {
                if (prior.Charge != charge) throw new GameWalletConflictException();
                return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
            }
            var before = Balance;
            var declined = before < entry.EntryFeeCents;
            if (!declined)
            {
                Debits++;
                Balance -= entry.EntryFeeCents;
            }
            var receipt = new GameWalletSettlementReceipt(id, declined ? null : entry.TransactionId, null,
                before, Balance, Balance, entry.EnteredAtUtc.AddSeconds(1), false, declined);
            receipts.Add(id, (charge, receipt));
            if (FailAfterCharging) throw new GameWalletUnavailableException();
            return Task.FromResult(receipt);
        }

        public Task<bool> VerifyLegacyChargeAsync(ArcadeCompetitionEntryDebit entry, CancellationToken _)
        {
            LegacyEntry = entry;
            return Task.FromResult(LegacyVerified);
        }
    }

    private sealed class FrozenTime(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
    }

    private sealed class ArcadeFirestoreFactAttribute : FactAttribute
    {
        public ArcadeFirestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
