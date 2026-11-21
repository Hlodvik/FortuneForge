using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Firestore;

public static class FirestoreWalletEvidence
{
    private const int CentsPerRand = 100;

    /// <summary>Decodes a wallet-shaped historical record selected by the caller.</summary>
    /// <exception cref="GameWalletUnavailableException">The record is absent or malformed.</exception>
    public static RoundAuditLedgerEntry Legacy(DocumentSnapshot snapshot, string roundReference)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(roundReference);
        if (!snapshot.Exists) throw new GameWalletUnavailableException();

        var fields = snapshot.ToDictionary();
        if (fields.GetValueOrDefault("currencyId") is not "slotsCredits" ||
            fields.GetValueOrDefault("userId") is not string userId || string.IsNullOrWhiteSpace(userId) ||
            fields.GetValueOrDefault("type") is not string kind || string.IsNullOrWhiteSpace(kind))
            throw new GameWalletUnavailableException();

        return new(snapshot.Reference.Parent.Id, snapshot.Id, userId, kind, roundReference,
            ReadAmountCents(fields), RoundAuditFinancialProof.LegacyHost);
    }

    private static long ReadAmountCents(IReadOnlyDictionary<string, object> fields)
    {
        if (fields.TryGetValue("amountCents", out var explicitAmount))
            return explicitAmount is long cents ? cents : throw new GameWalletUnavailableException();

        try
        {
            var amount = fields.GetValueOrDefault("amount");
            if (amount is long wholeRand) return checked(wholeRand * CentsPerRand);
            if (amount is not double rand || !double.IsFinite(rand)) throw new GameWalletUnavailableException();

            var scaled = (decimal)rand * CentsPerRand;
            if (scaled != decimal.Truncate(scaled) || (double)(scaled / CentsPerRand) != rand)
                throw new GameWalletUnavailableException();

            return checked((long)scaled);
        }
        catch (OverflowException exception)
        {
            throw new GameWalletUnavailableException(exception);
        }
    }
}
