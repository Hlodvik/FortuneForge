using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Accounts.Storage;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Replays every persisted Mines transition from the initial board through terminal settlement.</summary>
internal static class MinesSequenceReplayAudit
{
    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectFinancialAsync(
        FirestoreDb database, RoundAuditSummary summary, string? terminalPayloadJson, CancellationToken ct)
    {
        if (summary.GameId != "mines" || summary.AuditorVersion != MinesOutcomeAudit.Version) return [];
        if (terminalPayloadJson is null || summary.Participants.Count != 1)
            return Error("mines-financial-evidence-missing", "The Mines financial pass lacks a private round or a single account participant.");
        try
        {
            using var terminal = JsonDocument.Parse(terminalPayloadJson);
            var settled = ReadRound(terminal.RootElement.GetProperty("data"));
            var sourceTask = database.Collection("minesRounds").Document(summary.RoundId).GetSnapshotAsync(ct);
            var resultTask = database.Collection("cardGameResults").Document($"mines-{summary.RoundId}").GetSnapshotAsync(ct);
            var ledgerTask = database.Collection("balanceTransactions").WhereEqualTo("roundId", summary.RoundId)
                .Limit(4).GetSnapshotAsync(ct);
            await Task.WhenAll(sourceTask, resultTask, ledgerTask);
            var source = await sourceTask;
            var result = await resultTask;
            var ledger = await ledgerTask;
            if (!source.Exists || !source.TryGetValue<string>("privateRoundJson", out var sourceJson) ||
                string.IsNullOrWhiteSpace(sourceJson))
                return Error("mines-source-round-missing", "The stored Mines source round is unavailable for financial reconciliation.");
            using var sourceDocument = JsonDocument.Parse(sourceJson);
            var sourceRound = ReadRound(sourceDocument.RootElement);
            if (sourceRound.RoundId != settled.RoundId || sourceRound.UserId != settled.UserId ||
                sourceRound.Version != settled.Version || sourceRound.CreatedAt != settled.CreatedAt ||
                sourceRound.UpdatedAt != settled.UpdatedAt || sourceRound.ReservationSha256 != settled.ReservationSha256 ||
                !SameState(sourceRound.State, settled.State))
                return Error("mines-source-round-mismatch", "The Mines source round differs from its private terminal evidence.");
            var participant = summary.Participants[0];
            if (settled.RoundId != summary.RoundId || settled.UserId != participant.ActorId ||
                settled.State.Status == MinesRoundStatus.Active || settled.State.WagerCents != participant.WagerCents ||
                settled.State.PayoutCents != participant.PayoutCents || !result.Exists || Text(result, "game") != "mines" ||
                Text(result, "userId") != participant.ActorId ||
                Text(result, "matchId") != summary.RoundId || Text(result, "settlementStatus") != "paid" ||
                Number(result, "wagerCents") != participant.WagerCents ||
                Number(result, "payoutCents") != participant.PayoutCents ||
                Number(result, "netCents") != participant.NetCents)
                return Error("mines-result-mismatch", "The settled Mines result document is missing or differs from the private round.");
            if (settled.ReservationSha256 is { } reservation)
                return ledger.Count == 0 && await PaidReservationMatchesAsync(database, settled, reservation, ct)
                    ? [] : Error("mines-ledger-mismatch", "The Mines reservation or completion differs from the private settlement.");

            var wager = ledger.Documents.Where(item => Text(item, "type") == "mines-wager").ToArray();
            var payout = ledger.Documents.Where(item => Text(item, "type") == "mines-payout").ToArray();
            var availableTerminal = await FindMixedTerminalWalletAsync(database, settled, ct);
            var expectsLegacyCredit = availableTerminal is null && participant.PayoutCents > 0;
            if (ledger.Count != 1 + (expectsLegacyCredit ? 1 : 0) || wager.Length != 1 ||
                payout.Length != (expectsLegacyCredit ? 1 : 0) || !Entry(wager[0], participant.ActorId, -participant.WagerCents) ||
                (payout.Length == 1 && !Entry(payout[0], participant.ActorId, participant.PayoutCents)) ||
                (availableTerminal is not null && !await PaidWalletRequestsMatchAsync(database, settled.UserId, [availableTerminal], ct)))
                return Error("mines-ledger-mismatch", "The Mines wager debit or payout credit is missing or differs from the private settlement.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Error("mines-financial-evidence-invalid", "The saved Mines source or financial records cannot be parsed for reconciliation.");
        }
    }

    private static Task<bool> PaidReservationMatchesAsync(FirestoreDb database, MinesStoredRound settled,
        string reservation, CancellationToken ct)
    {
        var state = settled.State;
        var opening = settled with
        {
            Version = 1,
            UpdatedAt = settled.CreatedAt,
            ReservationSha256 = null,
            State = new(state.WagerCents, state.MineCount, state.MinePositions, [], MinesRoundStatus.Active, 0,
                state.HouseEdgeBasisPoints, state.PayoutPolicyVersion),
        };
        if (MinesService.Hash(JsonSerializer.Serialize(opening)) != reservation)
            return Task.FromResult(false);
        GameWalletSettlementRequest hold = new(settled.RoundId, reservation, state.WagerCents, 0,
            Phase: GameWalletSettlementPhase.Reserve);
        GameWalletSettlementRequest terminal = new(settled.RoundId, MinesService.Hash(JsonSerializer.Serialize(settled)),
            0, state.PayoutCents, Phase: GameWalletSettlementPhase.CompleteReservation, ReservationSha256: reservation);
        return PaidWalletRequestsMatchAsync(database, settled.UserId, [hold, terminal], ct);
    }

    private static async Task<GameWalletSettlementRequest?> FindMixedTerminalWalletAsync(FirestoreDb database,
        MinesStoredRound settled, CancellationToken ct)
    {
        // A legacy-start round lacks a reservation ID. Its new terminal action is identified by the frozen outcome digest.
        var hash = MinesService.Hash(JsonSerializer.Serialize(settled));
        var matches = await database.Collection("walletRoundSettlements").WhereEqualTo("outcomeSha256", hash)
            .Limit(3).GetSnapshotAsync(ct);
        var match = matches.Documents.SingleOrDefault(item => Text(item, "gameId") == "mines" && Text(item, "userId") == settled.UserId);
        if (match is null) return null;
        var operationId = Text(match, "operationId");
        if (operationId.Length != 64 || operationId.Any(character => !char.IsAsciiHexDigit(character)) ||
            match.Id != GameWalletSettlementIdentity.CreateId("mines", settled.UserId, operationId))
            throw new InvalidOperationException("The legacy Mines round links to an invalid wallet operation.");
        GameWalletSettlementRequest expected = new(operationId, hash, 0, settled.State.PayoutCents);
        var command = await database.Collection("minesCommands").Document(operationId).GetSnapshotAsync(ct);
        if (!command.Exists || Number(command, "walletProtocol") != 1 || Text(command, "userId") != settled.UserId ||
            !command.ContainsField("responseJson"))
            throw new InvalidOperationException("The Mines terminal wallet operation lacks its accepted command.");
        using var pending = JsonDocument.Parse(Text(command, "pendingJson"));
        var intent = pending.RootElement;
        var request = intent.GetProperty("WalletRequest").Deserialize<GameWalletSettlementRequest>();
        var round = intent.GetProperty("Mutation").GetProperty("Round").Deserialize<MinesStoredRound>();
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(Text(command, "walletReceiptJson"));
        if (request != expected || round is null || MinesService.Hash(JsonSerializer.Serialize(round)) != hash ||
            receipt is null || receipt.InsufficientFunds || receipt.WasAlreadySettled ||
            !GameWalletReceiptVerifier.MatchesSettlement(receipt, "mines", settled.UserId, expected))
            throw new InvalidOperationException("The Mines terminal command does not match its archived wallet evidence.");
        return expected;
    }

    private static async Task<bool> PaidWalletRequestsMatchAsync(FirestoreDb database, string actor,
        IReadOnlyList<GameWalletSettlementRequest> requests, CancellationToken ct)
    {
        try
        {
            return await database.RunTransactionAsync(async transaction =>
            {
                foreach (var request in requests)
                {
                    var bodyHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson)));
                    var archived = await FirestoreWalletRoundSettlementStore.StageAsync(database, transaction,
                        new("mines", actor, request.OperationId, request.OutcomeSha256, request.WagerCents, request.PayoutCents,
                            RequestBodySha256: bodyHash, Phase: request.Phase, ReservationSha256: request.ReservationSha256),
                        DateTimeOffset.UnixEpoch, ct, requireExisting: true);
                    if (archived.InsufficientFunds)
                        return false;
                }
                return true;
            }, cancellationToken: ct);
        }
        catch (InvalidOperationException) { return false; }
    }

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? terminalPayloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (summary.GameId != "mines" || summary.AuditorVersion != MinesOutcomeAudit.Version) return [];
        if (terminalPayloadJson is null) return Error("mines-terminal-evidence-missing", "The private terminal round is missing.");
        try
        {
            using var terminal = JsonDocument.Parse(terminalPayloadJson);
            var root = terminal.RootElement;
            var record = ReadRound(root.GetProperty("data"));
            if (summary.EvidenceKind != "mines-private-board-and-reveals-v1" ||
                root.GetProperty("sourceCollection").GetString() != "minesRounds" ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                record.RoundId != summary.RoundId || summary.Participants.Count != 1 ||
                record.UserId != summary.Participants[0].ActorId ||
                root.GetProperty("wagerCents").GetInt64() != record.State.WagerCents ||
                root.GetProperty("payoutCents").GetInt64() != record.State.PayoutCents ||
                root.GetProperty("netCents").GetInt64() != checked(record.State.PayoutCents - record.State.WagerCents) ||
                summary.Participants[0].WagerCents != record.State.WagerCents ||
                summary.Participants[0].PayoutCents != record.State.PayoutCents ||
                summary.Participants[0].NetCents != checked(record.State.PayoutCents - record.State.WagerCents) ||
                !MatchesSummaryTimestamp(summary.StartedAtUtc, record.CreatedAt.UtcDateTime) ||
                !MatchesSummaryTimestamp(summary.CompletedAtUtc, record.UpdatedAt.UtcDateTime) ||
                !MinesOutcomeAudit.Verify(record, summary.Participants[0].PayoutCents))
                return Error("mines-terminal-replay-mismatch", "The saved Mines round, independent payout, or audit summary disagree.");

            var events = new List<(long Version, DateTime OccurredAtUtc, MinesStoredRound Round)>();
            foreach (var payload in eventPayloads)
            {
                using var observation = JsonDocument.Parse(payload);
                var envelope = observation.RootElement;
                events.Add((envelope.GetProperty("version").GetInt64(),
                    envelope.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    ReadRound(envelope.GetProperty("state"))));
            }
            events.Sort((a, b) => a.Version.CompareTo(b.Version));
            if (events.Count != record.Version || events.Count == 0 ||
                events[0].Version != 1 || events[0].Round.State.Status != MinesRoundStatus.Active ||
                events[0].Round.State.RevealedTiles.Length != 0 || events[0].Round.State.PayoutCents != 0)
                return Error("mines-action-sequence-incomplete", "The saved Mines observation stream does not begin with the accepted empty board or has missing turns.");
            for (var i = 0; i < events.Count; i++)
            {
                var current = events[i];
                if (current.Version != i + 1 || current.Round.Version != current.Version ||
                    current.Round.RoundId != record.RoundId || current.Round.UserId != record.UserId ||
                    current.Round.CreatedAt != record.CreatedAt ||
                    current.Round.ReservationSha256 != record.ReservationSha256 ||
                    current.Round.UpdatedAt.UtcDateTime != current.OccurredAtUtc ||
                    current.Round.UpdatedAt > record.UpdatedAt ||
                    !SameTermsAndBoard(current.Round.State, record.State))
                    return Error("mines-action-sequence-mismatch", "An accepted Mines observation changes the board, terms, identity, version, or server time.");
                if (i == 0) continue;
                var previous = events[i - 1].Round;
                if (previous.State.Status != MinesRoundStatus.Active ||
                    current.Round.UpdatedAt < previous.UpdatedAt ||
                    !ValidTransition(previous.State, current.Round.State))
                    return Error("mines-action-sequence-mismatch", "The saved Mines reveal or cash-out sequence is not a legal transition.");
            }
            if (!SameState(events[^1].Round.State, record.State) || events[^1].Round.UpdatedAt != record.UpdatedAt)
                return Error("mines-action-terminal-mismatch", "The final observed Mines state differs from the settled private round.");
            return [];
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Error("mines-replay-evidence-invalid", "The private Mines round or observation stream cannot be parsed for sequence replay.");
        }
    }

    // The summary uses Firestore timestamps while the frozen JSON retains all .NET ticks.
    private static bool MatchesSummaryTimestamp(DateTime summary, DateTime frozen) =>
        summary == frozen || summary == frozen.AddTicks(-(frozen.Ticks % TimeSpan.TicksPerMicrosecond));

    private static MinesStoredRound ReadRound(JsonElement json) =>
        JsonSerializer.Deserialize<MinesStoredRound>(json.GetRawText(), RoundAuditCapture.Json) ??
        throw new JsonException("Missing Mines round.");

    private static bool Entry(DocumentSnapshot item, string userId, long cents) =>
        Text(item, "userId") == userId && Text(item, "currencyId") == "slotsCredits" &&
        Number(item, "amountCents") == cents && Text(item, "transactionId") == item.Id &&
        item.TryGetValue<double>("amount", out var amount) && SameCents(amount, cents) &&
        item.TryGetValue<long>("balanceAfterCents", out var afterCents) &&
        item.TryGetValue<double>("balanceAfter", out var after) && SameCents(after, afterCents);

    private static bool SameCents(double amount, long cents) =>
        double.IsFinite(amount) && Math.Abs(amount * 100d - cents) < 0.000001d;

    private static string Text(DocumentSnapshot item, string field) =>
        item.TryGetValue<string>(field, out var value) ? value : "";

    private static long Number(DocumentSnapshot item, string field) =>
        item.TryGetValue<long>(field, out var value) ? value : long.MinValue;

    private static bool SameTermsAndBoard(MinesRoundState a, MinesRoundState b) =>
        a.WagerCents == b.WagerCents && a.MineCount == b.MineCount &&
        a.HouseEdgeBasisPoints == b.HouseEdgeBasisPoints && a.PayoutPolicyVersion == b.PayoutPolicyVersion &&
        a.MinePositions.SequenceEqual(b.MinePositions);

    private static bool SameState(MinesRoundState a, MinesRoundState b) =>
        SameTermsAndBoard(a, b) && a.RevealedTiles.SequenceEqual(b.RevealedTiles) &&
        a.Status == b.Status && a.PayoutCents == b.PayoutCents;

    private static bool ValidTransition(MinesRoundState before, MinesRoundState after)
    {
        var oldTiles = before.RevealedTiles;
        var newTiles = after.RevealedTiles;
        if (newTiles.Length == oldTiles.Length)
            return oldTiles.Length > 0 && oldTiles.SequenceEqual(newTiles) && after.Status == MinesRoundStatus.CashedOut;
        if (newTiles.Length != oldTiles.Length + 1 || !newTiles.Take(oldTiles.Length).SequenceEqual(oldTiles)) return false;
        var hitMine = after.MinePositions.Contains(newTiles[^1]);
        var safeCount = newTiles.Count(tile => !after.MinePositions.Contains(tile));
        return hitMine ? after.Status is MinesRoundStatus.Lost or MinesRoundStatus.Consolation :
            safeCount == 25 - after.MineCount ? after.Status == MinesRoundStatus.Cleared :
            after.Status == MinesRoundStatus.Active;
    }

    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];
}
