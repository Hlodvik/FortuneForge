using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Bots.Blackjack;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal static class BlackjackWalletFinancialAudit
{
    private const string Game = "blackjack";
    private const string SoloEvidence = "legacy-blackjack-cards-and-final-action-v1";
    private const string TableEvidence = "blackjack-final-cards-v1";
    private static readonly JsonSerializerOptions CommandJson = new(JsonSerializerDefaults.Web);

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(FirestoreDb database,
        RoundAuditSummary summary, string payloadJson, CancellationToken ct)
    {
        if (summary.GameId != Game || summary.EvidenceKind is not (SoloEvidence or TableEvidence)) return [];
        try
        {
            using var protectedEvidence = JsonDocument.Parse(payloadJson);
            var root = protectedEvidence.RootElement;
            var proof = root.TryGetProperty(RoundAuditFinancialProof.PropertyName, out var financial)
                ? ReadProtectedProof(financial, summary) : null;
            if (summary.EvidenceKind == TableEvidence)
                return proof is null ? [] : await CheckProtectedTableAsync(database, summary, root, proof, ct);

            var detail = root.GetProperty("data");
            var saved = detail.GetProperty("game").Deserialize<BlackjackGame>(RoundAuditCapture.Json);
            var terminalAction = detail.GetProperty("terminalAction").GetString();
            if (saved is null || summary.Participants.Count != 1 || summary.Participants[0] is not { IsAccountBacked: true } participant ||
                saved.GameId != summary.RoundId || saved.UserId != participant.ActorId || saved.Status != BlackjackStatuses.Completed ||
                saved.TotalWagerCents != participant.WagerCents || saved.PayoutCents != participant.PayoutCents ||
                root.GetProperty("sourceCollection").GetString() != BlackjackBackend.SoloGames ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                root.GetProperty("wagerCents").GetInt64() != participant.WagerCents ||
                root.GetProperty("payoutCents").GetInt64() != participant.PayoutCents ||
                !BlackjackSoloReplay.Verify(saved))
                return Error("blackjack-wallet-evidence-invalid", "The saved Blackjack hand differs from its protected settlement.");

            if (proof is not null)
                return await CheckProtectedSoloAsync(database, saved, terminalAction, proof, ct);

            var source = await database.Collection(BlackjackBackend.SoloGames).Document(summary.RoundId).GetSnapshotAsync(ct);
            var stored = BlackjackSoloCodec.Read(source);
            var startKey = Text(source, "startIdempotencyKey");
            if (string.IsNullOrWhiteSpace(startKey) || BlackjackSoloStore.Hash($"{saved.UserId}\n{startKey}") != saved.GameId ||
                !SameInstant(stored.CreatedAtUtc, saved.CreatedAtUtc) || !SameInstant(stored.UpdatedAtUtc, saved.UpdatedAtUtc) ||
                !SameGame(stored with { CreatedAtUtc = saved.CreatedAtUtc, UpdatedAtUtc = saved.UpdatedAtUtc }, saved))
                return Error("blackjack-wallet-evidence-invalid", "The original Blackjack result differs from the protected hand.");

            var owner = Text(source, "backendOwner");
            if (source.ContainsField("backendOwner") && owner != BlackjackBackend.Owner)
                return Error("blackjack-wallet-evidence-invalid", "The Blackjack hand has an unknown settlement owner.");
            if (owner is null && source.ContainsField("walletCommandIds"))
                return Error("blackjack-wallet-evidence-invalid", "The Blackjack wallet command history has no settlement owner.");

            IReadOnlyList<SavedSoloCommand> commands = owner is null ? [] :
                await ReadSoloCommandsAsync(database, source.GetValue<string[]>("walletCommandIds"), saved, ct);
            var legacyOpening = commands.Count == 0 || commands[0].Terms.ExpectedVersion != 0;
            var legacyTerminal = commands.Count == 0;
            if (!await CheckLegacySoloAsync(database, saved, startKey, legacyOpening, legacyTerminal, ct))
                return Error("blackjack-ledger-mismatch", "The historical Blackjack wager, double, or payout ledger differs from the protected hand.");
            if (legacyTerminal) return [];

            var verified = ReplaySoloCommands(commands, saved, startKey, terminalAction);
            if (verified is null || !await CheckSignedWalletAsync(database, verified, ct))
                return Error("blackjack-wallet-evidence-invalid", "The Blackjack command history or signed wallet records differ from the protected hand.");
            return [];
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException or
            OverflowException or FormatException or InvalidCastException or BlackjackConflictException or
            GameWalletUnavailableException)
        {
            return Error("blackjack-wallet-evidence-invalid", "The saved Blackjack evidence could not be reconciled with its wallet history.");
        }
    }

    private sealed record SoloCommand(string Id, string UserId, string GameId, string Fingerprint, string Action,
        string IdempotencyKey, int ExpectedVersion, BlackjackGame After, long WagerCents, long PayoutCents,
        GameRequestObservation? Observation, GameWalletSettlementReceipt? Wallet, long? BalanceCents);

    private sealed record SavedSoloCommand(SoloCommand Terms, string OutcomeSha256);

    private sealed record WalletLink(string Account, GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt);

    private sealed record ProtectedProof(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations);

    private static ProtectedProof ReadProtectedProof(JsonElement value, RoundAuditSummary summary)
    {
        if (value.GetProperty("backendOwner").GetString() != BlackjackBackend.Owner ||
            value.GetProperty("contractVersion").GetString() != BlackjackFinancialProof.Contract)
            throw new InvalidOperationException("Unknown protected Blackjack wallet evidence.");
        var entries = value.GetProperty("entries").Deserialize<RoundAuditLedgerEntry[]>(CommandJson);
        var operations = value.GetProperty("walletOperations").Deserialize<RoundAuditWalletOperation[]>(CommandJson);
        var collection = summary.EvidenceKind == SoloEvidence ? BlackjackBackend.SoloCommands : BlackjackBackend.TableWalletCommands;
        if (entries is null || operations is null || operations.Length == 0 ||
            entries.Any(entry => entry is null || entry.RoundReference != summary.RoundId || entry.AmountCents == 0 ||
                !GameWalletBatchVerifier.IsValidAccountId(entry.DocumentId) || !GameWalletBatchVerifier.IsValidAccountId(entry.UserId) ||
                (entry.EvidenceAvailable ? entry.Provenance is not (RoundAuditFinancialProof.LegacyHost or RoundAuditFinancialProof.SignedWallet)
                    : entry.Provenance != RoundAuditFinancialProof.LegacyHostUnavailable)) ||
            entries.Distinct().Count() != entries.Length ||
            operations.Any(operation => operation is null || operation.Collection != collection ||
                !GameSecurityEventDocuments.IsEventId(operation.DocumentId) ||
                !GameSecurityEventDocuments.IsEventId(operation.OutcomeSha256) || operation.LedgerEvidenceIds is null ||
                operation.ReceiptsByAccount is null || operation.BatchRequest is not null || operation.BatchReceipt is not null) ||
            operations.Select(operation => operation.DocumentId).Distinct(StringComparer.Ordinal).Count() != operations.Length)
            throw new InvalidOperationException("Malformed protected Blackjack wallet evidence.");
        return new(entries, operations);
    }

    private static async Task<IReadOnlyList<RoundAuditFinding>> CheckProtectedSoloAsync(FirestoreDb database,
        BlackjackGame game, string? terminalAction, ProtectedProof proof, CancellationToken ct)
    {
        var operations = proof.Operations.ToDictionary(operation => operation.DocumentId, StringComparer.Ordinal);
        var commands = await ReadSoloCommandsAsync(database, operations.Keys.ToArray(), game, ct);
        var links = ReplaySoloCommands(commands, game, commands[0].Terms.IdempotencyKey, terminalAction);
        if (links is null)
            return Error("blackjack-wallet-evidence-invalid", "The protected Blackjack commands do not replay to this hand.");

        var expected = new List<RoundAuditLedgerEntry>();
        for (var index = 0; index < links.Count; index++)
        {
            var (account, request, receipt) = links[index];
            var operation = operations[request.OperationId];
            var ledger = new List<RoundAuditLedgerEntry>(2);
            if (receipt.WagerTransactionId is { } wager)
                ledger.Add(new("balanceTransactions", wager, account,
                    commands[index].Terms.ExpectedVersion == 0 ? "blackjack-wager" : "blackjack-double",
                    game.GameId, -request.WagerCents, RoundAuditFinancialProof.SignedWallet, operation.DocumentId, receipt.SettlementId));
            if (receipt.PayoutTransactionId is { } payout)
                ledger.Add(new("balanceTransactions", payout, account, "blackjack-payout",
                    game.GameId, request.PayoutCents, RoundAuditFinancialProof.SignedWallet, operation.DocumentId, receipt.SettlementId));
            if (operation.OutcomeSha256 != request.OutcomeSha256 || operation.ReceiptsByAccount.Count != 1 ||
                !operation.ReceiptsByAccount.TryGetValue(account, out var recorded) || recorded != receipt ||
                !operation.LedgerEvidenceIds.SequenceEqual(ledger.Select(entry => entry.DocumentId), StringComparer.Ordinal))
                return Error("blackjack-wallet-evidence-invalid", "A protected Blackjack command differs from its signed receipt or ledger links.");
            expected.AddRange(ledger);
        }

        var legacy = proof.Entries.Where(entry => entry.Provenance != RoundAuditFinancialProof.SignedWallet).ToArray();
        if (commands[0].Terms.ExpectedVersion == 0 ? legacy.Length != 0 : legacy.Length != 1)
            return Error("blackjack-ledger-mismatch", "The protected Blackjack opening stake has unexpected historical coverage.");
        if (legacy.Length == 1)
        {
            var entry = legacy[0];
            if (entry != new RoundAuditLedgerEntry("balanceTransactions", game.GameId + "-wager", game.UserId,
                    "blackjack-wager", game.GameId, -game.WagerCents, entry.Provenance, EvidenceAvailable: entry.EvidenceAvailable))
                return Error("blackjack-ledger-mismatch", "The protected historical Blackjack wager differs from the hand.");
            if (entry.EvidenceAvailable)
            {
                var snapshot = await database.Collection(entry.Collection).Document(entry.DocumentId).GetSnapshotAsync(ct);
                if (!MatchesRecordedLedger(snapshot, entry) || Text(snapshot, "idempotencyKey") is not { Length: > 0 } key ||
                    BlackjackSoloStore.Hash($"{game.UserId}\n{key}") != game.GameId ||
                    !snapshot.TryGetValue<Timestamp>("createdAt", out var created) || !SameInstant(created.ToDateTime(), game.CreatedAtUtc))
                    return Error("blackjack-ledger-mismatch", "The original Blackjack wager ledger differs from the protected hand.");
            }
            expected.Add(entry);
        }
        if (!expected.ToHashSet().SetEquals(proof.Entries) || !await CheckSignedWalletAsync(database, links, ct))
            return Error("blackjack-wallet-evidence-invalid", "The protected Blackjack ledger differs from its signed wallet receipts.");
        return Coverage(proof);
    }

    private static bool MatchesRecordedLedger(DocumentSnapshot snapshot, RoundAuditLedgerEntry entry) =>
        snapshot.Exists && Text(snapshot, "transactionId") == entry.DocumentId &&
        FirestoreWalletEvidence.Legacy(snapshot, entry.RoundReference) == (entry with
        {
            Provenance = RoundAuditFinancialProof.LegacyHost, WalletOperationId = null, WalletSettlementId = null,
        });

    private static IReadOnlyList<RoundAuditFinding> Coverage(ProtectedProof proof) =>
        proof.Entries.Any(entry => !entry.EvidenceAvailable)
            ? [new(RoundAuditFinancialProof.UnavailableLegacyEvidenceCode, "warning",
                "At least one original ledger entry is unavailable for independent financial verification.")]
            : [];

    private sealed record TableWalletEffect(string AccountId, long DeltaCents, long MinimumBalanceBeforeCents);

    private static async Task<IReadOnlyList<RoundAuditFinding>> CheckProtectedTableAsync(FirestoreDb database,
        RoundAuditSummary summary, JsonElement root, ProtectedProof proof, CancellationToken ct)
    {
        var players = (root.GetProperty("players").Deserialize<BlackjackTablePlayer[]>(CommandJson) ?? [])
            .Where(player => player.TotalWagerCents > 0).ToDictionary(player => player.ActorId, StringComparer.Ordinal);
        if (root.GetProperty("tableId").GetString() != summary.TableId || root.GetProperty("roundNumber").GetInt32() != summary.RoundNumber ||
            summary.RoundId != $"{summary.TableId}-round-{summary.RoundNumber}" || !root.GetProperty("roundAccountingSettled").GetBoolean() ||
            summary.Participants.Count != players.Count || summary.Participants.Select(player => player.ActorId).Distinct().Count() != players.Count ||
            summary.Participants.Any(participant => !players.TryGetValue(participant.ActorId, out var player) ||
                participant.IsAccountBacked == BlackjackManagedSeat.IsManaged(player) ||
                participant.WagerCents != BlackjackTableEngine.TotalCommitted(player) || participant.PayoutCents != player.PayoutCents))
            return Error("blackjack-wallet-evidence-invalid", "The Blackjack table settlement differs from its saved game state.");

        var accountPlayers = summary.Participants.Where(participant => participant.IsAccountBacked)
            .ToDictionary(participant => participant.ActorId, StringComparer.Ordinal);
        if (proof.Entries.Any(entry => !accountPlayers.ContainsKey(entry.UserId) || !ValidTableLedger(entry)) ||
            accountPlayers.Values.Any(participant => !TableAmountsMatch(players[participant.ActorId],
                proof.Entries.Where(entry => entry.UserId == participant.ActorId).ToArray())))
            return Error("blackjack-ledger-mismatch", "The protected Blackjack table wagers or returns differ from its saved hands.");

        var operations = proof.Operations.ToDictionary(operation => operation.DocumentId, StringComparer.Ordinal);
        var collection = database.Collection(BlackjackBackend.TableWalletCommands);
        var documents = await database.GetAllSnapshotsAsync(operations.Keys.Select(collection.Document), ct);
        var links = new List<WalletLink>();
        var expected = new HashSet<RoundAuditLedgerEntry>();
        foreach (var document in documents)
        {
            var operation = operations[document.Id];
            var raw = Text(document, "payloadJson");
            if (raw is null || Text(document, "sha256") != BlackjackTableIds.Hash(raw) ||
                !document.TryGetValue<long>("schemaVersion", out var schema) || schema != 1)
                return Error("blackjack-wallet-evidence-invalid", "A protected Blackjack table command is missing or corrupt.");

            using var parsed = JsonDocument.Parse(raw);
            var state = parsed.RootElement;
            var terms = state.GetProperty("terms");
            var actor = terms.GetProperty("userId").GetString();
            var receipts = state.GetProperty("receipts").Deserialize<Dictionary<string, GameWalletSettlementReceipt>>(CommandJson);
            var effects = terms.GetProperty("effects").Deserialize<TableWalletEffect[]>(CommandJson);
            var ledger = terms.GetProperty("journal").GetProperty("ledger").Deserialize<BlackjackTableLedgerEntry[]>(CommandJson);
            if (terms.GetProperty("id").GetString() != operation.DocumentId || terms.GetProperty("stateId").GetString() != Text(document, "stateId") ||
                Text(document, "userId") != (actor ?? "") || !state.GetProperty("finalized").GetBoolean() || state.GetProperty("declined").GetBoolean() ||
                BlackjackTableIds.Hash(terms.GetRawText()) != operation.OutcomeSha256 ||
                receipts is null || effects is null || ledger is null || effects.Length != receipts.Count ||
                receipts.Count != operation.ReceiptsByAccount.Count ||
                !operation.LedgerEvidenceIds.SequenceEqual(ledger.Select(entry => entry.Id), StringComparer.Ordinal))
                return Error("blackjack-wallet-evidence-invalid", "A Blackjack table command differs from its protected wallet operation.");

            var aggregate = GameWalletJournal.Aggregate(ledger.Select(entry => (entry.UserId, entry.AmountCents)))
                .ToDictionary(effect => effect.AccountId, StringComparer.Ordinal);
            if (effects.Select(effect => effect.AccountId).Distinct(StringComparer.Ordinal).Count() != effects.Length ||
                aggregate.Keys.Any(account => !receipts.ContainsKey(account)))
                return Error("blackjack-wallet-evidence-invalid", "A Blackjack table wallet journal has incomplete account effects.");
            foreach (var effect in effects)
            {
                var account = effect.AccountId;
                var funding = aggregate.GetValueOrDefault(account) ?? new(account, 0, 0);
                var request = new GameWalletSettlementRequest(BlackjackTableIds.Hash($"{operation.DocumentId}\n{account}"),
                    operation.OutcomeSha256, Math.Max(0, -effect.DeltaCents), Math.Max(0, effect.DeltaCents),
                    MinimumBalanceBeforeCents: effect.MinimumBalanceBeforeCents > 0 ? effect.MinimumBalanceBeforeCents : null);
                if (effect.DeltaCents != funding.NetCents || effect.MinimumBalanceBeforeCents != funding.MinimumBalanceBeforeCents ||
                    effect.MinimumBalanceBeforeCents > 0 && account != actor ||
                    !receipts.TryGetValue(account, out var receipt) || receipt is not { InsufficientFunds: false, WasAlreadySettled: false } ||
                    !operation.ReceiptsByAccount.TryGetValue(account, out var recorded) || receipt != recorded ||
                    !GameWalletReceiptVerifier.MatchesSettlement(receipt, Game, account, request))
                    return Error("blackjack-wallet-evidence-invalid", "A protected Blackjack table receipt does not settle its exact journal effects.");
                links.Add(new(account, request, receipt));
            }
            foreach (var entry in ledger.Where(entry => entry.Reference == summary.RoundId && accountPlayers.ContainsKey(entry.UserId)))
                if (!expected.Add(new(BlackjackBackend.TableLedgerEvidence, entry.Id, entry.UserId, entry.Type, entry.Reference,
                    entry.AmountCents, RoundAuditFinancialProof.SignedWallet, operation.DocumentId, receipts[entry.UserId].SettlementId)))
                    return Error("blackjack-ledger-mismatch", "A Blackjack table ledger is linked to more than one operation.");
        }

        if (!expected.SetEquals(proof.Entries.Where(entry => entry.Provenance == RoundAuditFinancialProof.SignedWallet)))
            return Error("blackjack-ledger-mismatch", "The Blackjack table journal differs from its protected ledger links.");
        foreach (var entry in proof.Entries.Where(entry => entry.EvidenceAvailable))
        {
            var saved = await database.Collection(entry.Collection).Document(entry.DocumentId).GetSnapshotAsync(ct);
            if (!MatchesRecordedLedger(saved, entry) || Text(saved, "idempotencyKey") != summary.RoundId ||
                Text(saved, "walletMutationId") != entry.WalletOperationId || Text(saved, "walletSettlementId") != entry.WalletSettlementId)
                return Error("blackjack-ledger-mismatch", "An exact Blackjack table ledger record differs from protected evidence.");
            if (entry.WalletOperationId is { } id)
            {
                var receipt = operations[id].ReceiptsByAccount[entry.UserId];
                if (!saved.TryGetValue<long>("walletBalanceBeforeCents", out var before) || before != receipt.BalanceBeforeCents ||
                    !saved.TryGetValue<long>("walletBalanceAfterCents", out var after) || after != receipt.BalanceAfterCents)
                    return Error("blackjack-ledger-mismatch", "A Blackjack table ledger record has different signed balances.");
            }
        }
        return await CheckSignedWalletAsync(database, links, ct) ? Coverage(proof) :
            Error("blackjack-wallet-evidence-invalid", "The protected Blackjack table receipts differ from the platform wallet.");
    }

    private static bool ValidTableLedger(RoundAuditLedgerEntry entry)
    {
        if (entry.Provenance == RoundAuditFinancialProof.LegacyHostUnavailable)
            return entry.Collection == BlackjackBackend.TableState && entry.Kind == "legacy-prepaid-stake" &&
                entry.AmountCents < 0 && entry.WalletOperationId is null && entry.WalletSettlementId is null;
        var linked = entry.Provenance == RoundAuditFinancialProof.SignedWallet;
        if (linked ? entry.Collection != BlackjackBackend.TableLedgerEvidence || entry.WalletOperationId is null || entry.WalletSettlementId is null
            : entry.Collection != "balanceTransactions" || entry.WalletOperationId is not null || entry.WalletSettlementId is not null)
            return false;
        return entry.Kind switch
        {
            "blackjack-table-wager" or "blackjack-table-split" or "blackjack-table-double" or "blackjack-table-insurance" => entry.AmountCents < 0,
            "blackjack-table-wager-adjustment" => true,
            "blackjack-table-wager-release" or "blackjack-table-payout" => entry.AmountCents > 0,
            _ => false,
        };
    }

    private static bool TableAmountsMatch(BlackjackTablePlayer player, IReadOnlyList<RoundAuditLedgerEntry> ledger)
    {
        var paid = ledger.Where(entry => entry.Kind == "blackjack-table-payout").Sum(entry => entry.AmountCents);
        var committed = checked(paid - ledger.Sum(entry => entry.AmountCents));
        if (paid != player.PayoutCents || committed != BlackjackTableEngine.TotalCommitted(player)) return false;
        if (ledger.Any(entry => !entry.EvidenceAvailable)) return true;

        long Stake(string kind) => checked(-ledger.Where(entry => entry.Kind == kind).Sum(entry => entry.AmountCents));
        var split = player.SecondaryHand?.WagerCents ?? 0;
        var doubles = checked(player.TotalWagerCents - player.WagerCents + (player.SecondaryHand?.TotalWagerCents ?? 0) - split);
        return Stake("blackjack-table-split") == split && Stake("blackjack-table-double") == doubles &&
            Stake("blackjack-table-insurance") == player.InsuranceWagerCents;
    }

    private static async Task<IReadOnlyList<SavedSoloCommand>> ReadSoloCommandsAsync(FirestoreDb database,
        IReadOnlyList<string> ids, BlackjackGame game, CancellationToken ct)
    {
        if (ids.Count == 0 || ids.Count > game.Version ||
            ids.Any(id => !GameSecurityEventDocuments.IsEventId(id)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("Invalid Blackjack wallet commands.");

        var collection = database.Collection(BlackjackBackend.SoloCommands);
        var documents = await database.GetAllSnapshotsAsync(ids.Select(collection.Document), ct);
        return documents.Select(document =>
        {
            var raw = Text(document, "payloadJson");
            if (raw is null || Text(document, "sha256") != BlackjackSoloStore.Hash(raw) ||
                Text(document, "userId") != game.UserId || Text(document, "gameId") != game.GameId ||
                !document.TryGetValue<long>("schemaVersion", out var schema) || schema != 1)
                throw new InvalidOperationException("Invalid Blackjack wallet command identity.");
            var command = JsonSerializer.Deserialize<SoloCommand>(raw, CommandJson);
            if (command is null || command.Id != document.Id || command.UserId != game.UserId || command.GameId != game.GameId ||
                command.Fingerprint != Text(document, "fingerprint") ||
                command.Observation is { } observation && (observation.UserId != game.UserId || observation.GameId != Game))
                throw new InvalidOperationException("Invalid Blackjack wallet command scope.");
            using var parsed = JsonDocument.Parse(raw);
            return new SavedSoloCommand(command, FrozenSoloHash(parsed.RootElement));
        }).OrderBy(command => command.Terms.ExpectedVersion).ToArray();
    }

    private static string FrozenSoloHash(JsonElement command)
    {
        // Only the receipt and displayed balance are added after the wallet binds the frozen command.
        var fields = command.EnumerateObject().Select(field =>
            JsonSerializer.Serialize(field.Name, CommandJson) + ":" +
            (field.Name is "wallet" or "balanceCents" ? "null" : field.Value.GetRawText()));
        return BlackjackSoloStore.Hash("{" + string.Join(",", fields) + "}");
    }

    private static IReadOnlyList<WalletLink>? ReplaySoloCommands(IReadOnlyList<SavedSoloCommand> commands,
        BlackjackGame saved, string? openingKey, string? terminalAction)
    {
        var replay = BlackjackRules.Deal(saved.GameId, saved.UserId, saved.WagerCents, saved.Deck, saved.CreatedAtUtc);
        // An active legacy prefix can contain hits. Its stand or double would already have finished the hand.
        while (replay.Version < commands[0].Terms.ExpectedVersion && replay.Status == BlackjackStatuses.Active)
            replay = BlackjackRules.ApplyAction(replay, BlackjackActions.Hit, saved.CreatedAtUtc);
        var links = new List<WalletLink>(commands.Count);
        foreach (var (command, outcomeSha256) in commands)
        {
            if (command.After is not { } after || string.IsNullOrWhiteSpace(command.IdempotencyKey) || string.IsNullOrWhiteSpace(command.Action) ||
                command.ExpectedVersion < 0 || command.ExpectedVersion > 0 && command.ExpectedVersion != replay.Version ||
                command.ExpectedVersion == 0 && (links.Count != 0 || command.IdempotencyKey != openingKey || command.Action != "deal" ||
                    BlackjackSoloStore.Hash($"{saved.UserId}\n{command.IdempotencyKey}") != saved.GameId) ||
                after.UpdatedAtUtc < replay.UpdatedAtUtc || after.UpdatedAtUtc > saved.UpdatedAtUtc)
                return null;

            var isDeal = command.ExpectedVersion == 0;
            var expected = isDeal ? replay : BlackjackRules.ApplyAction(replay, command.Action, after.UpdatedAtUtc);
            var wager = isDeal ? saved.WagerCents : checked(expected.TotalWagerCents - replay.TotalWagerCents);
            var payout = expected.Status == BlackjackStatuses.Completed ? expected.PayoutCents : 0;
            var kind = isDeal ? "start" : "action";
            var fingerprint = isDeal ? $"{saved.GameId}\nstart\n{wager}" : $"{saved.GameId}\n{replay.Version}\n{command.Action}";
            if (command.Id != BlackjackSoloStore.Hash($"{saved.UserId}\n{kind}\n{command.IdempotencyKey}") ||
                command.Fingerprint != BlackjackSoloStore.Hash(fingerprint) ||
                !SameReplayedGame(after, expected) || command.WagerCents != wager || command.PayoutCents != payout ||
                command.Wallet is not { WasAlreadySettled: false, InsufficientFunds: false } receipt ||
                command.BalanceCents != receipt.BalanceAfterCents)
                return null;

            var request = new GameWalletSettlementRequest(command.Id, outcomeSha256, wager, payout);
            if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, Game, saved.UserId, request)) return null;
            links.Add(new WalletLink(saved.UserId, request, receipt));
            replay = expected;
        }
        return SameGame(replay, saved) && commands[^1].Terms.Action == terminalAction ? links : null;
    }

    private static bool SameReplayedGame(BlackjackGame frozen, BlackjackGame replayed) =>
        // A later command's base hand has passed through Firestore's microsecond timestamp fields.
        frozen.CreatedAtUtc.Ticks / TimeSpan.TicksPerMicrosecond == replayed.CreatedAtUtc.Ticks / TimeSpan.TicksPerMicrosecond &&
        frozen.UpdatedAtUtc.Ticks / TimeSpan.TicksPerMicrosecond == replayed.UpdatedAtUtc.Ticks / TimeSpan.TicksPerMicrosecond &&
        SameGame(frozen with { CreatedAtUtc = replayed.CreatedAtUtc, UpdatedAtUtc = replayed.UpdatedAtUtc }, replayed);

    private static async Task<bool> CheckSignedWalletAsync(FirestoreDb database, IReadOnlyList<WalletLink> links,
        CancellationToken ct) => await database.RunTransactionAsync(async transaction =>
    {
        foreach (var (account, request, receipt) in links)
        {
            var bodySha = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson)));
            var wallet = await FirestoreWalletRoundSettlementStore.StageAsync(database, transaction,
                new(Game, account, request.OperationId, request.OutcomeSha256, request.WagerCents, request.PayoutCents,
                    RequestBodySha256: bodySha, Phase: request.Phase, ReservationSha256: request.ReservationSha256,
                    MinimumBalanceBeforeCents: request.MinimumBalanceBeforeCents), receipt.SettledAtUtc, ct, requireExisting: true);
            if ((wallet.ToWireReceipt() with { WasAlreadySettled = false }) != receipt) return false;
        }
        return true;
    }, cancellationToken: ct);

    private static async Task<bool> CheckLegacySoloAsync(FirestoreDb database, BlackjackGame game, string startKey,
        bool legacyOpening, bool legacyTerminal, CancellationToken ct)
    {
        var transactions = database.Collection("balanceTransactions");
        var records = await database.GetAllSnapshotsAsync(new[] { "wager", "double", "payout" }
            .Select(leg => transactions.Document($"{game.GameId}-{leg}")), ct);
        return LegacyLedger(records[0], game.UserId, "blackjack-wager", legacyOpening ? -game.WagerCents : 0, startKey, game.CreatedAtUtc) &&
            LegacyLedger(records[1], game.UserId, "blackjack-double", legacyTerminal ? game.WagerCents - game.TotalWagerCents : 0, null, game.UpdatedAtUtc) &&
            LegacyLedger(records[2], game.UserId, "blackjack-payout", legacyTerminal ? game.PayoutCents : 0, null, game.UpdatedAtUtc);
    }

    private static bool LegacyLedger(DocumentSnapshot record, string user, string type, long amount, string? key, DateTime occurredAt) =>
        amount == 0 ? !record.Exists : record.Exists && Text(record, "transactionId") == record.Id &&
        Text(record, "userId") == user && Text(record, "type") == type && Text(record, "currencyId") == "slotsCredits" &&
        Text(record, "idempotencyKey") is { Length: > 0 } idempotencyKey && (key is null || idempotencyKey == key) &&
        record.TryGetValue<double>("amount", out var rand) && rand == (double)BlackjackMoney.ToRand(amount) &&
        record.TryGetValue<double>("balanceAfter", out var balance) && double.IsFinite(balance) && balance >= 0 &&
        record.TryGetValue<Timestamp>("createdAt", out var timestamp) && SameInstant(timestamp.ToDateTime(), occurredAt);

    private static bool SameGame(BlackjackGame first, BlackjackGame second) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(first, CommandJson), JsonSerializer.SerializeToElement(second, CommandJson));

    private static bool SameInstant(DateTime first, DateTime second) => Math.Abs((first - second).Ticks) < TimeSpan.TicksPerMicrosecond;

    private static string? Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : null;

    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];
}
