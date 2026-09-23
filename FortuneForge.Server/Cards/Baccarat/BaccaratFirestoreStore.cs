using System.Collections;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Baccarat;

internal sealed class BaccaratFirestoreStore(FirestoreDb database) : IBaccaratStore
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string AvailableFractionalCentsField = "availableFractionalCents";

    public Task<BaccaratStoreResult> StartAsync(
        string userId,
        string idempotencyKey,
        BaccaratBetSide betSide,
        long stakeCents,
        IReadOnlyList<PlayingCard> shuffledShoe,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shuffledShoe);
        var roundId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var roundReference = RoundDocument(roundId);
        var balanceReference = BalanceDocument(userId);
        var wagerReference = BalanceTransactionDocument($"baccarat-{roundId}-wager");
        var payoutReference = BalanceTransactionDocument($"baccarat-{roundId}-payout");
        var eventReference = RoundEventDocument(roundId);
        var shoeReference = ShoeDocument(userId);

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(roundReference, cancellationToken),
                transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                transaction.GetSnapshotAsync(wagerReference, cancellationToken),
                transaction.GetSnapshotAsync(payoutReference, cancellationToken),
                transaction.GetSnapshotAsync(eventReference, cancellationToken),
                transaction.GetSnapshotAsync(shoeReference, cancellationToken));
            var balanceCents = ReadBalanceCents(snapshots[1]);
            if (snapshots[0].Exists)
            {
                var existing = ReadRound(snapshots[0]);
                if (existing.UserId != userId || existing.BetSide != betSide || ToCents(existing.Settlement.Stake) != stakeCents)
                    throw new BaccaratRoundConflictException("This Idempotency-Key was already used with a different Baccarat request.");
                var returnedCents = ToCents(existing.Settlement.TotalReturn);
                if (!snapshots[2].Exists || !snapshots[4].Exists || (returnedCents > 0) != snapshots[3].Exists)
                    throw new InvalidOperationException("A recorded Baccarat round is missing its ledger.");
                return new BaccaratStoreResult(existing, balanceCents);
            }
            if (snapshots[2].Exists || snapshots[3].Exists || snapshots[4].Exists)
                throw new InvalidOperationException("A Baccarat ledger already exists without its round.");
            if (balanceCents < stakeCents)
                throw new BaccaratInsufficientCreditsException(balanceCents, stakeCents);

            var shoe = snapshots[5].Exists ? ReadShoe(snapshots[5], userId) : new BaccaratShoe(shuffledShoe.ToArray(), 0);
            if (shoe.NextIndex >= 312) shoe = new BaccaratShoe(shuffledShoe.ToArray(), 0);
            var round = PuntoBancoRoundDealer.Deal(shoe.Cards.Skip(shoe.NextIndex).ToArray());
            var nextShoeIndex = checked(shoe.NextIndex + round.ConsumedCards.Length);
            var stake = BaccaratMoney.ToRand(stakeCents);
            var settlement = PuntoBancoPaytable.Settle(betSide, stake, round);
            var returnedCentsForRound = ToCents(settlement.TotalReturn);
            var balanceAfterWagerCents = checked(balanceCents - stakeCents);
            var finalBalanceCents = checked(balanceAfterWagerCents + returnedCentsForRound);
            var stored = new BaccaratStoreRound(roundId, userId, betSide, round, settlement, nextShoeIndex, shoe.Cards.Count - nextShoeIndex);

            transaction.Create(roundReference, RoundData(stored, idempotencyKey, nowUtc));
            transaction.Set(shoeReference, ShoeData(userId, shoe.Cards, nextShoeIndex, nowUtc));
            transaction.Set(balanceReference, BalanceUpdate(finalBalanceCents, nowUtc), SetOptions.MergeAll);
            transaction.Create(wagerReference, BalanceTransactionData(
                wagerReference.Id, userId, -stakeCents, balanceAfterWagerCents, "baccarat-wager", idempotencyKey, nowUtc));
            if (returnedCentsForRound > 0)
            {
                transaction.Create(payoutReference, BalanceTransactionData(
                    payoutReference.Id, userId, returnedCentsForRound, finalBalanceCents, "baccarat-payout", $"{idempotencyKey}-payout", nowUtc));
            }
            transaction.Create(eventReference, RoundEventData(roundId, userId, idempotencyKey, nowUtc));
            return new BaccaratStoreResult(stored, finalBalanceCents);
        }, cancellationToken: cancellationToken);
    }

    public async Task<BaccaratStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(
            RoundDocument(roundId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId).GetSnapshotAsync(cancellationToken));
        if (!snapshots[0].Exists) return null;
        var stored = ReadRound(snapshots[0]);
        return stored.UserId == userId
            ? new BaccaratStoreResult(stored, ReadBalanceCents(snapshots[1]))
            : null;
    }

    internal static string CreateLookupKey(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private DocumentReference RoundDocument(string roundId) => database.Collection("baccaratRounds").Document(roundId);
    private DocumentReference RoundEventDocument(string roundId) => database.Collection("baccaratRoundEvents").Document($"{roundId}-settled");
    private DocumentReference ShoeDocument(string userId) => database.Collection("baccaratShoes").Document(CreateLookupKey(userId));
    private DocumentReference BalanceDocument(string userId) => database.Collection("userBalances").Document($"{userId}_{SlotsCreditsCurrencyId}");
    private DocumentReference BalanceTransactionDocument(string transactionId) => database.Collection("balanceTransactions").Document(transactionId);

    private static Dictionary<string, object> RoundData(BaccaratStoreRound stored, string idempotencyKey, DateTimeOffset nowUtc) => new()
    {
        ["roundId"] = stored.RoundId,
        ["userId"] = stored.UserId,
        ["betSide"] = BaccaratService.BetSideName(stored.BetSide),
        ["stakeCents"] = ToCents(stored.Settlement.Stake),
        ["consumedCards"] = stored.Round.ConsumedCards.Select(CardCode.Format).ToArray(),
        ["outcome"] = BaccaratService.OutcomeName(stored.Round.Outcome),
        ["endedOnNatural"] = stored.Round.EndedOnNatural,
        ["disposition"] = BaccaratService.DispositionName(stored.Settlement.Disposition),
        ["profitCents"] = ToCents(stored.Settlement.Profit),
        ["totalReturnCents"] = ToCents(stored.Settlement.TotalReturn),
        ["shoeCardsUsed"] = (long)stored.ShoeCardsUsed,
        ["shoeCardsRemaining"] = (long)stored.ShoeCardsRemaining,
        ["startIdempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static Dictionary<string, object> ShoeData(
        string userId,
        IReadOnlyList<PlayingCard> cards,
        int nextIndex,
        DateTimeOffset nowUtc) => new()
    {
        ["userId"] = userId,
        ["cards"] = cards.Select(CardCode.Format).ToArray(),
        ["nextIndex"] = (long)nextIndex,
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static Dictionary<string, object> RoundEventData(string roundId, string userId, string idempotencyKey, DateTimeOffset nowUtc) => new()
    {
        ["roundId"] = roundId,
        ["userId"] = userId,
        ["event"] = "settled",
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static Dictionary<string, object> BalanceUpdate(long balanceCents, DateTimeOffset nowUtc) => new()
    {
        ["available"] = balanceCents / RandMoney.CentsPerRand,
        [AvailableFractionalCentsField] = balanceCents % RandMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId,
        string userId,
        long amountCents,
        long balanceAfterCents,
        string type,
        string idempotencyKey,
        DateTimeOffset nowUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["amount"] = (double)BaccaratMoney.ToRand(amountCents),
        ["balanceAfter"] = (double)BaccaratMoney.ToRand(balanceAfterCents),
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static BaccaratStoreRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("roundId", out var roundId) || roundId.Length != 64 ||
            !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) ||
            !snapshot.TryGetValue<string>("betSide", out var rawBetSide) ||
            !snapshot.TryGetValue<long>("stakeCents", out var stakeCents) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1)
        {
            throw new InvalidOperationException("The Baccarat round is corrupt.");
        }
        var betSide = BaccaratService.ParseBetSide(rawBetSide);
        var stake = BaccaratMoney.ToRand(ValidateStakeCents(stakeCents));
        var consumedCards = ReadStringArray(snapshot, "consumedCards").Select(CardCode.Parse).ToArray();
        PuntoBancoRoundResult round;
        try { round = PuntoBancoRoundDealer.Deal(consumedCards); }
        catch (ArgumentException exception) { throw new InvalidOperationException("The Baccarat dealt cards are corrupt.", exception); }
        if (!round.ConsumedCards.SequenceEqual(consumedCards))
            throw new InvalidOperationException("The Baccarat dealt cards are corrupt.");
        var settlement = PuntoBancoPaytable.Settle(betSide, stake, round);
        if (!snapshot.TryGetValue<string>("outcome", out var outcome) || outcome != BaccaratService.OutcomeName(round.Outcome) ||
            !snapshot.TryGetValue<bool>("endedOnNatural", out var endedOnNatural) || endedOnNatural != round.EndedOnNatural ||
            !snapshot.TryGetValue<string>("disposition", out var disposition) || disposition != BaccaratService.DispositionName(settlement.Disposition) ||
            !snapshot.TryGetValue<long>("profitCents", out var profitCents) || profitCents != ToCents(settlement.Profit) ||
            !snapshot.TryGetValue<long>("totalReturnCents", out var totalReturnCents) || totalReturnCents != ToCents(settlement.TotalReturn) ||
            !snapshot.TryGetValue<string>("startIdempotencyKey", out var idempotencyKey) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidOperationException("The Baccarat round settlement is corrupt.");
        }
        var shoeCardsUsed = snapshot.TryGetValue<long>("shoeCardsUsed", out var rawUsed)
            ? checked((int)rawUsed)
            : round.ConsumedCards.Length;
        var shoeCardsRemaining = snapshot.TryGetValue<long>("shoeCardsRemaining", out var rawRemaining)
            ? checked((int)rawRemaining)
            : 416 - shoeCardsUsed;
        if (shoeCardsUsed is < 0 or > 416 || shoeCardsRemaining is < 0 or > 416 || shoeCardsUsed + shoeCardsRemaining != 416)
            throw new InvalidOperationException("The Baccarat shoe position is corrupt.");
        return new BaccaratStoreRound(roundId, userId, betSide, round, settlement, shoeCardsUsed, shoeCardsRemaining);
    }

    private static BaccaratShoe ReadShoe(DocumentSnapshot snapshot, string expectedUserId)
    {
        if (!snapshot.TryGetValue<string>("userId", out var userId) || userId != expectedUserId ||
            !snapshot.TryGetValue<long>("nextIndex", out var rawNextIndex) || rawNextIndex is < 0 or > 416 ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1)
            throw new InvalidOperationException("The Baccarat shoe is corrupt.");
        var cards = ReadStringArray(snapshot, "cards").Select(CardCode.Parse).ToArray();
        var standard = StandardDeck.Create();
        if (cards.Length != 416 || standard.Any(card => cards.Count(value => value == card) != 8))
            throw new InvalidOperationException("The Baccarat shoe is corrupt.");
        return new BaccaratShoe(cards, checked((int)rawNextIndex));
    }

    private static long ValidateStakeCents(long stakeCents)
    {
        _ = BaccaratMoney.ToStakeCents(BaccaratMoney.ToRand(stakeCents));
        return stakeCents;
    }

    private static long ToCents(decimal value)
    {
        var cents = checked(value * RandMoney.CentsPerRand);
        if (cents != decimal.Truncate(cents))
            throw new InvalidOperationException("A Baccarat settlement resolved to a fraction of a cent.");
        return checked((long)cents);
    }

    private static long ReadBalanceCents(DocumentSnapshot snapshot) => checked(
        ReadLong(snapshot, "available") * RandMoney.CentsPerRand +
        Math.Clamp(ReadLong(snapshot, AvailableFractionalCentsField), 0, RandMoney.CentsPerRand - 1));

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;

    private static IReadOnlyList<string> ReadStringArray(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return [];
        return values.Cast<object?>().Select(value => value as string ?? string.Empty).ToArray();
    }

    private sealed record BaccaratShoe(IReadOnlyList<PlayingCard> Cards, int NextIndex);
}
