using System.Collections;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.CasinoWar;

internal sealed class CasinoWarFirestoreStore(FirestoreDb database) : ICasinoWarStore
{
    private const string CurrencyId = "slotsCredits";
    private const string FractionField = "availableFractionalCents";
    public Task<CasinoWarStoreResult> StartAsync(string userId, string idempotencyKey, long primaryStakeCents, long tieStakeCents, IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (shuffledShoe.Count < 10) throw new ArgumentException("A Casino War shoe requires at least ten cards.", nameof(shuffledShoe));
        var roundId = Hash($"{userId}\n{idempotencyKey}");
        var roundRef = RoundDocument(roundId); var balanceRef = BalanceDocument(userId);
        var wagerRef = LedgerDocument($"casino-war-{roundId}-opening-wager"); var tiePayoutRef = LedgerDocument($"casino-war-{roundId}-tie-payout"); var primaryPayoutRef = LedgerDocument($"casino-war-{roundId}-opening-primary-payout"); var eventRef = EventDocument(roundId, "opening");
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(transaction.GetSnapshotAsync(roundRef, cancellationToken), transaction.GetSnapshotAsync(balanceRef, cancellationToken), transaction.GetSnapshotAsync(wagerRef, cancellationToken), transaction.GetSnapshotAsync(tiePayoutRef, cancellationToken), transaction.GetSnapshotAsync(primaryPayoutRef, cancellationToken), transaction.GetSnapshotAsync(eventRef, cancellationToken));
            var balance = ReadBalance(reads[1]);
            if (reads[0].Exists)
            {
                var existing = ReadRound(reads[0]);
                if (existing.UserId != userId || ToCents(existing.Round.PrimaryStake) != primaryStakeCents || ToCents(existing.TieSettlement?.Stake ?? 0m) != tieStakeCents) throw new CasinoWarRoundConflictException("This Idempotency-Key was already used with different Casino War stakes.");
                var tieReturn = ToCents(existing.TieSettlement?.TotalReturn ?? 0m); var primaryReturn = ToCents(existing.Round.Settlement?.TotalReturn ?? 0m);
                if (!reads[2].Exists || !reads[5].Exists || (tieReturn > 0) != reads[3].Exists || (primaryReturn > 0) != reads[4].Exists) throw new InvalidOperationException("A recorded Casino War opening is missing its ledger.");
                return new CasinoWarStoreResult(existing, balance);
            }
            if (reads[2].Exists || reads[3].Exists || reads[4].Exists || reads[5].Exists) throw new InvalidOperationException("A Casino War opening ledger exists without its round.");
            var openingCharge = checked(primaryStakeCents + tieStakeCents);
            if (balance < openingCharge) throw new CasinoWarInsufficientCreditsException(balance, openingCharge);
            var round = CasinoWarRoundEngine.Start(shuffledShoe.Take(10).ToArray(), CasinoWarMoney.ToRand(primaryStakeCents));
            var tie = tieStakeCents == 0 ? null : CasinoWarTieBetPaytable.Settle(round.Opening, CasinoWarMoney.ToRand(tieStakeCents));
            var tieReturnCents = ToCents(tie?.TotalReturn ?? 0m); var primaryReturnCents = ToCents(round.Settlement?.TotalReturn ?? 0m);
            var afterCharge = checked(balance - openingCharge); var finalBalance = checked(afterCharge + tieReturnCents + primaryReturnCents);
            var stored = new CasinoWarStoreRound(roundId, userId, round, tie, shuffledShoe.Take(10).ToArray());
            transaction.Create(roundRef, RoundData(stored, idempotencyKey, nowUtc)); transaction.Set(balanceRef, BalanceUpdate(finalBalance, nowUtc), SetOptions.MergeAll);
            transaction.Create(wagerRef, Ledger(wagerRef.Id, userId, -openingCharge, afterCharge, "casino-war-opening-wager", idempotencyKey, nowUtc));
            if (tieReturnCents > 0) transaction.Create(tiePayoutRef, Ledger(tiePayoutRef.Id, userId, tieReturnCents, checked(afterCharge + tieReturnCents), "casino-war-tie-payout", $"{idempotencyKey}-tie-payout", nowUtc));
            if (primaryReturnCents > 0) transaction.Create(primaryPayoutRef, Ledger(primaryPayoutRef.Id, userId, primaryReturnCents, finalBalance, "casino-war-primary-payout", $"{idempotencyKey}-primary-payout", nowUtc));
            transaction.Create(eventRef, Event(roundId, userId, "opening", idempotencyKey, nowUtc));
            return new CasinoWarStoreResult(stored, finalBalance);
        }, cancellationToken: cancellationToken);
    }

    public async Task<CasinoWarStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(RoundDocument(roundId).GetSnapshotAsync(cancellationToken), BalanceDocument(userId).GetSnapshotAsync(cancellationToken));
        if (!snapshots[0].Exists) return null; var stored = ReadRound(snapshots[0]);
        return stored.UserId == userId ? new CasinoWarStoreResult(stored, ReadBalance(snapshots[1])) : null;
    }

    public Task<CasinoWarStoreResult> DecideAsync(string userId, string roundId, string idempotencyKey, CasinoWarTieDecision decision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundRef = RoundDocument(roundId); var balanceRef = BalanceDocument(userId); var extraWagerRef = LedgerDocument($"casino-war-{roundId}-war-wager"); var payoutRef = LedgerDocument($"casino-war-{roundId}-decision-primary-payout"); var eventRef = EventDocument(roundId, "decision");
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(transaction.GetSnapshotAsync(roundRef, cancellationToken), transaction.GetSnapshotAsync(balanceRef, cancellationToken), transaction.GetSnapshotAsync(extraWagerRef, cancellationToken), transaction.GetSnapshotAsync(payoutRef, cancellationToken), transaction.GetSnapshotAsync(eventRef, cancellationToken));
            if (!reads[0].Exists) throw new CasinoWarRoundNotFoundException(); var stored = ReadRound(reads[0]); if (stored.UserId != userId) throw new CasinoWarRoundNotFoundException();
            var balance = ReadBalance(reads[1]);
            if (stored.Round.Phase == CasinoWarRoundPhase.Completed)
            {
                if (stored.Round.TieDecision != decision) throw new CasinoWarRoundConflictException("This Casino War round was already completed with a different decision.");
                var returnCents = ToCents(stored.Round.Settlement!.TotalReturn); var charged = decision == CasinoWarTieDecision.GoToWar;
                if (!reads[4].Exists || (charged != reads[2].Exists) || (returnCents > 0) != reads[3].Exists) throw new InvalidOperationException("A completed Casino War decision is missing its ledger.");
                return new CasinoWarStoreResult(stored, balance);
            }
            if (reads[2].Exists || reads[3].Exists || reads[4].Exists) throw new InvalidOperationException("A Casino War decision ledger exists without a completed round.");
            var extraCharge = decision == CasinoWarTieDecision.GoToWar ? ToCents(stored.Round.PrimaryStake) : 0;
            if (balance < extraCharge) throw new CasinoWarInsufficientCreditsException(balance, extraCharge);
            var completed = CasinoWarRoundEngine.Decide(stored.Round, decision); var payoutCents = ToCents(completed.Settlement!.TotalReturn); var afterCharge = checked(balance - extraCharge); var finalBalance = checked(afterCharge + payoutCents);
            var updated = stored with { Round = completed };
            transaction.Update(roundRef, DecisionData(completed, idempotencyKey, nowUtc));
            if (extraCharge > 0) transaction.Create(extraWagerRef, Ledger(extraWagerRef.Id, userId, -extraCharge, afterCharge, "casino-war-war-wager", idempotencyKey, nowUtc));
            if (payoutCents > 0) transaction.Create(payoutRef, Ledger(payoutRef.Id, userId, payoutCents, finalBalance, "casino-war-primary-payout", $"{idempotencyKey}-primary-payout", nowUtc));
            if (extraCharge > 0 || payoutCents > 0) transaction.Set(balanceRef, BalanceUpdate(finalBalance, nowUtc), SetOptions.MergeAll);
            transaction.Create(eventRef, Event(roundId, userId, "decision", idempotencyKey, nowUtc)); return new CasinoWarStoreResult(updated, finalBalance);
        }, cancellationToken: cancellationToken);
    }

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private DocumentReference RoundDocument(string id) => database.Collection("casinoWarRounds").Document(id); private DocumentReference EventDocument(string id, string eventName) => database.Collection("casinoWarRoundEvents").Document($"{id}-{eventName}"); private DocumentReference BalanceDocument(string userId) => database.Collection("userBalances").Document($"{userId}_{CurrencyId}"); private DocumentReference LedgerDocument(string id) => database.Collection("balanceTransactions").Document(id);
    private static Dictionary<string, object> RoundData(CasinoWarStoreRound stored, string key, DateTimeOffset now) => new() { ["roundId"] = stored.RoundId, ["userId"] = stored.UserId, ["primaryStakeCents"] = ToCents(stored.Round.PrimaryStake), ["tieStakeCents"] = ToCents(stored.TieSettlement?.Stake ?? 0m), ["deck"] = stored.Deck.Select(CardCode.Format).ToArray(), ["phase"] = PhaseName(stored.Round.Phase), ["startIdempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L };
    private static Dictionary<string, object> DecisionData(CasinoWarRound round, string key, DateTimeOffset now) => new() { ["phase"] = "completed", ["decision"] = CasinoWarService.DecisionName(round.TieDecision!.Value), ["decisionIdempotencyKey"] = key, ["primaryReturnCents"] = ToCents(round.Settlement!.TotalReturn), ["primaryOutcome"] = CasinoWarService.RoundOutcomeName(round.Settlement.Outcome), ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime) };
    private static Dictionary<string, object> Event(string id, string user, string name, string key, DateTimeOffset now) => new() { ["roundId"] = id, ["userId"] = user, ["event"] = name, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L };
    private static Dictionary<string, object> BalanceUpdate(long cents, DateTimeOffset now) => new() { ["available"] = cents / RandMoney.CentsPerRand, [FractionField] = cents % RandMoney.CentsPerRand, ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime) };
    private static Dictionary<string, object> Ledger(string id, string user, long amount, long after, string type, string key, DateTimeOffset now) => new() { ["transactionId"] = id, ["userId"] = user, ["currencyId"] = CurrencyId, ["amount"] = (double)CasinoWarMoney.ToRand(amount), ["balanceAfter"] = (double)CasinoWarMoney.ToRand(after), ["type"] = type, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime) };
    private static CasinoWarStoreRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("roundId", out var id) || id.Length != 64 || !snapshot.TryGetValue<string>("userId", out var user) || string.IsNullOrWhiteSpace(user) || !snapshot.TryGetValue<long>("primaryStakeCents", out var primaryCents) || !snapshot.TryGetValue<long>("tieStakeCents", out var tieCents) || !snapshot.TryGetValue<string>("phase", out var phase) || phase is not ("awaiting-tie-decision" or "completed") || !snapshot.TryGetValue<long>("schemaVersion", out var version) || version != 1) throw new InvalidOperationException("The Casino War round is corrupt.");
        var primary = CasinoWarMoney.PrimaryCents(CasinoWarMoney.ToRand(primaryCents)); var tieStake = CasinoWarMoney.TieCents(CasinoWarMoney.ToRand(tieCents)); var deck = ReadStringArray(snapshot, "deck").Select(CardCode.Parse).ToArray(); if (deck.Length != 10) throw new InvalidOperationException("The Casino War deck is corrupt.");
        var round = CasinoWarRoundEngine.Start(deck, CasinoWarMoney.ToRand(primary)); var tie = tieStake == 0 ? null : CasinoWarTieBetPaytable.Settle(round.Opening, CasinoWarMoney.ToRand(tieStake));
        if (phase == "awaiting-tie-decision") { if (round.Phase != CasinoWarRoundPhase.AwaitingTieDecision) throw new InvalidOperationException("The Casino War phase is corrupt."); return new CasinoWarStoreRound(id, user, round, tie, deck); }
        if (!snapshot.TryGetValue<string>("decision", out var rawDecision) || !snapshot.TryGetValue<string>("decisionIdempotencyKey", out var decisionKey) || string.IsNullOrWhiteSpace(decisionKey)) throw new InvalidOperationException("The Casino War decision is corrupt.");
        round = CasinoWarRoundEngine.Decide(round, CasinoWarService.ParseDecision(rawDecision)); if (!snapshot.TryGetValue<long>("primaryReturnCents", out var returnCents) || returnCents != ToCents(round.Settlement!.TotalReturn) || !snapshot.TryGetValue<string>("primaryOutcome", out var outcome) || outcome != CasinoWarService.RoundOutcomeName(round.Settlement.Outcome)) throw new InvalidOperationException("The Casino War settlement is corrupt.");
        return new CasinoWarStoreRound(id, user, round, tie, deck);
    }
    private static string PhaseName(CasinoWarRoundPhase value) => value == CasinoWarRoundPhase.AwaitingTieDecision ? "awaiting-tie-decision" : value == CasinoWarRoundPhase.Completed ? "completed" : throw new ArgumentOutOfRangeException(nameof(value));
    private static long ToCents(decimal value) { var cents = checked(value * RandMoney.CentsPerRand); if (cents != decimal.Truncate(cents)) throw new InvalidOperationException("A Casino War settlement resolved to a fraction of a cent."); return checked((long)cents); }
    private static long ReadBalance(DocumentSnapshot snapshot) => checked(ReadLong(snapshot, "available") * RandMoney.CentsPerRand + Math.Clamp(ReadLong(snapshot, FractionField), 0, 99)); private static long ReadLong(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static IReadOnlyList<string> ReadStringArray(DocumentSnapshot snapshot, string field) { if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return []; return values.Cast<object?>().Select(value => value as string ?? string.Empty).ToArray(); }
}
