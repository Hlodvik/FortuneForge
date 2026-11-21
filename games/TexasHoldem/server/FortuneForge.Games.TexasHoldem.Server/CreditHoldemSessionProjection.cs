using System.Text.Json;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal static class CreditHoldemSessionProjection
{
    internal static Task<CreditHoldemSessionResponse> FromAfterAsync(CreditHoldemMutation mutation, FirestoreDb database,
        string account, DateTime now, CancellationToken token) => ResolveAsync(database, account, now,
        document => mutation.AfterAsync(document, token));

    internal static Task<CreditHoldemSessionResponse> ReadAsync(FirestoreDb database, string account, DateTime now,
        CancellationToken token) => ResolveAsync(database, account, now, async document =>
        {
            var snapshot = await document.GetSnapshotAsync(token);
            return snapshot.Exists ? snapshot.ToDictionary() : new Dictionary<string, object>();
        });

    private static async Task<CreditHoldemSessionResponse> ResolveAsync(FirestoreDb database, string account, DateTime now,
        Func<DocumentReference, Task<Dictionary<string, object>>> read)
    {
        var session = await read(database.Collection(CreditHoldemBackend.Sessions).Document(CreditHoldemIds.Hash(account)));
        var kind = Text(session, "kind");
        if (kind.Length == 0 || kind == CreditHoldemSessionKinds.Idle) return new CreditHoldemIdleSessionResponse();
        if (kind == CreditHoldemSessionKinds.Queue)
        {
            var fields = await read(database.Collection(CreditHoldemBackend.Tickets).Document(Text(session, "ticketId")));
            var ticket = JsonSerializer.Deserialize<CreditHoldemTicket>(Text(fields, "ticketJson"), CreditHoldemMutation.Json)
                ?? throw new CreditHoldemNotFoundException("The Hold'em queue ticket was not found.");
            var queue = ticket.Status == "pending-next-hand" ? [ticket] :
                JsonSerializer.Deserialize<List<CreditHoldemTicket>>(
                    Text(await read(database.Collection(CreditHoldemBackend.QueuePartitions).Document(ticket.PartitionKey)), "queueJson"), CreditHoldemMutation.Json)
                    ?? [];
            var ordered = queue.Where(value => value.Status is "queued" or "pending-next-hand")
                .OrderBy(value => value.JoinedAtUtc).ThenBy(value => value.TicketId, StringComparer.Ordinal).ToArray();
            return new CreditHoldemQueueSessionResponse(ticket.TicketId,
                Array.FindIndex(ordered, value => value.UserId == account) + 1, ticket.JoinedAtUtc, ticket.GraceEndsAtUtc,
                ordered.Select((value, seat) => new CreditHoldemSeatResponse(value.PublicSeatId, value.DisplayName, seat,
                    value.BuyInCents, value.BuyInCents, 0, 0, value.Status, null, [], null, value.UserId == account)).ToArray(),
                ticket.Version, CreditHoldemTableRules.Resolve(ticket.TableRuleId).Public);
        }
        var saved = await read(database.Collection(CreditHoldemBackend.Matches).Document(Text(session, "matchId")));
        if (Text(saved, "matchJson") is not { Length: > 0 } json)
            throw new CreditHoldemNotFoundException("The Hold'em match was not found.");
        var match = CreditHoldemMatchPersistence.Deserialize(json);
        if (saved.ContainsKey("managedPlayersReleasedAt") || !match.Players.Any(player => player.IsAccountBacked &&
            player.ActorId == account && !match.LeavingActorIds.Contains(account))) return new CreditHoldemIdleSessionResponse();
        return kind == CreditHoldemSessionKinds.Result ? CreditHoldemProjection.Result(match, account, now) : CreditHoldemProjection.Match(match, account, now);
    }

    private static string Text(IReadOnlyDictionary<string, object> fields, string name) => fields.GetValueOrDefault(name) as string ?? string.Empty;
}
