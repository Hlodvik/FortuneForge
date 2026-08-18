using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore : ICompetitiveSolitaireStore
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string AvailableFractionalCentsField = "availableFractionalCents";
    private const string QueueStatus = "queued";
    private const string MatchedStatus = "matched";
    private const string CancelledStatus = "cancelled";
    private const string PlayingMatchStatus = "playing";
    private const string SettledMatchStatus = "settled";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FirestoreDb database;
    private readonly CompetitiveSolitaireOptions options;

    public FirestoreCompetitiveSolitaireStore(
        FirestoreDb database,
        CompetitiveSolitaireOptions? options = null)
    {
        this.database = database;
        this.options = options ?? new CompetitiveSolitaireOptions();
    }

    private async Task<T> RunTransactionAsync<T>(
        Func<Transaction, Task<T>> callback,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await database.RunTransactionAsync(callback, cancellationToken: cancellationToken);
            }
            catch (RpcException exception) when (
                exception.StatusCode == StatusCode.Aborted && attempt < maximumAttempts)
            {
                var exponential = Math.Min(500, 20 * (1 << Math.Min(attempt - 1, 5)));
                var backoffMilliseconds = exponential + Random.Shared.Next(25, 126);
                await Task.Delay(backoffMilliseconds, cancellationToken);
            }
        }
    }

    internal static string CreateLookupKey(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest);
    }

    private DocumentReference SessionDocument(string userId) =>
        database.Collection("solitairePlayerSessions").Document(CreateLookupKey(userId));

    private DocumentReference TicketDocument(string ticketId) =>
        database.Collection("solitaireTickets").Document(ticketId);

    private DocumentReference PartitionDocument(string partitionKey) =>
        database.Collection("solitaireQueuePartitions").Document(partitionKey);

    private DocumentReference MatchDocument(string matchId) =>
        database.Collection("solitaireMatches").Document(matchId);

    private DocumentReference PlayerDocument(string matchId, string userId) =>
        database.Collection("solitaireMatchPlayers")
            .Document(CreateLookupKey($"{matchId}\n{userId}"));

    private DocumentReference ActionDocument(string userId, string idempotencyKey) =>
        database.Collection("solitaireCommandGuards")
            .Document(CreateLookupKey($"{userId}\n{idempotencyKey}"));

    private DocumentReference BalanceDocument(string userId) =>
        database.Collection("userBalances").Document($"{userId}_{SlotsCreditsCurrencyId}");

    private DocumentReference BalanceTransactionDocument(string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    private DocumentReference RevenueDocument(string matchId) =>
        database.Collection("solitaireMatchRevenue").Document(matchId);

    private DocumentReference TestTraceDocument(string matchId) =>
        database.Collection("solitaireTestMatchTrace").Document(matchId);

    // Shared history can aggregate this game-owned collection and invoke the
    // Solitaire claim endpoint with matchId. IDs are deterministic per run.
    private DocumentReference CardGameResultDocument(string matchId, string userId) =>
        database.Collection("cardGameResults")
            .Document(CreateLookupKey($"solitaire\n{matchId}\n{userId}"));

    private static string PartitionKey(int playerCount, long buyInCents, int drawCount) =>
        $"players-{playerCount}-buyin-{buyInCents}-draw-{drawCount}";

    private static Dictionary<string, object> SessionData(
        string userId,
        string kind,
        string? ticketId,
        string? matchId,
        DateTime nowUtc) => new()
    {
        ["userId"] = userId,
        ["kind"] = kind,
        ["ticketId"] = ticketId ?? string.Empty,
        ["matchId"] = matchId ?? string.Empty,
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> TicketData(SolitaireTicket ticket) => new()
    {
        ["ticketId"] = ticket.TicketId,
        ["userId"] = ticket.UserId,
        ["displayName"] = ticket.DisplayName,
        ["playerCount"] = ticket.PlayerCount,
        ["buyInCents"] = ticket.BuyInCents,
        ["partitionKey"] = ticket.PartitionKey,
        ["status"] = ticket.Status,
        ["joinedAt"] = Timestamp.FromDateTime(ticket.JoinedAtUtc),
        ["matchId"] = ticket.MatchId ?? string.Empty,
        ["drawCount"] = ticket.DrawCount,
        ["schemaVersion"] = 2L
    };

    private static Dictionary<string, object> MatchData(SolitaireMatch match) => new()
    {
        ["matchId"] = match.MatchId,
        ["playerCount"] = match.PlayerCount,
        ["buyInCents"] = match.BuyInCents,
        ["prizePoolCents"] = match.PrizePoolCents,
        ["winnerPayoutCents"] = match.WinnerPayoutCents,
        ["platformFeeCents"] = match.PlatformFeeCents,
        ["dealSeed"] = (long)match.DealSeed,
        ["startedAt"] = Timestamp.FromDateTime(match.StartedAtUtc),
        ["deadlineAt"] = Timestamp.FromDateTime(match.DeadlineAtUtc),
        ["status"] = match.Status,
        ["playerIds"] = match.PlayerIds.ToArray(),
        ["displayNames"] = match.DisplayNames.ToArray(),
        ["ticketIds"] = match.TicketIds.ToArray(),
        ["joinedAt"] = match.JoinedAtUtc.Select(Timestamp.FromDateTime).ToArray(),
        ["completedAt"] = match.CompletedAtUtc is null
            ? string.Empty
            : Timestamp.FromDateTime(match.CompletedAtUtc.Value),
        ["winnerUserId"] = match.WinnerUserId ?? string.Empty,
        ["partitionKey"] = match.PartitionKey,
        ["botFillEligibleAt"] = match.BotFillEligibleAtUtc is null
            ? string.Empty
            : Timestamp.FromDateTime(match.BotFillEligibleAtUtc.Value),
        ["botsFilled"] = match.BotsFilled,
        ["drawCount"] = match.DrawCount,
        ["schemaVersion"] = 3L
    };

    private static Dictionary<string, object> PlayerData(SolitairePlayerState player) => new()
    {
        ["matchId"] = player.MatchId,
        ["userId"] = player.UserId,
        ["displayName"] = player.DisplayName,
        ["seat"] = player.Seat,
        ["status"] = player.Status,
        ["gameStateJson"] = JsonSerializer.Serialize(player.Game, JsonOptions),
        ["undoStateJson"] = JsonSerializer.Serialize(player.UndoHistory, JsonOptions),
        ["integrityWarningsJson"] = JsonSerializer.Serialize(player.IntegrityWarnings, JsonOptions),
        ["version"] = player.Version,
        ["elapsedMilliseconds"] = player.ElapsedMilliseconds ?? -1L,
        ["completedAt"] = player.CompletedAtUtc is null
            ? string.Empty
            : Timestamp.FromDateTime(player.CompletedAtUtc.Value),
        ["payoutCents"] = player.PayoutCents,
        ["acknowledged"] = player.Acknowledged,
        ["startedAt"] = Timestamp.FromDateTime(player.StartedAtUtc),
        ["deadlineAt"] = Timestamp.FromDateTime(player.DeadlineAtUtc),
        ["isSynthetic"] = player.IsSynthetic,
        ["syntheticSkill"] = player.SyntheticSkill ?? 0,
        ["pauseUsedMilliseconds"] = player.PauseUsedMilliseconds,
        ["pausedAt"] = player.PausedAtUtc is null
            ? string.Empty
            : Timestamp.FromDateTime(player.PausedAtUtc.Value),
        ["schemaVersion"] = 5L
    };

    private static Dictionary<string, object> ActionData(
        string userId,
        string operation,
        string targetId,
        string detail,
        DateTime nowUtc) => new()
    {
        ["userId"] = userId,
        ["operation"] = operation,
        ["targetId"] = targetId,
        ["detail"] = detail,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> BalanceUpdate(long cents, DateTime nowUtc) => new()
    {
        ["available"] = cents / SolitaireMoney.CentsPerCredit,
        [AvailableFractionalCentsField] = cents % SolitaireMoney.CentsPerCredit,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId,
        string userId,
        long amountCents,
        long balanceAfterCents,
        string type,
        string idempotencyKey,
        DateTime nowUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["amount"] = (double)SolitaireMoney.ToCredits(amountCents),
        ["balanceAfter"] = (double)SolitaireMoney.ToCredits(balanceAfterCents),
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> UnclaimedResultData(
        SolitaireMatch match,
        SolitairePlayerState player,
        DateTime completedAtUtc) => new()
    {
        ["resultId"] = CreateLookupKey($"solitaire\n{match.MatchId}\n{player.UserId}"),
        ["game"] = "solitaire",
        ["mode"] = "competitive",
        ["matchId"] = match.MatchId,
        ["userId"] = player.UserId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["claimStatus"] = SolitaireClaimStatuses.Unclaimed,
        ["settlementStatus"] = "pending",
        ["playerStatus"] = player.Status,
        ["score"] = (long)player.Game.Score,
        ["moves"] = (long)player.Game.Moves,
        ["elapsedMilliseconds"] = player.ElapsedMilliseconds ?? 0L,
        ["buyInCents"] = match.BuyInCents,
        ["payoutCents"] = 0L,
        ["completedAt"] = Timestamp.FromDateTime(completedAtUtc),
        ["schemaVersion"] = 1L
    };

    private static Dictionary<string, object> ClaimableResultData(
        SolitaireMatch match,
        SolitairePlayerState player,
        long payoutCents,
        DateTime completedAtUtc)
    {
        var data = UnclaimedResultData(match, player, player.CompletedAtUtc ?? completedAtUtc);
        data["settlementStatus"] = "claimable";
        data["payoutCents"] = payoutCents;
        data["claimableAt"] = Timestamp.FromDateTime(completedAtUtc);
        return data;
    }

    private static SolitaireTicket ReadTicket(DocumentSnapshot snapshot) => new(
        ReadString(snapshot, "ticketId"),
        ReadString(snapshot, "userId"),
        ReadString(snapshot, "displayName"),
        checked((int)ReadLong(snapshot, "playerCount")),
        ReadLong(snapshot, "buyInCents"),
        ReadString(snapshot, "partitionKey"),
        ReadString(snapshot, "status"),
        ReadTimestamp(snapshot, "joinedAt"),
        EmptyToNull(ReadString(snapshot, "matchId")))
    {
        DrawCount = checked((int)ReadLong(snapshot, "drawCount", 3))
    };

    private static SolitaireMatch ReadMatch(DocumentSnapshot snapshot)
    {
        var completed = ReadOptionalTimestamp(snapshot, "completedAt");
        return new SolitaireMatch(
            ReadString(snapshot, "matchId"),
            checked((int)ReadLong(snapshot, "playerCount")),
            ReadLong(snapshot, "buyInCents"),
            ReadLong(snapshot, "prizePoolCents"),
            ReadLong(snapshot, "winnerPayoutCents"),
            ReadLong(snapshot, "platformFeeCents"),
            checked((uint)ReadLong(snapshot, "dealSeed")),
            ReadTimestamp(snapshot, "startedAt"),
            ReadTimestamp(snapshot, "deadlineAt"),
            ReadString(snapshot, "status"),
            ReadStringArray(snapshot, "playerIds"),
            ReadStringArray(snapshot, "displayNames"),
            ReadStringArray(snapshot, "ticketIds"),
            ReadTimestampArray(snapshot, "joinedAt"),
            completed,
            EmptyToNull(ReadString(snapshot, "winnerUserId")))
        {
            PartitionKey = ReadString(snapshot, "partitionKey"),
            BotFillEligibleAtUtc = ReadOptionalTimestamp(snapshot, "botFillEligibleAt"),
            BotsFilled = ReadBool(snapshot, "botsFilled"),
            DrawCount = checked((int)ReadLong(snapshot, "drawCount", 3))
        };
    }

    private static SolitairePlayerState ReadPlayer(DocumentSnapshot snapshot)
    {
        var gameJson = ReadString(snapshot, "gameStateJson");
        var game = JsonSerializer.Deserialize<SolitaireGameState>(gameJson, JsonOptions)
            ?? throw new InvalidOperationException("A stored Solitaire game state is invalid.");
        var undoJson = ReadString(snapshot, "undoStateJson");
        var undoHistory = string.IsNullOrWhiteSpace(undoJson)
            ? Array.Empty<SolitaireGameState>()
            : JsonSerializer.Deserialize<SolitaireGameState[]>(undoJson, JsonOptions)
                ?? Array.Empty<SolitaireGameState>();
        var warningJson = ReadString(snapshot, "integrityWarningsJson");
        var integrityWarnings = string.IsNullOrWhiteSpace(warningJson)
            ? Array.Empty<SolitaireIntegrityWarning>()
            : JsonSerializer.Deserialize<SolitaireIntegrityWarning[]>(warningJson, JsonOptions)
                ?? Array.Empty<SolitaireIntegrityWarning>();
        var elapsed = ReadLong(snapshot, "elapsedMilliseconds", -1);
        var syntheticSkill = ReadLong(snapshot, "syntheticSkill");
        var startedAt = ReadOptionalTimestamp(snapshot, "startedAt") ?? DateTime.UnixEpoch;
        var deadlineAt = ReadOptionalTimestamp(snapshot, "deadlineAt") ?? DateTime.UnixEpoch;
        return new SolitairePlayerState(
            ReadString(snapshot, "matchId"),
            ReadString(snapshot, "userId"),
            ReadString(snapshot, "displayName"),
            checked((int)ReadLong(snapshot, "seat")),
            ReadString(snapshot, "status"),
            game,
            checked((int)ReadLong(snapshot, "version")),
            elapsed < 0 ? null : elapsed,
            ReadOptionalTimestamp(snapshot, "completedAt"),
            ReadLong(snapshot, "payoutCents"),
            ReadBool(snapshot, "acknowledged"))
        {
            StartedAtUtc = startedAt,
            DeadlineAtUtc = deadlineAt,
            IsSynthetic = ReadBool(snapshot, "isSynthetic"),
            SyntheticSkill = syntheticSkill > 0
                ? checked((int)syntheticSkill)
                : null,
            PauseUsedMilliseconds = ReadLong(snapshot, "pauseUsedMilliseconds"),
            PausedAtUtc = ReadOptionalTimestamp(snapshot, "pausedAt"),
            UndoHistory = undoHistory,
            IntegrityWarnings = integrityWarnings
        };
    }

    private static long ReadBalanceCents(DocumentSnapshot snapshot) => checked(
        ReadLong(snapshot, "available") * SolitaireMoney.CentsPerCredit +
        Math.Clamp(ReadLong(snapshot, AvailableFractionalCentsField), 0, 99));

    private static long ReadLong(DocumentSnapshot snapshot, string field, long fallback = 0) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : fallback;

    private static bool ReadBool(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<bool>(field, out var value) && value;

    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value)
            ? value
            : string.Empty;

    private static IReadOnlyList<string> ReadStringArray(DocumentSnapshot snapshot, string field)
    {
        var values = snapshot.ToDictionary();
        if (!values.TryGetValue(field, out var raw) || raw is not IEnumerable<object> items)
        {
            return [];
        }
        return items.Select(item => item as string ?? string.Empty).ToArray();
    }

    private static IReadOnlyList<DateTime> ReadTimestampArray(DocumentSnapshot snapshot, string field)
    {
        var values = snapshot.ToDictionary();
        if (!values.TryGetValue(field, out var raw) || raw is not IEnumerable<object> items)
        {
            return [];
        }
        return items.Select(item => item is Timestamp timestamp
            ? timestamp.ToDateTime()
            : DateTime.UnixEpoch).ToArray();
    }

    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.UnixEpoch;

    private static DateTime? ReadOptionalTimestamp(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.Exists) return null;
        return snapshot.ToDictionary().TryGetValue(field, out var raw) && raw is Timestamp value
            ? value.ToDateTime()
            : null;
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static void VerifyAction(
        DocumentSnapshot actionSnapshot,
        string operation,
        string targetId,
        string detail)
    {
        if (!string.Equals(ReadString(actionSnapshot, "operation"), operation, StringComparison.Ordinal) ||
            !string.Equals(ReadString(actionSnapshot, "targetId"), targetId, StringComparison.Ordinal) ||
            !string.Equals(ReadString(actionSnapshot, "detail"), detail, StringComparison.Ordinal))
        {
            throw new SolitaireConflictException(
                "This idempotency key was already used for a different Solitaire request.");
        }
    }
}
