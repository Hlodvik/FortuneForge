using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;
using static FortuneForge.Server.Accounts.Storage.FirestoreWalletDocuments;

namespace FortuneForge.Server.Accounts.Storage;

internal sealed class WalletRoundLedgerLayout
{
    private WalletRoundLedgerLayout(Guid? slotSpinId, string? entryId, string? entryAttemptId)
    {
        SlotSpinId = slotSpinId;
        EntryId = entryId;
        EntryAttemptId = entryAttemptId;
    }


    internal Guid? SlotSpinId { get; }
    internal string? EntryId { get; }
    internal string? EntryAttemptId { get; }

    internal static WalletRoundLedgerLayout LegacySlot(Guid spinId) => new(spinId, null, null);
    internal static WalletRoundLedgerLayout ArcadeEntry(string entryId, string attemptId) => new(null, entryId, attemptId);
}

internal sealed class FirestoreWalletRoundSettlementStore(FirestoreDb database) : IWalletRoundSettlementStore
{
    internal static IReadOnlyList<DocumentReference> PostedStandardLegEvidence(FirestoreDb database, DocumentSnapshot ledger)
    {
        if (PostedLegIdentity(ledger) is not { } identity) return [];
        var id = GameWalletSettlementIdentity.CreateId(identity.GameId, identity.UserId, identity.OperationId);
        var completion = GameWalletSettlementIdentity.CreateCompletionId(identity.GameId, identity.UserId, identity.OperationId);
        return [database.Collection("walletRoundSettlements").Document(id),
            database.Collection("walletRoundSettlements").Document(completion),
            Transaction(database, id + "-wager"), Transaction(database, id + "-payout")];
    }

    // Read-only history uses the same receipt arithmetic and ledger contracts as exact wallet retries.
    internal static bool IsPostedStandardLeg(DocumentSnapshot ledger, IReadOnlyDictionary<string, DocumentSnapshot> evidence)
    {
        try
        {
            if (PostedLegIdentity(ledger) is not { } identity ||
                !ledger.TryGetValue<string>("walletSettlementId", out var settlementId)) return false;
            var id = GameWalletSettlementIdentity.CreateId(identity.GameId, identity.UserId, identity.OperationId);
            var completionId = GameWalletSettlementIdentity.CreateCompletionId(identity.GameId, identity.UserId, identity.OperationId);
            if (settlementId != id && settlementId != completionId) return false;
            DocumentSnapshot Read(string collection, string key) => evidence[collection + "/" + key];
            var stored = Read("walletRoundSettlements", settlementId);
            var phase = stored.ContainsField("phase")
                ? Enum.Parse<GameWalletSettlementPhase>(stored.GetValue<string>("phase"), ignoreCase: false)
                : GameWalletSettlementPhase.Direct;
            var settlement = ReadLinkedSettlement(stored, identity, phase);
            var settledAt = new DateTimeOffset(stored.GetValue<Timestamp>("settledAtUtc").ToDateTime());
            Validate(settlement, settledAt, null);
            if (settlement.SpendLegacyFreeGame || settlement.ExpectedLegacyFreeGamesRemaining is not null ||
                stored.ContainsField("legacyFreeGamesBefore")) return false;
            var wager = Read("balanceTransactions", id + "-wager");
            var payout = Read("balanceTransactions", id + "-payout");
            var receipt = ReadReceipt(stored, settlement, false, wager.Id, payout.Id, null);
            if (IsDeclined(receipt) || receipt.SettlementId != (phase == GameWalletSettlementPhase.CompleteReservation ? completionId : id))
                return false;
            var isWager = ledger.GetValue<string>("type") == "game-wager";
            VerifyLeg(ledger, isWager ? receipt.WagerTransactionId : receipt.PayoutTransactionId,
                settlement, receipt, false, isWager);

            if (phase == GameWalletSettlementPhase.Direct)
            {
                VerifyLeg(wager, receipt.WagerTransactionId, settlement, receipt, false, true);
                VerifyLeg(payout, receipt.PayoutTransactionId, settlement, receipt, false, false);
                return true;
            }
            var reservedDocument = Read("walletRoundSettlements", id);
            var reserve = ReadLinkedSettlement(reservedDocument, identity, GameWalletSettlementPhase.Reserve);
            Validate(reserve, new DateTimeOffset(reservedDocument.GetValue<Timestamp>("settledAtUtc").ToDateTime()), null);
            if (reserve.SpendLegacyFreeGame || reserve.ExpectedLegacyFreeGamesRemaining is not null ||
                reservedDocument.ContainsField("legacyFreeGamesBefore")) return false;
            var reserved = ReadReceipt(reservedDocument, reserve, false, wager.Id, payout.Id, null);
            if (IsDeclined(reserved)) return false;
            VerifyLeg(wager, reserved.WagerTransactionId, reserve, reserved, false, true);
            var completedDocument = Read("walletRoundSettlements", completionId);
            if (!completedDocument.Exists)
            {
                VerifyLeg(payout, null, reserve, reserved, false, false);
                return phase == GameWalletSettlementPhase.Reserve;
            }
            var complete = ReadLinkedSettlement(completedDocument, identity, GameWalletSettlementPhase.CompleteReservation);
            Validate(complete, new DateTimeOffset(completedDocument.GetValue<Timestamp>("settledAtUtc").ToDateTime()), null);
            if (!string.Equals(complete.ReservationSha256, reserve.OutcomeSha256, StringComparison.OrdinalIgnoreCase)) return false;
            var completed = ReadReceipt(completedDocument, complete, false, wager.Id, payout.Id, null);
            if (IsDeclined(completed)) return false;
            VerifyLeg(payout, completed.PayoutTransactionId, complete, completed, false, false);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return false; }
    }

    private static WalletRoundSettlement? PostedLegIdentity(DocumentSnapshot ledger)
    {
        try
        {
            if (!ledger.Exists || ledger.Reference.Parent.Id != "balanceTransactions" ||
                !ledger.TryGetValue<string>("currencyId", out var currency) || currency != SlotsCreditsCurrencyId ||
                !ledger.TryGetValue<long>("schemaVersion", out var schema) || schema != 1 ||
                !ledger.TryGetValue<string>("type", out var type) || type is not ("game-wager" or "game-payout") ||
                !ledger.TryGetValue<string>("gameId", out var game) || !ledger.TryGetValue<string>("userId", out var actor) ||
                !ledger.TryGetValue<string>("operationId", out var operation)) return null;
            ValidateIdentityPart(game, nameof(game));
            ValidateIdentityPart(actor, nameof(actor));
            ValidateIdentityPart(operation, nameof(operation));
            if (actor.Contains('/')) return null;
            return new(game, actor, operation, "", 0, 0);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        { return null; }
    }

    public Task<WalletRoundSettlementReceipt> SettleAsync(
        WalletRoundSettlement settlement, DateTimeOffset settledAtUtc, CancellationToken cancellationToken,
        WalletRoundLedgerLayout? ledgerLayout = null) =>
        database.RunTransactionAsync(transaction => StageAsync(database, transaction, settlement, settledAtUtc, cancellationToken, ledgerLayout),
            cancellationToken: cancellationToken);

    // Call after the game has finished reading. Firestore requires every transactional read to precede the first write.
    internal static async Task<WalletRoundSettlementReceipt> StageAsync(
        FirestoreDb database, Transaction transaction, WalletRoundSettlement settlement,
        DateTimeOffset settledAtUtc, CancellationToken cancellationToken, WalletRoundLedgerLayout? ledgerLayout = null,
        bool requireExisting = false)
    {
        Validate(settlement, settledAtUtc, ledgerLayout);
        var baseId = GameWalletSettlementIdentity.CreateId(settlement.GameId, settlement.UserId, settlement.OperationId);
        var completing = settlement.Phase == GameWalletSettlementPhase.CompleteReservation;
        var reserving = settlement.Phase == GameWalletSettlementPhase.Reserve;
        var completionId = completing || reserving
            ? GameWalletSettlementIdentity.CreateCompletionId(settlement.GameId, settlement.UserId, settlement.OperationId) : null;
        var settlementId = completing ? completionId! : baseId;
        var isLegacySlot = ledgerLayout?.SlotSpinId is not null;
        var ledgerPrefix = ledgerLayout?.SlotSpinId?.ToString("N") ?? baseId;
        var balance = Balance(database, settlement.UserId, SlotsCreditsCurrencyId);
        var receipts = database.Collection("walletRoundSettlements");
        var receiptDocument = receipts.Document(settlementId);
        var linkedDocument = completing ? receipts.Document(baseId) : reserving ? receipts.Document(completionId!) : null;
        var wager = Transaction(database, ledgerLayout?.EntryId is { } entryId
            ? $"arcade-competition-{entryId}-entry" : $"{ledgerPrefix}-wager");
        var payout = Transaction(database, $"{ledgerPrefix}-payout");
        var readLegacyBalance = settlement.SpendLegacyFreeGame || settlement.IncludeLegacyFreeGameBalance ||
            settlement.ExpectedLegacyFreeGamesRemaining is not null;
        var legacy = readLegacyBalance || completing
            ? LegacyFreeGameWallet.Create(database, settlement, ledgerPrefix) : null;
        List<DocumentReference> reads = [balance, receiptDocument, wager, payout];
        if (linkedDocument is not null) reads.Add(linkedDocument);
        if (legacy is not null) reads.Add(legacy.Ledger);
        if (readLegacyBalance && legacy is not null) reads.Add(legacy.Balance);
        var snapshots = (await transaction.GetAllSnapshotsAsync(reads, cancellationToken))
            .ToDictionary(snapshot => snapshot.Reference);
        var linked = linkedDocument is null ? null : snapshots[linkedDocument];
        if (completing)
        {
            var heldEntitlement = legacy ?? throw CorruptSettlement();
            var hold = ReadLinkedSettlement(linked!, settlement, GameWalletSettlementPhase.Reserve);
            if (!string.Equals(hold.OutcomeSha256, settlement.ReservationSha256, StringComparison.OrdinalIgnoreCase))
                throw new WalletRoundSettlementConflictException();
            var reserved = ReadReceipt(linked!, hold, isLegacySlot, wager.Id, payout.Id, heldEntitlement.Ledger.Id);
            if (IsDeclined(reserved))
                throw new WalletRoundSettlementConflictException();
            VerifyLeg(snapshots[wager], reserved.WagerTransactionId, hold, reserved, isLegacySlot, isWager: true);
            heldEntitlement.VerifyLedger(snapshots[heldEntitlement.Ledger], settlement.UserId, reserved);
        }

        var receiptSnapshot = snapshots[receiptDocument];
        if (receiptSnapshot.Exists)
        {
            var replay = ReadReceipt(receiptSnapshot, settlement, isLegacySlot, wager.Id, payout.Id, legacy?.Ledger.Id, ledgerLayout);
            if (!completing)
            {
                VerifyLeg(snapshots[wager], replay.WagerTransactionId, settlement, replay, isLegacySlot, isWager: true, layout: ledgerLayout);
                if (legacy is not null)
                    legacy.VerifyLedger(snapshots[legacy.Ledger], settlement.UserId, replay);
            }
            if (reserving && linked is { Exists: true })
            {
                var completion = ReadLinkedSettlement(linked, settlement, GameWalletSettlementPhase.CompleteReservation);
                var completed = ReadReceipt(linked, completion, isLegacySlot, wager.Id, payout.Id, null);
                if (IsDeclined(replay) || IsDeclined(completed) ||
                    !string.Equals(completion.ReservationSha256, settlement.OutcomeSha256, StringComparison.OrdinalIgnoreCase))
                    throw CorruptSettlement();
                VerifyLeg(snapshots[payout], completed.PayoutTransactionId, completion, completed, isLegacySlot, isWager: false);
            }
            else
                VerifyLeg(snapshots[payout], replay.PayoutTransactionId, settlement, replay, isLegacySlot, isWager: false);
            return replay;
        }

        if (requireExisting || snapshots[payout].Exists || reserving && linked is { Exists: true } ||
            !completing && (snapshots[wager].Exists || legacy is not null && snapshots[legacy.Ledger].Exists))
            throw CorruptSettlement();

        var availableCents = isLegacySlot
            ? ReadLegacyRandBalanceCents(snapshots[balance])
            : ReadSlotsCreditCents(snapshots[balance], settlement.UserId, allowMissingFraction: true);
        if (availableCents < 0)
            throw new InvalidOperationException("The account's slots credit balance is invalid.");

        // Firestore persists timestamps at microsecond precision. Return the same timestamp on the first call and replay.
        settledAtUtc = settledAtUtc.AddTicks(-(settledAtUtc.Ticks % TimeSpan.TicksPerMicrosecond));
        long? freeGamesBefore = readLegacyBalance && legacy is not null
            ? legacy.ReadBalance(snapshots[legacy.Balance], settlement.UserId) : null;
        var receipt = CalculateReceipt(settlement, settlementId, wager.Id, payout.Id, availableCents, settledAtUtc,
            wasAlreadySettled: false, legacy?.Ledger.Id, freeGamesBefore);
        if (!IsDeclined(receipt) &&
            (isLegacySlot && !completing || receipt.WagerTransactionId is not null || receipt.PayoutTransactionId is not null))
            transaction.Update(balance, SlotsCreditUpdate(receipt.BalanceAfterCents, Timestamp.FromDateTime(settledAtUtc.UtcDateTime)));
        if (receipt.WagerTransactionId is not null)
            transaction.Create(wager, LegData(wager.Id, settlement, receipt, isLegacySlot, isWager: true, layout: ledgerLayout));
        if (receipt.PayoutTransactionId is not null)
            transaction.Create(payout, LegData(payout.Id, settlement, receipt, isLegacySlot, isWager: false));
        if (receipt.LegacyFreeGameTransactionId is not null)
            legacy!.StageSpend(transaction, settlement.UserId, receipt);
        var identity = ReceiptIdentity(settlement, settlementId, isLegacySlot, ledgerLayout);
        foreach (var (field, value) in ReceiptData(receipt, freeGamesBefore))
            identity.Add(field, value);
        transaction.Create(receiptDocument, identity);
        return receipt;
    }

    private static Dictionary<string, object> ReceiptIdentity(WalletRoundSettlement settlement, string id, bool isLegacySlot,
        WalletRoundLedgerLayout? layout = null)
    {
        var fields = new Dictionary<string, object>
        {
            ["settlementId"] = id,
            ["gameId"] = settlement.GameId,
            ["userId"] = settlement.UserId,
            ["currencyId"] = SlotsCreditsCurrencyId,
            ["operationId"] = settlement.OperationId,
            ["outcomeSha256"] = settlement.OutcomeSha256.ToLowerInvariant(),
            ["wagerCents"] = settlement.WagerCents,
            ["payoutCents"] = settlement.PayoutCents,
            ["ledgerLayout"] = isLegacySlot ? "legacy-slot" : layout?.EntryId is null ? "standard" : "arcade-entry",
            ["schemaVersion"] = 1L,
        };
        if (layout?.EntryAttemptId is { } attemptId) fields.Add("entryAttemptId", attemptId);
        if (settlement.RequestBodySha256 is { } body) fields.Add("requestBodySha256", body.ToLowerInvariant());
        if (settlement.SpendLegacyFreeGame) fields.Add("spendLegacyFreeGame", true);
        if (settlement.ExpectedLegacyFreeGamesRemaining is { } count) fields.Add("expectedLegacyFreeGamesRemaining", count);
        if (settlement.Phase != GameWalletSettlementPhase.Direct) fields.Add("phase", settlement.Phase.ToString());
        if (settlement.ReservationSha256 is { } hash) fields.Add("reservationSha256", hash.ToLowerInvariant());
        if (settlement.MinimumBalanceBeforeCents is { } minimum) fields.Add("minimumBalanceBeforeCents", minimum);
        return fields;
    }

    private static WalletRoundSettlementReceipt ReadReceipt(DocumentSnapshot document, WalletRoundSettlement settlement,
        bool isLegacySlot, string wagerId, string payoutId, string? freeGameId, WalletRoundLedgerLayout? layout = null)
    {
        var fields = document.ToDictionary();
        var identity = ReceiptIdentity(settlement, document.Id, isLegacySlot, layout);
        if (!MatchesFields(fields, identity) ||
            !Equals(fields.GetValueOrDefault("requestBodySha256"), identity.GetValueOrDefault("requestBodySha256")) ||
            (fields.GetValueOrDefault("spendLegacyFreeGame") is true) != settlement.SpendLegacyFreeGame ||
            !Equals(fields.GetValueOrDefault("expectedLegacyFreeGamesRemaining"), settlement.ExpectedLegacyFreeGamesRemaining) ||
            !Equals(fields.GetValueOrDefault("phase"), identity.GetValueOrDefault("phase")) ||
            !Equals(fields.GetValueOrDefault("reservationSha256"), identity.GetValueOrDefault("reservationSha256")) ||
            !Equals(fields.GetValueOrDefault("minimumBalanceBeforeCents"), settlement.MinimumBalanceBeforeCents))
            throw new WalletRoundSettlementConflictException();
        if (fields.GetValueOrDefault("balanceBeforeCents") is not long balanceBefore || balanceBefore < 0 ||
            fields.GetValueOrDefault("settledAtUtc") is not Timestamp originalTime)
            throw CorruptSettlement();

        var savedFreeGames = fields.GetValueOrDefault("legacyFreeGamesBefore") as long?;
        if (savedFreeGames < 0 || savedFreeGames is null &&
            (settlement.SpendLegacyFreeGame || settlement.ExpectedLegacyFreeGamesRemaining is not null))
            throw CorruptSettlement();
        var replay = CalculateReceipt(settlement, document.Id, wagerId, payoutId, balanceBefore,
            new DateTimeOffset(originalTime.ToDateTime()), wasAlreadySettled: true, freeGameId, savedFreeGames);
        if (!MatchesFields(fields, ReceiptData(replay, savedFreeGames)) ||
            !Equals(fields.GetValueOrDefault("legacyFreeGamesCountMismatch") ?? false, replay.LegacyFreeGamesCountMismatch))
            throw CorruptSettlement();
        return replay;
    }

    private static WalletRoundSettlement ReadLinkedSettlement(DocumentSnapshot document, WalletRoundSettlement settlement,
        GameWalletSettlementPhase phase)
    {
        if (!document.Exists)
            throw new WalletRoundSettlementConflictException();
        var fields = document.ToDictionary();
        if (!Equals(fields.GetValueOrDefault("phase"), phase == GameWalletSettlementPhase.Direct ? null : phase.ToString()))
            throw new WalletRoundSettlementConflictException();
        if (fields.GetValueOrDefault("outcomeSha256") is not string outcome ||
            fields.GetValueOrDefault("wagerCents") is not long wager || fields.GetValueOrDefault("payoutCents") is not long payout)
            throw CorruptSettlement();
        var linked = settlement with
        {
            OutcomeSha256 = outcome,
            WagerCents = wager,
            PayoutCents = payout,
            SpendLegacyFreeGame = fields.GetValueOrDefault("spendLegacyFreeGame") is true,
            RequestBodySha256 = fields.GetValueOrDefault("requestBodySha256") as string,
            ExpectedLegacyFreeGamesRemaining = fields.GetValueOrDefault("expectedLegacyFreeGamesRemaining") as long?,
            Phase = phase,
            ReservationSha256 = fields.GetValueOrDefault("reservationSha256") as string,
            MinimumBalanceBeforeCents = fields.GetValueOrDefault("minimumBalanceBeforeCents") as long?,
        };
        if (wager < 0 || payout < 0 || linked.ExpectedLegacyFreeGamesRemaining < 0 || !IsSha256(outcome) || !ValidPhase(linked) ||
            !GameWalletBalanceRequirement.IsValid(linked.Phase, linked.MinimumBalanceBeforeCents))
            throw CorruptSettlement();
        return linked;
    }

    private static bool IsDeclined(WalletRoundSettlementReceipt receipt) =>
        receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch;

    private static WalletRoundSettlementReceipt CalculateReceipt(WalletRoundSettlement settlement,
        string settlementId, string wagerId, string payoutId, long availableCents, DateTimeOffset settledAtUtc,
        bool wasAlreadySettled, string? freeGameId, long? freeGamesBefore)
    {
        var legacyCountMismatch = settlement.ExpectedLegacyFreeGamesRemaining is { } expected && expected != freeGamesBefore;
        var requiredCents = GameWalletBalanceRequirement.RequiredCents(settlement.WagerCents, settlement.MinimumBalanceBeforeCents);
        var insufficientFunds = !legacyCountMismatch && availableCents < requiredCents;
        var insufficientFreeGames = !legacyCountMismatch && settlement.SpendLegacyFreeGame && freeGamesBefore == 0;
        var allowed = !legacyCountMismatch && !insufficientFunds && !insufficientFreeGames;
        var spentFreeGame = allowed && settlement.SpendLegacyFreeGame;
        var afterWager = allowed ? checked(availableCents - settlement.WagerCents) : availableCents;
        var afterPayout = allowed ? checked(afterWager + settlement.PayoutCents) : afterWager;
        return new(settlementId,
            allowed && settlement.WagerCents > 0 ? wagerId : null,
            allowed && settlement.PayoutCents > 0 ? payoutId : null,
            availableCents, afterWager, afterPayout, settledAtUtc, wasAlreadySettled, insufficientFunds,
            insufficientFreeGames, spentFreeGame ? freeGameId : null, freeGamesBefore - (spentFreeGame ? 1 : 0), legacyCountMismatch);
    }

    private static Dictionary<string, object> ReceiptData(WalletRoundSettlementReceipt receipt, long? freeGamesBefore)
    {
        var fields = new Dictionary<string, object>
        {
            ["wagerTransactionId"] = receipt.WagerTransactionId!,
            ["payoutTransactionId"] = receipt.PayoutTransactionId!,
            ["balanceBeforeCents"] = receipt.BalanceBeforeCents,
            ["balanceAfterWagerCents"] = receipt.BalanceAfterWagerCents,
            ["balanceAfterCents"] = receipt.BalanceAfterCents,
            ["settledAtUtc"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
            ["insufficientFunds"] = receipt.InsufficientFunds,
        };
        if (freeGamesBefore is not null)
        {
            fields.Add("legacyFreeGamesBefore", freeGamesBefore.Value);
            fields.Add("legacyFreeGamesRemaining", receipt.LegacyFreeGamesRemaining!.Value);
            fields.Add("legacyFreeGameTransactionId", receipt.LegacyFreeGameTransactionId!);
            fields.Add("insufficientLegacyFreeGames", receipt.InsufficientLegacyFreeGames);
        }
        if (receipt.LegacyFreeGamesCountMismatch)
            fields.Add("legacyFreeGamesCountMismatch", true);
        return fields;
    }

    private sealed record LegacyFreeGameWallet(DocumentReference Balance, DocumentReference Ledger, string CurrencyId)
    {
        internal static LegacyFreeGameWallet Create(FirestoreDb database, WalletRoundSettlement settlement, string ledgerPrefix)
        {
            var currency = LegacyFreeGameCurrencyId(settlement.GameId);
            return new(FirestoreWalletDocuments.Balance(database, settlement.UserId, currency),
                Transaction(database, $"{ledgerPrefix}-free-game-use"), currency);
        }

        internal long ReadBalance(DocumentSnapshot snapshot, string userId) => ReadLegacyFreeGames(snapshot, userId, CurrencyId);

        internal void StageSpend(Transaction transaction, string userId, WalletRoundSettlementReceipt receipt)
        {
            transaction.Update(Balance, new Dictionary<string, object>
            {
                ["available"] = receipt.LegacyFreeGamesRemaining!.Value,
                ["version"] = FieldValue.Increment(1),
                ["updatedAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
            });
            transaction.Create(Ledger, LedgerData(userId, receipt));
        }

        internal void VerifyLedger(DocumentSnapshot snapshot, string userId, WalletRoundSettlementReceipt receipt)
        {
            if (receipt.LegacyFreeGameTransactionId is null ? snapshot.Exists :
                !snapshot.Exists || !MatchesFields(snapshot.ToDictionary(), LedgerData(userId, receipt)))
                throw CorruptSettlement();
        }

        private Dictionary<string, object> LedgerData(string userId, WalletRoundSettlementReceipt receipt) => new()
        {
            ["transactionId"] = Ledger.Id,
            ["userId"] = userId,
            ["currencyId"] = CurrencyId,
            ["amount"] = -1L,
            ["balanceAfter"] = receipt.LegacyFreeGamesRemaining!.Value,
            ["type"] = "free-game-use",
            ["idempotencyKey"] = Ledger.Id,
            ["createdAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
        };
    }

    private static Dictionary<string, object> LegData(string transactionId, WalletRoundSettlement settlement,
        WalletRoundSettlementReceipt receipt, bool isLegacySlot, bool isWager, WalletRoundLedgerLayout? layout = null)
    {
        var amountCents = isWager ? -settlement.WagerCents : settlement.PayoutCents;
        var balanceAfterCents = isWager ? receipt.BalanceAfterWagerCents : receipt.BalanceAfterCents;
        var attemptId = isWager ? layout?.EntryAttemptId : null;
        var type = attemptId is not null ? "arcade-competition-entry" : $"{(isLegacySlot ? "slot" : "game")}-{(isWager ? "wager" : "payout")}";
        var fields = RandTransactionIdentity(transactionId, settlement.UserId, amountCents, type, attemptId ?? transactionId);
        fields.Add("balanceAfter", (double)RandMoney.CentsToRand(balanceAfterCents));
        fields.Add("createdAt", Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime));
        if (!isLegacySlot)
        {
            fields.Add("amountCents", amountCents);
            fields.Add("balanceAfterCents", balanceAfterCents);
            fields.Add("gameId", settlement.GameId);
            fields.Add("operationId", settlement.OperationId);
            fields.Add("walletSettlementId", receipt.SettlementId);
            fields.Add("schemaVersion", 1L);
        }
        return fields;
    }

    private static void VerifyLeg(DocumentSnapshot snapshot, string? expectedId, WalletRoundSettlement settlement,
        WalletRoundSettlementReceipt receipt, bool isLegacySlot, bool isWager, WalletRoundLedgerLayout? layout = null)
    {
        if (expectedId is null ? snapshot.Exists :
            !snapshot.Exists || snapshot.Id != expectedId ||
            !MatchesFields(snapshot.ToDictionary(), LegData(expectedId, settlement, receipt, isLegacySlot, isWager, layout)))
            throw CorruptSettlement();
    }

    private static bool MatchesFields(IReadOnlyDictionary<string, object> stored, IReadOnlyDictionary<string, object> expected) =>
        expected.All(field => stored.TryGetValue(field.Key, out var value) && Equals(value, field.Value));

    private static InvalidOperationException CorruptSettlement() =>
        new("The wallet settlement receipt and ledger records do not agree.");

    private static void Validate(WalletRoundSettlement settlement, DateTimeOffset settledAtUtc, WalletRoundLedgerLayout? ledgerLayout)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ValidateIdentityPart(settlement.GameId, nameof(settlement.GameId));
        ValidateIdentityPart(settlement.UserId, nameof(settlement.UserId));
        ValidateIdentityPart(settlement.OperationId, nameof(settlement.OperationId));
        if (settlement.UserId.Contains('/'))
            throw new ArgumentException("The wallet user must be a document ID.", nameof(settlement));
        if (!IsSha256(settlement.OutcomeSha256))
            throw new ArgumentException("A SHA-256 digest of the prepared game outcome is required.", nameof(settlement));
        ArgumentOutOfRangeException.ThrowIfNegative(settlement.WagerCents);
        ArgumentOutOfRangeException.ThrowIfNegative(settlement.PayoutCents);
        if (settlement.SpendLegacyFreeGame && settlement.WagerCents != 0 && !settlement.AllowCashWagerWithLegacyFreeGame)
            throw new ArgumentException("A legacy free-game operation cannot charge a cash wager.", nameof(settlement));
        if (settlement.ExpectedLegacyFreeGamesRemaining < 0)
            throw new ArgumentOutOfRangeException(nameof(settlement), "The expected legacy free-game count cannot be negative.");
        if (!GameWalletBalanceRequirement.IsValid(settlement.Phase, settlement.MinimumBalanceBeforeCents))
            throw new ArgumentException("The minimum wallet balance is invalid for the requested phase.", nameof(settlement));
        if (!ValidPhase(settlement))
            throw new ArgumentException("The wallet operation terms are invalid for the requested phase.", nameof(settlement));
        if (settlement.RequestBodySha256 is not null && (settlement.RequestBodySha256.Length != 64 ||
            !settlement.RequestBodySha256.All(Uri.IsHexDigit)))
            throw new ArgumentException("The wallet request fingerprint is invalid.", nameof(settlement));
        if (settledAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Wallet settlement times must be UTC.", nameof(settledAtUtc));
        if (ledgerLayout?.SlotSpinId is { } spinId && (spinId == Guid.Empty || settlement.OperationId != spinId.ToString("N")))
            throw new ArgumentException("The wallet operation must match the legacy slot spin ID.", nameof(ledgerLayout));
        if (ledgerLayout?.EntryId is { } entryId && (settlement.OperationId != entryId ||
            settlement.Phase != GameWalletSettlementPhase.Direct || settlement.WagerCents <= 0 || settlement.PayoutCents != 0 ||
            settlement.IncludeLegacyFreeGameBalance || settlement.SpendLegacyFreeGame ||
            settlement.ExpectedLegacyFreeGamesRemaining is not null || settlement.MinimumBalanceBeforeCents is not null))
            throw new ArgumentException("The wallet operation must match the competition entry debit.", nameof(ledgerLayout));
    }

    private static bool ValidPhase(WalletRoundSettlement settlement) => settlement.Phase switch
    {
        GameWalletSettlementPhase.Direct => settlement.ReservationSha256 is null,
        GameWalletSettlementPhase.Reserve => settlement.PayoutCents == 0 && settlement.ReservationSha256 is null,
        GameWalletSettlementPhase.CompleteReservation => settlement.WagerCents == 0 && !settlement.SpendLegacyFreeGame &&
            settlement.ExpectedLegacyFreeGamesRemaining is null && IsSha256(settlement.ReservationSha256),
        _ => false,
    };

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void ValidateIdentityPart(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Wallet operation identifiers must be at most 256 characters and contain no control characters.", parameterName);
    }
}
