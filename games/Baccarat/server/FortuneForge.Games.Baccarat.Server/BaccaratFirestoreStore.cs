using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Baccarat;

/// <summary>Owns saved deals and shoe admission; account money is changed only through the signed wallet.</summary>
public sealed class BaccaratFirestoreStore(FirestoreDb database, IGameWalletClient wallet, IGameWalletAccountReader accounts, Func<bool>? acceptsNewRounds = null) : IBaccaratStore
{
    private const string GameId = "baccarat";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<BaccaratStoreResult> StartAsync(string userId, string idempotencyKey, BaccaratBetSide betSide,
        long stakeCents, long playerPairStakeCents, long bankerPairStakeCents, IReadOnlyList<PlayingCard> shuffledShoe,
        DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        StartWithShoeFactoryAsync(userId, idempotencyKey, betSide, stakeCents, playerPairStakeCents, bankerPairStakeCents,
            () => shuffledShoe, nowUtc, cancellationToken);

    public async Task<BaccaratStoreResult> StartWithShoeFactoryAsync(string userId, string idempotencyKey, BaccaratBetSide betSide,
        long stakeCents, long playerPairStakeCents, long bankerPairStakeCents, Func<IReadOnlyList<PlayingCard>> createShoe,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var stored = await database.RunTransactionAsync(async transaction =>
        {
            var documents = await transaction.GetAllSnapshotsAsync([RoundDocument(roundId), ShoeDocument(userId)], cancellationToken);
            if (documents[0].Exists)
            {
                var existing = ReadRound(documents[0]);
                Match(existing, userId, betSide, stakeCents, playerPairStakeCents, bankerPairStakeCents);
                return existing;
            }
            if (acceptsNewRounds?.Invoke() == false) throw new BaccaratUnavailableException();
            var shoeDocument = documents[1];
            if (shoeDocument.TryGetValue<string>("pendingRoundId", out var pending) && pending.Length > 0)
                throw new BaccaratRoundConflictException("Recover the pending Baccarat deal before starting another wager.");
            var shoe = shoeDocument.Exists ? ReadShoe(shoeDocument, userId) : new BaccaratShoe(createShoe().ToArray(), 0);
            if (shoe.NextIndex >= 312) shoe = new BaccaratShoe(createShoe().ToArray(), 0);
            ValidateShoe(shoe.Cards);
            var request = new BaccaratComputeRequest(shoe.Cards.Skip(shoe.NextIndex).Take(6).ToArray(), betSide,
                BaccaratMoney.ToRand(stakeCents), BaccaratMoney.ToRand(playerPairStakeCents), BaccaratMoney.ToRand(bankerPairStakeCents));
            var result = BaccaratCompute.Resolve(request);
            var round = result.Round.ToRound();
            var nextIndex = checked(shoe.NextIndex + round.ConsumedCards.Length);
            var prepared = new BaccaratStoreRound(roundId, userId, betSide, round, result.MainBet, result.PlayerPair,
                result.BankerPair, nextIndex, shoe.Cards.Count - nextIndex);
            var fields = RoundData(prepared, idempotencyKey, nowUtc);
            fields["walletStatus"] = "pending";
            fields["outcomeSha256"] = Digest(prepared);
            fields["pendingShoeCards"] = shoe.Cards.Select(CardCode.Format).ToArray();
            fields["pendingShoeStartIndex"] = (long)shoe.NextIndex;
            fields["pendingShoeSha256"] = ShoeDigest(shoe);
            transaction.Create(RoundDocument(roundId), fields);
            var shoeFields = shoeDocument.Exists ? new Dictionary<string, object>() : ShoeData(userId, shoe.Cards, shoe.NextIndex, nowUtc);
            shoeFields["pendingRoundId"] = roundId;
            transaction.Set(ShoeDocument(userId), shoeFields, SetOptions.MergeAll);
            var observation = GameSecurityEventDocuments.Capture(GameId, roundId, 0, nowUtc.UtcDateTime,
                prepared, [userId], "prepared");
            FirestoreGameAudit.WriteObservation(transaction, database, observation);
            return prepared;
        }, cancellationToken: cancellationToken);
        return await CompleteAsync(userId, stored, cancellationToken);
    }

    public async Task<BaccaratStoreResult?> FindStartAsync(string userId, string key, BaccaratBetSide side, long main, long player, long banker, CancellationToken token)
    {
        var snapshot = await RoundDocument(CreateLookupKey(userId + "\n" + key)).GetSnapshotAsync(token);
        if (!snapshot.Exists) return null;
        var round = ReadRound(snapshot); Match(round, userId, side, main, player, banker);
        return await CompleteAsync(userId, round, token);
    }

    public async Task<BaccaratStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshot = await RoundDocument(roundId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("userId", out var owner) || owner != userId) return null;
        return await CompleteAsync(userId, ReadRound(snapshot), cancellationToken);
    }

    public async Task<BaccaratStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken)
    {
        var shoe = await ShoeDocument(userId).GetSnapshotAsync(cancellationToken);
        var id = shoe.TryGetValue<string>("pendingRoundId", out var pending) && pending.Length > 0 ? pending :
            shoe.TryGetValue<string>("latestRoundId", out var latest) ? latest : null;
        return id is null ? null : await GetAsync(userId, id, cancellationToken);
    }

    private async Task<BaccaratStoreResult> CompleteAsync(string userId, BaccaratStoreRound stored, CancellationToken token)
    {
        var reference = RoundDocument(stored.RoundId);
        var document = await reference.GetSnapshotAsync(token);
        if (!document.TryGetValue<string>("walletStatus", out var status))
            return new(stored, (await accounts.GetStateAsync(userId, token)).SlotsCreditsBalanceCents
                ?? throw new GameWalletUnavailableException());
        ValidatePrepared(document, stored);
        if (status is "settled" or "declined") return Result(stored, ReadReceipt(document, stored));
        if (status != "pending") throw new InvalidOperationException("The Baccarat wallet status is corrupt.");
        var key = ReadRequiredString(document, "startIdempotencyKey");
        var request = WalletRequest(document, stored);
        var receipt = await wallet.SettleAsync(userId, request, token);
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, GameId, userId, request))
            throw new InvalidOperationException("The Baccarat wallet receipt does not match the prepared deal.");
        var confirmed = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([reference, ShoeDocument(userId)], token);
            var current = ReadRound(snapshots[0]);
            ValidatePrepared(snapshots[0], current);
            var currentStatus = ReadRequiredString(snapshots[0], "walletStatus");
            if (currentStatus is "settled" or "declined") return ReadReceipt(snapshots[0], current);
            if (currentStatus != "pending" || ReadRequiredString(snapshots[1], "pendingRoundId") != stored.RoundId || Digest(current) != Digest(stored))
                throw new InvalidOperationException("The Baccarat pending deal changed before wallet confirmation.");
            var declined = receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch;
            transaction.Update(reference, new Dictionary<string, object> { ["walletStatus"] = declined ? "declined" : "settled",
                ["walletReceiptJson"] = JsonSerializer.Serialize(receipt, Json) });
            var fields = new Dictionary<string, object> { ["pendingRoundId"] = "" };
            if (!declined)
            {
                var frozenShoe = ReadPendingShoe(snapshots[0], stored);
                foreach (var field in ShoeData(userId, frozenShoe.Cards, stored.ShoeCardsUsed, receipt.SettledAtUtc)) fields[field.Key] = field.Value;
                fields["latestRoundId"] = stored.RoundId;
                WriteSettlement(transaction, stored, receipt.SettledAtUtc.UtcDateTime);
                transaction.Create(RoundEventDocument(stored.RoundId), RoundEventData(stored.RoundId, userId, key, receipt.SettledAtUtc));
            }
            transaction.Set(ShoeDocument(userId), fields, SetOptions.MergeAll);
            return receipt;
        }, cancellationToken: token);
        return Result(stored, confirmed);
    }

    private void WriteSettlement(Transaction transaction, BaccaratStoreRound round, DateTime now)
    {
        var wager = BaccaratMoney.ToCents(round.TotalStake); var payout = BaccaratMoney.ToCents(round.CombinedTotalReturn);
        var record = SinglePlayerRoundRecord.Create(GameId, round.RoundId, round.UserId, wager, payout, now, now,
            "baccaratRounds", round.RoundId, round, "baccarat-deal-and-side-bets-v1", BaccaratOutcomeAudit.Verify(round, wager, payout), BaccaratOutcomeAudit.Version);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, record.Audit);
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, GameId, round.UserId, now);
    }
    private static BaccaratStoreResult Result(BaccaratStoreRound round, GameWalletSettlementReceipt receipt)
    {
        if (receipt.InsufficientFunds) throw new BaccaratInsufficientCreditsException(receipt.BalanceBeforeCents, BaccaratMoney.ToCents(round.TotalStake));
        if (receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch) throw new InvalidOperationException("Unexpected Baccarat wallet decline.");
        return new(round, receipt.BalanceAfterCents);
    }
    private static GameWalletSettlementRequest WalletRequest(DocumentSnapshot document, BaccaratStoreRound round) =>
        new(ReadRequiredString(document, "startIdempotencyKey"), Digest(round), BaccaratMoney.ToCents(round.TotalStake), BaccaratMoney.ToCents(round.CombinedTotalReturn));
    private static GameWalletSettlementReceipt ReadReceipt(DocumentSnapshot document, BaccaratStoreRound round)
    {
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(ReadRequiredString(document, "walletReceiptJson"), Json)
            ?? throw new InvalidOperationException("The Baccarat wallet receipt is corrupt.");
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, GameId, round.UserId, WalletRequest(document, round)))
            throw new InvalidOperationException("The Baccarat wallet receipt does not match its saved deal.");
        var declined = receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch;
        if ((ReadRequiredString(document, "walletStatus") == "declined") != declined)
            throw new InvalidOperationException("The Baccarat wallet status differs from its receipt.");
        return receipt;
    }
    private static void ValidatePrepared(DocumentSnapshot document, BaccaratStoreRound round)
    {
        if (ReadRequiredString(document, "outcomeSha256") != Digest(round)) throw new InvalidOperationException("The Baccarat prepared outcome is corrupt.");
        _ = ReadPendingShoe(document, round);
    }
    private static BaccaratShoe ReadPendingShoe(DocumentSnapshot document, BaccaratStoreRound round)
    {
        try { return DecodePendingShoe(document, round); }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or OverflowException or NullReferenceException)
        { throw new InvalidOperationException("The saved Baccarat state could not be verified.", error); }
    }
    private static BaccaratShoe DecodePendingShoe(DocumentSnapshot document, BaccaratStoreRound round)
    {
        var cards = ReadStringArray(document, "pendingShoeCards").Select(CardCode.Parse).ToArray(); ValidateShoe(cards);
        var start = checked((int)ReadRequiredLong(document, "pendingShoeStartIndex"));
        var shoe = new BaccaratShoe(cards, start);
        if (start < 0 || start + round.Round.ConsumedCards.Length != round.ShoeCardsUsed ||
            !cards.Skip(start).Take(round.Round.ConsumedCards.Length).SequenceEqual(round.Round.ConsumedCards) ||
            ReadRequiredString(document, "pendingShoeSha256") != ShoeDigest(shoe))
            throw new InvalidOperationException("The Baccarat pending shoe is corrupt.");
        return shoe;
    }
    private static string ShoeDigest(BaccaratShoe shoe) => CreateLookupKey(JsonSerializer.Serialize(new
        { cards = shoe.Cards.Select(CardCode.Format).ToArray(), shoe.NextIndex }, Json));
    private static void Match(BaccaratStoreRound round, string userId, BaccaratBetSide side, long main, long player, long banker)
    {
        if (round.UserId != userId || round.BetSide != side || BaccaratMoney.ToCents(round.Settlement.Stake) != main ||
            BaccaratMoney.ToCents(round.PlayerPairSettlement.Stake) != player || BaccaratMoney.ToCents(round.BankerPairSettlement.Stake) != banker)
            throw new BaccaratRoundConflictException("This Idempotency-Key was already used with a different Baccarat request.");
    }
    private static void ValidateShoe(IReadOnlyList<PlayingCard> cards)
    {
        if (cards.Count != 416 || StandardDeck.Create().Any(card => cards.Count(value => value == card) != 8))
            throw new InvalidOperationException("The Baccarat shoe is corrupt.");
    }
    private static string Digest(BaccaratStoreRound round) => CreateLookupKey(JsonSerializer.Serialize(round, Json));
    internal static string CreateLookupKey(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private DocumentReference RoundDocument(string id) => database.Collection("baccaratRounds").Document(id);
    private DocumentReference RoundEventDocument(string id) => database.Collection("baccaratRoundEvents").Document($"{id}-settled");
    private DocumentReference ShoeDocument(string userId) => database.Collection("baccaratShoes").Document(CreateLookupKey(userId));

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
        ["playerPairStakeCents"] = ToCents(stored.PlayerPairSettlement.Stake),
        ["playerPairDisposition"] = BaccaratService.DispositionName(stored.PlayerPairSettlement.Disposition),
        ["playerPairProfitCents"] = ToCents(stored.PlayerPairSettlement.Profit),
        ["playerPairTotalReturnCents"] = ToCents(stored.PlayerPairSettlement.TotalReturn),
        ["bankerPairStakeCents"] = ToCents(stored.BankerPairSettlement.Stake),
        ["bankerPairDisposition"] = BaccaratService.DispositionName(stored.BankerPairSettlement.Disposition),
        ["bankerPairProfitCents"] = ToCents(stored.BankerPairSettlement.Profit),
        ["bankerPairTotalReturnCents"] = ToCents(stored.BankerPairSettlement.TotalReturn),
        ["totalStakeCents"] = ToCents(stored.TotalStake),
        ["combinedProfitCents"] = ToCents(stored.CombinedProfit),
        ["combinedTotalReturnCents"] = ToCents(stored.CombinedTotalReturn),
        ["shoeCardsUsed"] = (long)stored.ShoeCardsUsed,
        ["shoeCardsRemaining"] = (long)stored.ShoeCardsRemaining,
        ["startIdempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 2L,
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

    private static BaccaratStoreRound ReadRound(DocumentSnapshot snapshot)
    {
        try { return DecodeRound(snapshot); }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or OverflowException or NullReferenceException)
        { throw new InvalidOperationException("The saved Baccarat state could not be verified.", error); }
    }
    private static BaccaratStoreRound DecodeRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("roundId", out var roundId) || roundId.Length != 64 ||
            !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) ||
            !snapshot.TryGetValue<string>("betSide", out var rawBetSide) ||
            !snapshot.TryGetValue<long>("stakeCents", out var stakeCents) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion is not (1 or 2))
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
        var playerPairStakeCents = schemaVersion == 2 ? ReadRequiredLong(snapshot, "playerPairStakeCents") : 0;
        var bankerPairStakeCents = schemaVersion == 2 ? ReadRequiredLong(snapshot, "bankerPairStakeCents") : 0;
        var playerPairSettlement = PuntoBancoPaytable.SettlePair(
            BaccaratMoney.ToRand(ValidateOptionalStakeCents(playerPairStakeCents)), round.PlayerHand);
        var bankerPairSettlement = PuntoBancoPaytable.SettlePair(
            BaccaratMoney.ToRand(ValidateOptionalStakeCents(bankerPairStakeCents)), round.BankerHand);
        if (!snapshot.TryGetValue<string>("outcome", out var outcome) || outcome != BaccaratService.OutcomeName(round.Outcome) ||
            !snapshot.TryGetValue<bool>("endedOnNatural", out var endedOnNatural) || endedOnNatural != round.EndedOnNatural ||
            !snapshot.TryGetValue<string>("disposition", out var disposition) || disposition != BaccaratService.DispositionName(settlement.Disposition) ||
            !snapshot.TryGetValue<long>("profitCents", out var profitCents) || profitCents != ToCents(settlement.Profit) ||
            !snapshot.TryGetValue<long>("totalReturnCents", out var totalReturnCents) || totalReturnCents != ToCents(settlement.TotalReturn) ||
            !snapshot.TryGetValue<string>("startIdempotencyKey", out var idempotencyKey) || string.IsNullOrWhiteSpace(idempotencyKey) ||
            roundId != snapshot.Id || roundId != CreateLookupKey(userId + "\n" + idempotencyKey))
        {
            throw new InvalidOperationException("The Baccarat round settlement is corrupt.");
        }
        if (schemaVersion == 2 &&
            (ReadRequiredString(snapshot, "playerPairDisposition") != BaccaratService.DispositionName(playerPairSettlement.Disposition) ||
             ReadRequiredLong(snapshot, "playerPairProfitCents") != ToCents(playerPairSettlement.Profit) ||
             ReadRequiredLong(snapshot, "playerPairTotalReturnCents") != ToCents(playerPairSettlement.TotalReturn) ||
             ReadRequiredString(snapshot, "bankerPairDisposition") != BaccaratService.DispositionName(bankerPairSettlement.Disposition) ||
             ReadRequiredLong(snapshot, "bankerPairProfitCents") != ToCents(bankerPairSettlement.Profit) ||
             ReadRequiredLong(snapshot, "bankerPairTotalReturnCents") != ToCents(bankerPairSettlement.TotalReturn) ||
             ReadRequiredLong(snapshot, "totalStakeCents") != stakeCents + playerPairStakeCents + bankerPairStakeCents ||
             ReadRequiredLong(snapshot, "combinedProfitCents") != ToCents(settlement.Profit + playerPairSettlement.Profit + bankerPairSettlement.Profit) ||
             ReadRequiredLong(snapshot, "combinedTotalReturnCents") != ToCents(settlement.TotalReturn + playerPairSettlement.TotalReturn + bankerPairSettlement.TotalReturn)))
        {
            throw new InvalidOperationException("The Baccarat pair settlement is corrupt.");
        }
        var shoeCardsUsed = snapshot.TryGetValue<long>("shoeCardsUsed", out var rawUsed)
            ? checked((int)rawUsed)
            : round.ConsumedCards.Length;
        var shoeCardsRemaining = snapshot.TryGetValue<long>("shoeCardsRemaining", out var rawRemaining)
            ? checked((int)rawRemaining)
            : 416 - shoeCardsUsed;
        if (shoeCardsUsed is < 0 or > 416 || shoeCardsRemaining is < 0 or > 416 || shoeCardsUsed + shoeCardsRemaining != 416)
            throw new InvalidOperationException("The Baccarat shoe position is corrupt.");
        return new BaccaratStoreRound(roundId, userId, betSide, round, settlement,
            playerPairSettlement, bankerPairSettlement, shoeCardsUsed, shoeCardsRemaining);
    }

    private static BaccaratShoe ReadShoe(DocumentSnapshot snapshot, string expectedUserId)
    {
        try { return DecodeShoe(snapshot, expectedUserId); }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or OverflowException or NullReferenceException)
        { throw new InvalidOperationException("The saved Baccarat state could not be verified.", error); }
    }
    private static BaccaratShoe DecodeShoe(DocumentSnapshot snapshot, string expectedUserId)
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

    private static long ValidateStakeCents(long stakeCents) => stakeCents > 0 ? stakeCents
        : throw new InvalidOperationException("The historical Baccarat main stake is corrupt.");
    private static long ValidateOptionalStakeCents(long stakeCents) => stakeCents >= 0 ? stakeCents
        : throw new InvalidOperationException("The historical Baccarat side stake is corrupt.");

    private static long ReadRequiredLong(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<long>(field, out var value)
            ? value
            : throw new InvalidOperationException($"The Baccarat round is missing {field}.");

    private static string ReadRequiredString(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<string>(field, out var value)
            ? value
            : throw new InvalidOperationException($"The Baccarat round is missing {field}.");

    private static long ToCents(decimal value)
    {
        var cents = checked(value * 100m);
        if (cents != decimal.Truncate(cents))
            throw new InvalidOperationException("A Baccarat settlement resolved to a fraction of a cent.");
        return checked((long)cents);
    }

    private static IReadOnlyList<string> ReadStringArray(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return [];
        return values.Cast<object?>().Select(value => value as string ?? string.Empty).ToArray();
    }

    private sealed record BaccaratShoe(IReadOnlyList<PlayingCard> Cards, int NextIndex);
}
