using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Api.Gax;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Tests.Firestore;

public sealed class FirestoreWalletEvidenceTests
{
    [FirestoreEmulatorFact]
    public async Task Legacy_reads_integer_cents_and_whole_cent_rand_from_the_selected_document()
    {
        var database = OpenDatabase();
        var ledger = database.Collection("balanceTransactions");
        var debit = ledger.Document("debit");
        var credit = ledger.Document("credit");
        var archived = database.Collection("archivedEntries").Document("int-rand");
        var batch = database.StartBatch();
        batch.Set(debit, Fixture("wager", "amountCents", long.MinValue));
        batch.Set(credit, Fixture("payout", "amount", 12.29d));
        batch.Set(archived, Fixture("wager", "amount", -7L));
        await batch.CommitAsync();

        var entries = (await database.GetAllSnapshotsAsync([debit, credit, archived]))
            .Select(snapshot => FirestoreWalletEvidence.Legacy(snapshot, "round-a"))
            .ToDictionary(entry => entry.DocumentId);

        Assert.Equal(3, entries.Count);
        Assert.Equal(new RoundAuditLedgerEntry("balanceTransactions", "debit", "alice", "wager", "round-a",
            long.MinValue, RoundAuditFinancialProof.LegacyHost), entries["debit"]);
        Assert.Equal(new RoundAuditLedgerEntry("balanceTransactions", "credit", "alice", "payout", "round-a",
            1229, RoundAuditFinancialProof.LegacyHost), entries["credit"]);
        Assert.Equal(new RoundAuditLedgerEntry("archivedEntries", "int-rand", "alice", "wager", "round-a",
            -700, RoundAuditFinancialProof.LegacyHost), entries["int-rand"]);
    }

    [FirestoreEmulatorFact]
    public async Task Legacy_rejects_missing_incomplete_and_noncanonical_money_rows()
    {
        var database = OpenDatabase();
        var ledger = database.Collection("balanceTransactions");
        var invalid = new Dictionary<string, object>[]
        {
            new(Fixture("wager", "amountCents", -500L)) { ["currencyId"] = "practice" },
            new(Fixture("wager", "amountCents", -500L)) { ["userId"] = "" },
            Fixture(" ", "amountCents", -500L),
            new(Fixture("wager", "amountCents", -500d)) { ["amount"] = -5d },
            Fixture("payout", "amount", 0.041d),
            Fixture("payout", "amount", Math.BitIncrement(0.29d)),
            Fixture("payout", "amount", double.MaxValue),
            Fixture("payout", "amount", long.MaxValue),
        };

        var batch = database.StartBatch();
        var documents = new List<DocumentReference> { ledger.Document("missing") };
        foreach (var fields in invalid)
        {
            var document = ledger.Document();
            documents.Add(document);
            batch.Set(document, fields);
        }
        await batch.CommitAsync();

        var snapshots = await database.GetAllSnapshotsAsync(documents);
        Assert.Equal(documents.Count, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Throws<GameWalletUnavailableException>(
            () => FirestoreWalletEvidence.Legacy(snapshot, "round-a")));
    }

    private static Dictionary<string, object> Fixture(string kind, string amountField, object value) => new()
    {
        ["currencyId"] = "slotsCredits",
        ["userId"] = "alice",
        ["type"] = kind,
        [amountField] = value,
    };

    private static FirestoreDb OpenDatabase() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-wallet-evidence-{Guid.NewGuid():N}",
        EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build();

    private sealed class FirestoreEmulatorFactAttribute : FactAttribute
    {
        public FirestoreEmulatorFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
                Skip = "The Firestore emulator is required.";
        }
    }
}
