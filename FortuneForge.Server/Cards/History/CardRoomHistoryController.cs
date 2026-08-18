using System.Text.RegularExpressions;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.History;

public sealed record CardRoomHistoryItemResponse(
    string ResultId,
    string Game,
    string Mode,
    string MatchId,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool Unseen,
    bool RequiresClaim,
    decimal WinningsCredits,
    int? Score,
    int? Moves,
    int SchemaVersion,
    decimal WagerCredits = 0,
    decimal NetCredits = 0);

[ApiController]
[Route("api/cards/history")]
public sealed partial class CardRoomHistoryController(
    FirestoreDb database,
    AccountService accountService) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult<IReadOnlyList<CardRoomHistoryItemResponse>>> Get(
        [FromQuery] int limit = 40,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
            return BadRequest(new { error = "Choose a history limit from 1 to 100." });

        var account = await AccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to read card-game history." });

        var snapshots = await database.Collection("cardGameResults")
            .WhereEqualTo("userId", account.UserId)
            .Limit(100)
            .GetSnapshotAsync(cancellationToken);

        return Ok(snapshots.Documents
            .Select(ToPublicItem)
            .Where(item => item is not null)
            .Cast<CardRoomHistoryItemResponse>()
            .OrderByDescending(item => item.CompletedAtUtc ?? item.StartedAtUtc)
            .ThenBy(item => item.ResultId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray());
    }

    [HttpPost("{resultId}/seen")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> MarkSeen(
        string resultId,
        CancellationToken cancellationToken)
    {
        if (!ResultIdPattern().IsMatch(resultId))
            return BadRequest(new { error = "The history result identifier is invalid." });

        var account = await AccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to update card-game history." });

        var reference = database.Collection("cardGameResults").Document(resultId);
        var found = await database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (!snapshot.Exists || !String(snapshot, "userId").Equals(account.UserId, StringComparison.Ordinal))
                return false;

            if (!snapshot.ContainsField("seenAt") || snapshot.GetValue<object>("seenAt") is not Timestamp)
                transaction.Update(reference, "seenAt", Timestamp.GetCurrentTimestamp());
            return true;
        }, cancellationToken: cancellationToken);

        return found ? NoContent() : NotFound(new { error = "The history result was not found." });
    }

    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(
            AccountSessionCookie.Read(Request),
            cancellationToken)).Value;

    private static CardRoomHistoryItemResponse? ToPublicItem(DocumentSnapshot snapshot)
    {
        var game = String(snapshot, "game");
        if (game is not ("blackjack" or "texas-holdem" or "solitaire")) return null;

        var claimStatus = String(snapshot, "claimStatus");
        var settlementStatus = String(snapshot, "settlementStatus");
        var requiresClaim = game == "solitaire"
            && claimStatus == "unclaimed"
            && settlementStatus == "claimable";
        var completedAt = TimestampUtc(snapshot, "completedAt")
            ?? TimestampUtc(snapshot, "settledAt")
            ?? TimestampUtc(snapshot, "recognizedAt");
        var startedAt = TimestampUtc(snapshot, "startedAt")
            ?? completedAt
            ?? DateTime.UnixEpoch;
        var unseen = requiresClaim
            || (game == "solitaire"
                ? claimStatus != "completed" && !HasTimestamp(snapshot, "seenAt")
                : !HasTimestamp(snapshot, "seenAt"));

        var payoutCents = Money(snapshot, "payoutCents");
        var wagerCents = Money(snapshot, "wagerCents");
        if (wagerCents == 0) wagerCents = Money(snapshot, "buyInCents");
        var netCents = snapshot.ContainsField("netCents") ? Money(snapshot, "netCents") : payoutCents - wagerCents;
        return new CardRoomHistoryItemResponse(
            snapshot.Id,
            game,
            String(snapshot, "mode") is { Length: > 0 } mode ? mode : "credit",
            String(snapshot, "matchId") is { Length: > 0 } matchId
                ? matchId
                : String(snapshot, "tableId"),
            startedAt,
            completedAt,
            unseen,
            requiresClaim,
            payoutCents / 100m,
            Int(snapshot, "score"),
            Int(snapshot, "moves"),
            Int(snapshot, "schemaVersion") ?? 1,
            wagerCents / 100m,
            netCents / 100m);
    }

    private static string String(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists
        && snapshot.ContainsField(field)
        && snapshot.GetValue<object>(field) is string value
            ? value
            : string.Empty;

    private static long Money(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.ContainsField(field)
            ? snapshot.GetValue<object>(field) switch
            {
                long value => value,
                int value => value,
                _ => 0
            }
            : 0;

    private static int? Int(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.ContainsField(field)
            ? snapshot.GetValue<object>(field) switch
            {
                long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
                int value => value,
                _ => null
            }
            : null;

    private static DateTime? TimestampUtc(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists
        && snapshot.ContainsField(field)
        && snapshot.GetValue<object>(field) is Timestamp value
            ? value.ToDateTime().ToUniversalTime()
            : null;

    private static bool HasTimestamp(DocumentSnapshot snapshot, string field) =>
        TimestampUtc(snapshot, field) is not null;

    [GeneratedRegex("^[A-Za-z0-9_-]{16,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResultIdPattern();
}
