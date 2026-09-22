using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreArcadeCompetitionPayoutCreditorTests
{
    private readonly SolitaireFirestoreEmulatorFixture fixture;

    public FirestoreArcadeCompetitionPayoutCreditorTests(SolitaireFirestoreEmulatorFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task PrizeCreditAtomicallyIncreasesBalanceAndWritesCompetitionLedgerData()
    {
        var (database, creditor) = CreateCreditor();
        var instruction = PrizeInstruction();
        await SeedBalanceAsync(database, instruction.PlayerId, 250);
        var creditedAt = End.AddMinutes(1);

        var result = await creditor.CreditAsync(Identity(), instruction, creditedAt, default);

        Assert.False(result.WasAlreadyCredited);
        Assert.Equal(
            FirestoreArcadeCompetitionPayoutCreditor.TransactionIdFor(instruction.IdempotencyKey),
            result.TransactionId);
        Assert.Equal(450, result.BalanceAfterCents);
        Assert.Equal(450, await ReadBalanceCentsAsync(database, instruction.PlayerId));
        var balance = await BalanceDocument(database, instruction.PlayerId).GetSnapshotAsync();
        Assert.Equal(2, Field<long>(balance, "version"));

        var ledger = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(result.TransactionId, ledger.Id);
        Assert.Equal(instruction.PlayerId, Field<string>(ledger, "userId"));
        Assert.Equal("slotsCredits", Field<string>(ledger, "currencyId"));
        Assert.Equal(200, Field<long>(ledger, "amountCents"));
        Assert.Equal(2d, Field<double>(ledger, "amount"));
        Assert.Equal(450, Field<long>(ledger, "balanceAfterCents"));
        Assert.Equal(4.5d, Field<double>(ledger, "balanceAfter"));
        Assert.Equal("arcade-competition-payout", Field<string>(ledger, "type"));
        Assert.Equal(Identity().DocumentId, Field<string>(ledger, "competitionId"));
        Assert.Equal("asteroids", Field<string>(ledger, "gameId"));
        Assert.Equal("daily", Field<string>(ledger, "windowKind"));
        Assert.Equal("prize", Field<string>(ledger, "payoutKind"));
        Assert.Equal(1, Field<long>(ledger, "placement"));
        Assert.Equal(instruction.IdempotencyKey, Field<string>(ledger, "idempotencyKey"));
        Assert.Equal(creditedAt, new DateTimeOffset(Field<Timestamp>(ledger, "createdAt").ToDateTime()));
    }

    [Fact]
    public async Task RefundCreditPreservesRefundKindAndNullPlacement()
    {
        var (database, creditor) = CreateCreditor();
        var instruction = RefundInstruction();
        await SeedBalanceAsync(database, instruction.PlayerId, 25);

        var result = await creditor.CreditAsync(Identity(), instruction, End, default);

        Assert.Equal(225, result.BalanceAfterCents);
        var ledger = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal("refund", Field<string>(ledger, "payoutKind"));
        Assert.Null(ledger.ToDictionary()["placement"]);
        Assert.Equal(200, Field<long>(ledger, "amountCents"));
    }

    [Fact]
    public async Task ReplayReturnsExistingOutcomeWithExactlyOneCreditAndLedgerRecord()
    {
        var (database, creditor) = CreateCreditor();
        var instruction = PrizeInstruction();
        await SeedBalanceAsync(database, instruction.PlayerId, 100);

        var first = await creditor.CreditAsync(Identity(), instruction, End, default);
        var replay = await creditor.CreditAsync(Identity(), instruction, End.AddHours(1), default);

        Assert.False(first.WasAlreadyCredited);
        Assert.True(replay.WasAlreadyCredited);
        Assert.Equal(first.TransactionId, replay.TransactionId);
        Assert.Equal(first.BalanceAfterCents, replay.BalanceAfterCents);
        Assert.Equal(first.CreditedAtUtc, replay.CreditedAtUtc);
        Assert.Equal(300, await ReadBalanceCentsAsync(database, instruction.PlayerId));
        Assert.Equal(2, Field<long>(await BalanceDocument(database, instruction.PlayerId).GetSnapshotAsync(), "version"));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task TamperedExistingLedgerIsRejectedWithoutAnotherBalanceIncrease()
    {
        var (database, creditor) = CreateCreditor();
        var instruction = PrizeInstruction();
        await SeedBalanceAsync(database, instruction.PlayerId, 100);
        var first = await creditor.CreditAsync(Identity(), instruction, End, default);
        await database.Collection("balanceTransactions").Document(first.TransactionId)
            .UpdateAsync("amountCents", instruction.AmountCents + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            creditor.CreditAsync(Identity(), instruction, End.AddHours(1), default));

        Assert.Equal(300, await ReadBalanceCentsAsync(database, instruction.PlayerId));
        Assert.Equal(2, Field<long>(await BalanceDocument(database, instruction.PlayerId).GetSnapshotAsync(), "version"));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task MissingAndInvalidBalancesAreRejectedWithoutLedgerWrites()
    {
        var (missingDatabase, missingCreditor) = CreateCreditor();
        var instruction = PrizeInstruction();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            missingCreditor.CreditAsync(Identity(), instruction, End, default));
        Assert.Empty((await missingDatabase.Collection("balanceTransactions").GetSnapshotAsync()).Documents);

        var (invalidDatabase, invalidCreditor) = CreateCreditor();
        await SeedBalanceAsync(invalidDatabase, instruction.PlayerId, 100);
        await BalanceDocument(invalidDatabase, instruction.PlayerId)
            .UpdateAsync("availableFractionalCents", 100L);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invalidCreditor.CreditAsync(Identity(), instruction, End, default));
        Assert.Empty((await invalidDatabase.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(100, Field<long>(
            await BalanceDocument(invalidDatabase, instruction.PlayerId).GetSnapshotAsync(),
            "availableFractionalCents"));
    }

    private (FirestoreDb Database, FirestoreArcadeCompetitionPayoutCreditor Creditor) CreateCreditor()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-arcade-payout-credit-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        return (database, new FirestoreArcadeCompetitionPayoutCreditor(database));
    }

    private static ArcadeCompetitionCreditInstruction PrizeInstruction() =>
        Assert.Single(Plan([
            new ArcadeCompetitionAttempt("alpha", 200),
            new ArcadeCompetitionAttempt("bravo", 100),
        ]).Instructions);

    private static ArcadeCompetitionCreditInstruction RefundInstruction() =>
        Assert.Single(Plan([
            new ArcadeCompetitionAttempt("solo", 20),
            new ArcadeCompetitionAttempt("solo", 10),
        ]).Instructions);

    private static ArcadeCompetitionPayoutPlan Plan(IEnumerable<ArcadeCompetitionAttempt> attempts)
    {
        var competition = Identity();
        var result = ArcadeCompetitionRules.Evaluate(
            new ArcadeCompetitionWindow(
                competition.WindowKind,
                competition.StartsAtUtc,
                competition.EndsAtUtc),
            attempts);
        return ArcadeCompetitionPayoutPlan.Create(competition, result);
    }

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
        return checked(Field<long>(snapshot, "available") * 100 + Field<long>(snapshot, "availableFractionalCents"));
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
}
