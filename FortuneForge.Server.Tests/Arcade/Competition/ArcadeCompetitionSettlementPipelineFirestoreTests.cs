using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class ArcadeCompetitionSettlementPipelineFirestoreTests
{
    private readonly SolitaireFirestoreEmulatorFixture fixture;

    public ArcadeCompetitionSettlementPipelineFirestoreTests(
        SolitaireFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task RealPipelineDebitsCompletesPlansCreditsAndRetriesWithoutSecondPayout()
    {
        var database = CreateDatabase();
        var competition = Identity();
        await SeedBalanceAsync(database, "alpha", 500);
        await SeedBalanceAsync(database, "bravo", 500);
        var rulesOptions = new ArcadeCompetitionRulesOptions();
        var paidEntry = new FirestoreArcadeCompetitionPaidEntryCoordinator(database, rulesOptions);
        var store = new FirestoreArcadeCompetitionStore(database);

        _ = await paidEntry.StartPaidAttemptAsync(
            Entry("attempt-alpha", "alpha", Start.AddMinutes(1)),
            default);
        _ = await paidEntry.StartPaidAttemptAsync(
            Entry("attempt-bravo", "bravo", Start.AddMinutes(2)),
            default);
        var alphaCompletion = await store.CompleteAttemptAsync(
            Completion("attempt-alpha", "alpha", 500, Start.AddMinutes(3)),
            default);
        var bravoCompletion = await store.CompleteAttemptAsync(
            Completion("attempt-bravo", "bravo", 100, Start.AddMinutes(4)),
            default);

        Assert.True(alphaCompletion.Attempt.IsCompleted);
        Assert.True(bravoCompletion.Attempt.IsCompleted);
        Assert.Equal(400, await ReadBalanceCentsAsync(database, "alpha"));
        Assert.Equal(400, await ReadBalanceCentsAsync(database, "bravo"));

        var timeProvider = new FixedTimeProvider(End);
        var planner = new ArcadeCompetitionSettlementPlanningService(
            store,
            timeProvider,
            rulesOptions);
        var creditor = new FirestoreArcadeCompetitionPayoutCreditor(database);
        var executor = new ArcadeCompetitionSettlementExecutor(
            store,
            planner,
            creditor,
            timeProvider);

        var first = await executor.ExecuteAsync(competition, default);

        Assert.False(first.WasAlreadyCompleted);
        Assert.True(first.SettlementState.IsCompleted);
        Assert.Equal(200, first.PayoutPlan.VisibleJackpotCents);
        Assert.Equal(0, first.PayoutPlan.HouseCutCents);
        var instruction = Assert.Single(first.PayoutPlan.Instructions);
        Assert.Equal(ArcadeCompetitionPayoutKind.Prize, instruction.Kind);
        Assert.Equal("alpha", instruction.PlayerId);
        Assert.Equal(1, instruction.Placement);
        Assert.Equal(200, instruction.AmountCents);
        Assert.Equal(600, await ReadBalanceCentsAsync(database, "alpha"));
        Assert.Equal(400, await ReadBalanceCentsAsync(database, "bravo"));

        var settlement = await store.ReadSettlementStateAsync(competition, default);
        Assert.True(settlement.HasStoredPlan);
        Assert.True(settlement.IsCompleted);
        Assert.Equal(End, settlement.CreatedAtUtc);
        Assert.Equal(End, settlement.CompletedAtUtc);
        Assert.True(first.PayoutPlan.HasSameValueAs(settlement.PayoutPlan!));
        var settlementDocument = await database
            .Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(competition))
            .GetSnapshotAsync();
        Assert.Equal("completed", Field<string>(settlementDocument, "status"));
        var competitionDocument = await database
            .Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection)
            .Document(competition.DocumentId)
            .GetSnapshotAsync();
        Assert.Equal("completed", Field<string>(competitionDocument, "settlementStatus"));

        var ledgersAfterFirst = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(3, ledgersAfterFirst.Count);
        var entryLedgers = ledgersAfterFirst
            .Where(document => Field<string>(document, "type") == "arcade-competition-entry")
            .ToArray();
        Assert.Equal(2, entryLedgers.Length);
        Assert.Single(entryLedgers, document => Field<string>(document, "userId") == "alpha");
        Assert.Single(entryLedgers, document => Field<string>(document, "userId") == "bravo");
        Assert.All(entryLedgers, document => Assert.Equal(-1d, Field<double>(document, "amount")));
        var payoutLedger = Assert.Single(
            ledgersAfterFirst,
            document => Field<string>(document, "type") == "arcade-competition-payout");
        Assert.Equal("alpha", Field<string>(payoutLedger, "userId"));
        Assert.Equal(200, Field<long>(payoutLedger, "amountCents"));
        Assert.Equal(instruction.IdempotencyKey, Field<string>(payoutLedger, "idempotencyKey"));
        Assert.Equal(
            FirestoreArcadeCompetitionPayoutCreditor.TransactionIdFor(instruction.IdempotencyKey),
            payoutLedger.Id);

        var retry = await executor.ExecuteAsync(competition, default);

        Assert.True(retry.WasAlreadyCompleted);
        Assert.Equal(600, await ReadBalanceCentsAsync(database, "alpha"));
        Assert.Equal(400, await ReadBalanceCentsAsync(database, "bravo"));
        Assert.Equal(3, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Equal(3, Field<long>(await BalanceDocument(database, "alpha").GetSnapshotAsync(), "version"));
        Assert.Equal(2, Field<long>(await BalanceDocument(database, "bravo").GetSnapshotAsync(), "version"));
    }

    private FirestoreDb CreateDatabase()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-arcade-settlement-pipeline-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static ArcadeCompetitionPaidEntryRequest Entry(
        string attemptId,
        string playerId,
        DateTimeOffset enteredAtUtc) => new(
            attemptId,
            Identity(),
            playerId,
            100,
            enteredAtUtc);

    private static ArcadeCompetitionAttemptCompletion Completion(
        string attemptId,
        string playerId,
        long score,
        DateTimeOffset completedAtUtc) => new(
            attemptId,
            Identity(),
            playerId,
            score,
            completedAtUtc);

    private static async Task SeedBalanceAsync(FirestoreDb database, string userId, long cents) =>
        await BalanceDocument(database, userId).SetAsync(new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["currencyId"] = "slotsCredits",
            ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100,
            ["reserved"] = 0L,
            ["version"] = 1L,
            ["createdAt"] = Timestamp.FromDateTime(Start.UtcDateTime),
            ["updatedAt"] = Timestamp.FromDateTime(Start.UtcDateTime),
        });

    private static DocumentReference BalanceDocument(FirestoreDb database, string userId) =>
        database.Collection("userBalances").Document($"{userId}_slotsCredits");

    private static async Task<long> ReadBalanceCentsAsync(FirestoreDb database, string userId)
    {
        var snapshot = await BalanceDocument(database, userId).GetSnapshotAsync();
        return checked(Field<long>(snapshot, "available") * 100 +
            Field<long>(snapshot, "availableFractionalCents"));
    }

    private static T Field<T>(DocumentSnapshot document, string name) =>
        document.TryGetValue<T>(name, out var value)
            ? value
            : throw new InvalidOperationException($"Missing {name}.");

    private static readonly DateTimeOffset Start = new(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddDays(1);
    private static ArcadeCompetitionIdentity Identity() => new(
        "asteroids",
        ArcadeCompetitionWindowKind.Daily,
        Start,
        End);

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}
