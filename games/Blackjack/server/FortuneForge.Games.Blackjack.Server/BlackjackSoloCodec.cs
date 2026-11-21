using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack;

internal static class BlackjackSoloCodec
{
    internal static Dictionary<string, object> Create(
        BlackjackGame game,
        string idempotencyKey)
    {
        var data = Update(game);
        data["gameId"] = game.GameId;
        data["userId"] = game.UserId;
        data["wagerCents"] = game.WagerCents;
        data["deck"] = game.Deck.ToArray();
        data["startIdempotencyKey"] = idempotencyKey;
        data["createdAt"] = Timestamp.FromDateTime(game.CreatedAtUtc);
        data["schemaVersion"] = 1L;
        return data;
    }

    internal static Dictionary<string, object> Update(BlackjackGame game) => new()
    {
        ["totalWagerCents"] = game.TotalWagerCents,
        ["payoutCents"] = game.PayoutCents,
        ["status"] = game.Status,
        ["outcome"] = game.Outcome ?? string.Empty,
        ["nextCardIndex"] = game.NextCardIndex,
        ["playerCards"] = game.PlayerCards.ToArray(),
        ["dealerCards"] = game.DealerCards.ToArray(),
        ["version"] = game.Version,
        ["updatedAt"] = Timestamp.FromDateTime(game.UpdatedAtUtc)
    };

    internal static BlackjackGame Read(DocumentSnapshot snapshot)
    {
        var game = new BlackjackGame(
        ReadString(snapshot, "gameId"),
        ReadString(snapshot, "userId"),
        ReadLong(snapshot, "wagerCents"),
        ReadLong(snapshot, "totalWagerCents"),
        ReadLong(snapshot, "payoutCents"),
        ReadString(snapshot, "status"),
        EmptyToNull(ReadString(snapshot, "outcome")),
        ReadStringArray(snapshot, "deck"),
        checked((int)ReadLong(snapshot, "nextCardIndex")),
        ReadStringArray(snapshot, "playerCards"),
        ReadStringArray(snapshot, "dealerCards"),
        checked((int)ReadLong(snapshot, "version")),
        ReadTimestamp(snapshot, "createdAt"),
        ReadTimestamp(snapshot, "updatedAt"));
        if (!BlackjackSoloReplay.Verify(game))
            throw new InvalidOperationException("The saved Blackjack hand does not replay from its deck and wager.");
        return game;
    }

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;

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

    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.UnixEpoch;

    private static string? EmptyToNull(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
