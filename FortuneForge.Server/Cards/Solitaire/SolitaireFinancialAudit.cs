using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Solitaire;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

/// <summary>Reconciles the immutable paid match receipt with its mutable source and wallet writes.</summary>
internal static class SolitaireFinancialAudit
{
    private const string HouseFundedModel = "house-funded-bot-pool-v1";
    private const string Currency = "slotsCredits";

    internal static bool Supports(RoundAuditSummary summary) =>
        summary.GameId == "solitaire" && summary.AuditorVersion == CompetitiveSolitaireRoundAudit.Version;

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(
        FirestoreDb database, RoundAuditSummary summary, string? payloadJson, CancellationToken ct)
    {
        if (!Supports(summary)) return [];
        if (string.IsNullOrWhiteSpace(payloadJson))
            return [Error("solitaire-financial-evidence-missing", "The paid Solitaire settlement receipt is unavailable for financial reconciliation.")];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var match = root.GetProperty("match").Deserialize<SolitaireMatch>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing Solitaire match.");
            var players = root.GetProperty("players").Deserialize<SolitairePlayerState[]>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing Solitaire players.");
            if (summary.EvidenceKind != "paid-solitaire-match-v1" ||
                match.MatchId != summary.RoundId || match.Status != "settled" ||
                match.CompletedAtUtc != summary.CompletedAtUtc || match.StartedAtUtc != summary.StartedAtUtc ||
                match.BuyInCents <= 0 || players.Length == 0 || players.Length != match.PlayerIds.Count ||
                match.TicketIds.Count != players.Length || match.JoinedAtUtc.Count != players.Length ||
                match.PlayerIds.Distinct(StringComparer.Ordinal).Count() != players.Length ||
                !match.PlayerIds.SequenceEqual(players.Select(player => player.UserId)) ||
                root.GetProperty("sourceCollection").GetString() != "solitaireMatches" ||
                root.GetProperty("sourceDocumentId").GetString() != match.MatchId ||
                root.GetProperty("financialClassification").GetString() != match.FinancialModel ||
                (root.GetProperty("revenueDocumentId").ValueKind == JsonValueKind.String
                    ? root.GetProperty("revenueDocumentId").GetString()
                    : null) != (match.FinancialModel == HouseFundedModel ? match.MatchId : null) ||
                summary.Participants.Count != players.Length)
                return [Error("solitaire-financial-receipt-mismatch", "The saved Solitaire settlement identities or source link differ from the audit summary.")];

            var findings = new List<RoundAuditFinding>();
            var humanCount = players.Count(player => player.IsAccountBacked);
            var managedCount = players.Length - humanCount;
            var humanEntry = checked(humanCount * match.BuyInCents);
            var houseEntry = match.FinancialModel == HouseFundedModel
                ? checked(managedCount * match.BuyInCents) : 0L;
            var humanPayout = players.Where(player => player.IsAccountBacked).Sum(player => player.PayoutCents);
            var botPrize = players.Where(player => !player.IsAccountBacked).Sum(player => player.PayoutCents);
            if (match.FinancialModel == HouseFundedModel && match.HumanEntryCents != humanEntry ||
                match.FinancialModel == HouseFundedModel && match.HouseFundedEntryCents != houseEntry ||
                match.PrizePoolCents != checked(humanEntry + houseEntry) ||
                match.WinnerPayoutCents < 0 || match.PlatformFeeCents < 0 ||
                match.PrizePoolCents != checked(match.WinnerPayoutCents + match.PlatformFeeCents) ||
                humanPayout + botPrize != match.WinnerPayoutCents ||
                players.Count(player => player.PayoutCents > 0) != 1 ||
                players.Any(player => player.MatchId != match.MatchId || player.PayoutCents < 0 ||
                    player.PayoutCents != (player.UserId == match.WinnerUserId ? match.WinnerPayoutCents : 0)) ||
                summary.Participants.Where((participant, index) =>
                    participant.ActorId != players[index].UserId ||
                    participant.IsAccountBacked != players[index].IsAccountBacked ||
                    participant.WagerCents != (players[index].IsAccountBacked ? match.BuyInCents : 0) ||
                    participant.PayoutCents != players[index].PayoutCents ||
                    participant.NetCents != players[index].PayoutCents - participant.WagerCents).Any())
                findings.Add(Error("solitaire-financial-receipt-mismatch", "The saved Solitaire seats, account entries, pool or awards do not reconcile."));

            var sourceRef = database.Collection("solitaireMatches").Document(match.MatchId);
            var fundingRef = database.Collection("solitaireHouseFunding").Document(match.MatchId);
            var revenueRef = database.Collection("solitaireMatchRevenue").Document(match.MatchId);
            var sourceReads = await Task.WhenAll(sourceRef.GetSnapshotAsync(ct), fundingRef.GetSnapshotAsync(ct),
                revenueRef.GetSnapshotAsync(ct));
            var source = sourceReads[0]; var funding = sourceReads[1]; var revenue = sourceReads[2];
            if (!source.Exists || Text(source, "matchId") != match.MatchId || Text(source, "status") != "settled" ||
                Text(source, "financialModel") != match.FinancialModel ||
                Text(source, "winnerUserId") != match.WinnerUserId ||
                Long(source, "buyInCents") != match.BuyInCents ||
                Long(source, "prizePoolCents") != match.PrizePoolCents ||
                Long(source, "winnerPayoutCents") != match.WinnerPayoutCents ||
                Long(source, "platformFeeCents") != match.PlatformFeeCents ||
                Long(source, "humanEntryCents") != match.HumanEntryCents ||
                Long(source, "houseFundedEntryCents") != match.HouseFundedEntryCents ||
                Time(source, "completedAt") != match.CompletedAtUtc ||
                !Strings(source, "playerIds").SequenceEqual(match.PlayerIds) ||
                !Strings(source, "ticketIds").SequenceEqual(match.TicketIds))
                findings.Add(Error("solitaire-financial-source-mismatch", "The current Solitaire match differs from its saved final settlement."));
            if (match.FinancialModel == HouseFundedModel &&
                (Long(source, "humanPayoutCents") != humanPayout ||
                 Long(source, "botPrizeCents") != botPrize ||
                 Long(source, "houseNetCents") != checked(humanEntry - humanPayout)))
                findings.Add(Error("solitaire-financial-source-mismatch", "The current Solitaire match's recognized payout or house net differs from the final settlement."));

            if (match.FinancialModel == HouseFundedModel)
            {
                if (!funding.Exists || Text(funding, "matchId") != match.MatchId ||
                    Text(funding, "currencyId") != Currency ||
                    Text(funding, "financialClassification") != HouseFundedModel ||
                    Long(funding, "humanEntryCents") != humanEntry ||
                    Long(funding, "houseFundedEntryCents") != houseEntry ||
                    Long(funding, "humanPlayerCount") != humanCount ||
                    Long(funding, "managedPlayerCount") != managedCount)
                    findings.Add(Error("solitaire-financial-funding-mismatch", "The house funding record does not match the saved account and managed seats."));
                if (!revenue.Exists || Text(revenue, "matchId") != match.MatchId ||
                    Text(revenue, "currencyId") != Currency ||
                    Text(revenue, "financialClassification") != HouseFundedModel ||
                    Text(revenue, "winnerUserId") != match.WinnerUserId ||
                    Long(revenue, "humanEntryCents") != humanEntry ||
                    Long(revenue, "houseFundedEntryCents") != houseEntry ||
                    Long(revenue, "grossPoolCents") != match.PrizePoolCents ||
                    Long(revenue, "winnerPayoutCents") != match.WinnerPayoutCents ||
                    Long(revenue, "humanPayoutCents") != humanPayout ||
                    Long(revenue, "botPrizeCents") != botPrize ||
                    Long(revenue, "platformFeeCents") != match.PlatformFeeCents ||
                    Long(revenue, "houseNetCents") != checked(humanEntry - humanPayout) ||
                    Long(revenue, "botFinancialContributionCents") != 0 ||
                    Long(revenue, "humanPlayerCount") != humanCount ||
                    Long(revenue, "managedPlayerCount") != managedCount ||
                    Time(revenue, "recognizedAt") != match.CompletedAtUtc)
                    findings.Add(Error("solitaire-financial-revenue-mismatch", "The Solitaire revenue record does not reconcile to entries, awards and house net."));
            }
            else if (humanCount > 1)
            {
                if (!revenue.Exists || Text(revenue, "matchId") != match.MatchId ||
                    Text(revenue, "currencyId") != Currency ||
                    Text(revenue, "financialClassification") != "real-human-pool-v1" ||
                    Text(revenue, "winnerUserId") != match.WinnerUserId ||
                    Long(revenue, "grossPoolCents") != match.PrizePoolCents ||
                    Long(revenue, "winnerPayoutCents") != match.WinnerPayoutCents ||
                    Long(revenue, "platformFeeCents") != match.PlatformFeeCents ||
                    Long(revenue, "humanPlayerCount") != humanCount ||
                    Time(revenue, "recognizedAt") != match.CompletedAtUtc)
                    findings.Add(Error("solitaire-financial-revenue-mismatch", "The legacy Solitaire revenue record differs from the saved human pool."));
            }
            else if (humanCount == 1)
            {
                var trace = await database.Collection("solitaireTestMatchTrace")
                    .Document(match.MatchId).GetSnapshotAsync(ct);
                if (!trace.Exists || Text(trace, "matchId") != match.MatchId ||
                    Text(trace, "classification") != "single-human-bot-fill-test" ||
                    Time(trace, "completedAt") != match.CompletedAtUtc)
                    findings.Add(Error("solitaire-financial-legacy-trace-mismatch", "The legacy single-human refund classification has no matching test trace."));
            }

            var playerRefs = players.Select(player => database.Collection("solitaireMatchPlayers")
                .Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"{match.MatchId}\n{player.UserId}"))).ToArray();
            var playerReads = await Task.WhenAll(playerRefs.Select(reference => reference.GetSnapshotAsync(ct)));
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index]; var current = playerReads[index];
                if (!current.Exists || Text(current, "matchId") != match.MatchId ||
                    Text(current, "userId") != player.UserId ||
                    Long(current, "seat") != player.Seat ||
                    Long(current, "payoutCents") != player.PayoutCents ||
                    !current.TryGetValue<bool>("isSynthetic", out var synthetic) ||
                    synthetic == player.IsAccountBacked)
                    findings.Add(Error("solitaire-financial-player-mismatch", $"The stored Solitaire seat for {player.UserId} differs from the final receipt."));
            }

            var ticketIds = match.TicketIds.Where(id => !string.IsNullOrEmpty(id)).ToArray();
            if (ticketIds.Length != humanCount || ticketIds.Distinct(StringComparer.Ordinal).Count() != ticketIds.Length ||
                players.Where((player, index) => player.IsAccountBacked != !string.IsNullOrEmpty(match.TicketIds[index])).Any())
                findings.Add(Error("solitaire-financial-ticket-link-mismatch", "The match ticket IDs do not map one to one to its account-backed seats."));
            var ticketRefs = ticketIds.Select(id => database.Collection("solitaireTickets").Document(id)).ToArray();
            var ticketReads = await Task.WhenAll(ticketRefs.Select(reference => reference.GetSnapshotAsync(ct)));
            for (var index = 0; index < ticketIds.Length; index++)
            {
                var ticketId = ticketIds[index]; var ticket = ticketReads[index];
                var seat = Array.FindIndex(match.TicketIds.ToArray(), id => id == ticketId);
                var userId = seat >= 0 ? players[seat].UserId : "";
                if (!ticket.Exists || Text(ticket, "ticketId") != ticketId ||
                    Text(ticket, "matchId") != match.MatchId || Text(ticket, "userId") != userId ||
                    Text(ticket, "status") != "matched" || Long(ticket, "buyInCents") != match.BuyInCents ||
                    Time(ticket, "joinedAt") != match.JoinedAtUtc[seat])
                    findings.Add(Error("solitaire-financial-ticket-mismatch", $"The paid ticket {ticketId} is absent or differs from the saved match seat."));
                var buyinId = $"{ticketId}-buyin";
                var buyin = await database.Collection("balanceTransactions").Document(buyinId).GetSnapshotAsync(ct);
                if (!LedgerMatches(buyin, buyinId, userId, -match.BuyInCents, "solitaire-buyin") ||
                    Time(buyin, "createdAt") != match.JoinedAtUtc[seat])
                    findings.Add(Error("solitaire-financial-buyin-ledger-mismatch", $"The account-backed buy-in ledger for ticket {ticketId} is absent or differs from its stake."));
                var refund = await database.Collection("balanceTransactions").Document($"{ticketId}-refund").GetSnapshotAsync(ct);
                if (refund.Exists)
                    findings.Add(Error("solitaire-financial-unexpected-refund", $"Matched ticket {ticketId} has a queue-cancellation refund ledger."));
            }

            var linkedTickets = await database.Collection("solitaireTickets").WhereEqualTo("matchId", match.MatchId)
                .Limit(101).GetSnapshotAsync(ct);
            if (linkedTickets.Count >= 101 || linkedTickets.Documents.Any(ticket => !ticketIds.Contains(ticket.Id, StringComparer.Ordinal)))
                findings.Add(Error("solitaire-financial-ticket-set-mismatch", "The tickets linked to this match differ from the saved final ticket IDs."));

            var expectedClaims = new HashSet<string>(StringComparer.Ordinal);
            foreach (var player in players)
            {
                var resultId = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{match.MatchId}\n{player.UserId}");
                var result = await database.Collection("cardGameResults").Document(resultId).GetSnapshotAsync(ct);
                var claimId = $"{match.MatchId}-claim-{FirestoreCompetitiveSolitaireStore.CreateLookupKey(player.UserId)}";
                var claim = await database.Collection("balanceTransactions").Document(claimId).GetSnapshotAsync(ct);
                if (!result.Exists || Text(result, "resultId") != resultId ||
                    Text(result, "game") != "solitaire" || Text(result, "matchId") != match.MatchId ||
                    Text(result, "userId") != player.UserId || Text(result, "currencyId") != Currency ||
                    Long(result, "payoutCents") != player.PayoutCents ||
                    Long(result, "buyInCents") != (player.IsAccountBacked ? match.BuyInCents : 0) ||
                    Text(result, "settlementStatus") != (player.IsAccountBacked ? "claimable" : "recorded"))
                {
                    findings.Add(Error("solitaire-financial-result-mismatch", $"The shared result for {player.UserId} differs from the final entitlement."));
                    continue;
                }
                if (!player.IsAccountBacked)
                {
                    if (claim.Exists) findings.Add(Error("solitaire-financial-managed-claim", "A managed seat has a wallet claim ledger."));
                    continue;
                }
                var status = Text(result, "claimStatus");
                if (status == SolitaireClaimStatuses.Unclaimed)
                {
                    if (claim.Exists)
                        findings.Add(Error("solitaire-financial-unclaimed-ledger", $"An unclaimed entitlement for {player.UserId} has a wallet credit ledger."));
                    else if (player.PayoutCents > 0)
                        findings.Add(new RoundAuditFinding("solitaire-claim-pending", "warning",
                            $"The {player.UserId} award remains claimable; no wallet credit is due until a claim commits."));
                }
                else if (status == SolitaireClaimStatuses.Completed)
                {
                    var claimedAt = Time(result, "claimedAt");
                    if (claimedAt is null || claimedAt < match.CompletedAtUtc ||
                        string.IsNullOrWhiteSpace(Text(result, "claimIdempotencyKey")))
                        findings.Add(Error("solitaire-financial-claim-state-mismatch", $"The claim state for {player.UserId} lacks a valid claim time or command key."));
                    if (player.PayoutCents > 0)
                    {
                        expectedClaims.Add(claimId);
                        var type = match.AdmissionClosesAtUtc is not null && match.PlayerIds.Count == 1
                            ? "solitaire-no-contest-refund-claim"
                            : match.PrizePoolCents == match.BuyInCents
                                ? "solitaire-test-refund-claim" : "solitaire-winner-payout-claim";
                        if (!LedgerMatches(claim, claimId, player.UserId, player.PayoutCents, type) ||
                            Time(claim, "createdAt") != claimedAt ||
                            Text(claim, "idempotencyKey") != Text(result, "claimIdempotencyKey"))
                            findings.Add(Error("solitaire-financial-claim-ledger-mismatch", $"The claimed award for {player.UserId} has no matching wallet credit."));
                    }
                    else if (claim.Exists)
                        findings.Add(Error("solitaire-financial-zero-claim-ledger", $"Zero-award claim {player.UserId} unexpectedly credited a wallet."));
                }
                else
                    findings.Add(Error("solitaire-financial-claim-state-mismatch", $"The claim state for {player.UserId} is invalid."));
            }
            var claimPrefix = $"{match.MatchId}-claim-";
            var linkedClaims = await database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
                .StartAt(claimPrefix).EndAt(claimPrefix + "\uf8ff").Limit(101).GetSnapshotAsync(ct);
            if (linkedClaims.Count >= 101 || linkedClaims.Documents.Any(ledger => !expectedClaims.Contains(ledger.Id)))
                findings.Add(Error("solitaire-financial-claim-set-mismatch", "The match has an extra or unbounded wallet claim ledger."));
            return findings;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            return [Error("solitaire-financial-evidence-invalid", "The paid Solitaire financial receipt cannot be parsed for independent reconciliation.")];
        }
    }

    private static bool LedgerMatches(DocumentSnapshot ledger, string id, string userId, long cents, string type) =>
        ledger.Exists && ledger.Id == id && Text(ledger, "transactionId") == id &&
        Text(ledger, "userId") == userId && Text(ledger, "currencyId") == Currency &&
        Text(ledger, "type") == type && Money(ledger, "amount") == cents &&
        Time(ledger, "createdAt") is not null && !string.IsNullOrWhiteSpace(Text(ledger, "idempotencyKey"));

    private static long Money(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<double>(field, out var value) ||
            !double.IsFinite(value) || Math.Abs(value * 100 - Math.Round(value * 100)) > 0.000001)
            return long.MinValue;
        return checked((long)Math.Round(value * 100));
    }

    private static string Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : "";

    private static long Long(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : long.MinValue;

    private static DateTime? Time(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value) ? value.ToDateTime() : null;

    private static IReadOnlyList<string> Strings(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<List<string>>(field, out var value) ? value : [];

    private static RoundAuditFinding Error(string code, string message) => new(code, "error", message);
}
