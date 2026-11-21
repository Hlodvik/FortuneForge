using FortuneForge.Games.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Dice;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.LiarsDice.Server;

/// <summary>Owns free account matches and commits every accepted action, replay and request receipt atomically.</summary>
public class LiarsDiceGameService(FirestoreDb database, ILiarsDiceManagedPlayerRoster managedPlayers, TimeProvider clock)
{
    public LiarsDiceStatusResponse Status() => new(true, LiarsDiceMatchEngine.DefaultDicePerPlayer, "free-play");

    public async Task<LiarsDiceMatchResponse> StartAsync(string userId, StartLiarsDiceMatchRequest request,
        CancellationToken token = default, string? bodySha256 = null)
    {
        var dicePerPlayer = request.DicePerPlayer ?? LiarsDiceMatchEngine.DefaultDicePerPlayer;
        if (dicePerPlayer is <= 0 or > 20) throw new LiarsDiceRuleException("Choose between 1 and 20 dice per player.");
        var now = UtcNow();
        var id = PuzzleSessionIdentity.CreateDurableId(now);
        var assignment = LiarsDiceSession.AssignmentId(id);
        IReadOnlyList<LiarsDiceManagedPlayer> players = [];
        LiarsDiceSession? candidate = null;
        var keepAssignment = false;
        var resolved = false;
        try
        {
            var result = await Execute(userId, bodySha256, async (tx, ct) =>
            {
                // Receipt replay is checked before admission. Cache this separately idempotent lease
                // operation so a Firestore transaction retry never reserves another roster.
                if (candidate is null)
                {
                    players = await managedPlayers.ReserveAsync(assignment, 3, [userId], now, ct);
                    if (players.Count != 3) throw new LiarsDiceManagedPlayerSupplyException();
                    candidate = LiarsDiceSession.Start(id, userId, request.Seed ?? NextSeed(), dicePerPlayer, players, now);
                }
                tx.Create(Match(id), Fields(candidate));
                Write(tx, candidate, now);
                return candidate.ToResponse();
            }, token);
            resolved = true;
            keepAssignment = result.MatchId == id;
            return result;
        }
        finally
        {
            if (!keepAssignment && !resolved && candidate is not null)
            {
                // An interrupted commit response is not evidence of rollback. Retain committed leases.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { keepAssignment = (await Match(id).GetSnapshotAsync(timeout.Token)).Exists; }
                catch { keepAssignment = true; } // Uncertain unused leases expire under the roster policy.
            }
            if (!keepAssignment && players.Count > 0)
                await managedPlayers.ReleaseAsync(assignment, players.Select(player => player.UserId).ToArray(), UtcNow(), CancellationToken.None);
        }
    }

    public async Task<LiarsDiceMatchResponse> GetAsync(string userId, Guid id, CancellationToken token = default)
    {
        var session = Open(await Match(id).GetSnapshotAsync(token), userId);
        await MaintainLeases(session, token);
        return session.ToResponse();
    }

    public Task<LiarsDiceMatchResponse> BidAsync(string userId, Guid id, PlaceLiarsDiceBidRequest request,
        CancellationToken token = default, string? bodySha256 = null) =>
        Change(userId, id, bodySha256, (session, now) => session.HumanAction(new PlaceLiarsDiceBid(
            LiarsDiceSession.HumanId, new(request.Quantity, new DieValue(request.Face))), now), token);
    public Task<LiarsDiceMatchResponse> ChallengeAsync(string userId, Guid id, CancellationToken token = default, string? bodySha256 = null) =>
        Change(userId, id, bodySha256, (session, now) => session.HumanAction(new ChallengeLiarsDiceBid(LiarsDiceSession.HumanId), now), token);
    public Task<LiarsDiceMatchResponse> SpotOnAsync(string userId, Guid id, CancellationToken token = default, string? bodySha256 = null) =>
        Change(userId, id, bodySha256, (session, now) => session.HumanAction(new SpotOnLiarsDiceBid(LiarsDiceSession.HumanId), now), token);
    public Task<LiarsDiceMatchResponse> AdvanceAsync(string userId, Guid id, CancellationToken token = default, string? bodySha256 = null) =>
        Change(userId, id, bodySha256, (session, now) => session.Advance(now), token);
    public Task<LiarsDiceMatchResponse> NextRoundAsync(string userId, Guid id, uint? seed,
        CancellationToken token = default, string? bodySha256 = null)
    {
        var chosenSeed = seed ?? NextSeed();
        return Change(userId, id, bodySha256, (session, now) => session.NextRound(chosenSeed, now), token);
    }

    public async Task AdvanceDueManagedTurnsAsync(CancellationToken token = default)
    {
        var due = await database.Collection("liarsDiceMatches").WhereEqualTo("opponentsThinking", true)
            .Limit(100).GetSnapshotAsync(token);
        foreach (var snapshot in due.Documents)
        {
            var session = Decode(snapshot);
            if (session.NextManagedActionAtUtc <= UtcNow())
                await AdvanceAsync(session.UserId, session.Id, token);
        }
    }

    private async Task<LiarsDiceMatchResponse> Change(string userId, Guid id, string? bodySha256,
        Func<LiarsDiceSession, DateTime, LiarsDiceSession> change, CancellationToken token)
    {
        var result = await Execute(userId, bodySha256, async (tx, ct) =>
        {
            var reference = Match(id);
            var current = Open(await tx.GetSnapshotAsync(reference, ct), userId);
            var now = UtcNow();
            var previousTime = current.Actions.LastOrDefault()?.AcceptedAtUtc ?? current.StartedAtUtc;
            if (now < previousTime) now = previousTime;
            var next = change(current, now);
            if (next != current)
            {
                tx.Set(reference, Fields(next));
                Write(tx, next, now);
                FirestoreGameAudit.WriteAcceptedParticipation(tx, database, "liars-dice", userId, now);
                if (next.State.Winner is not null)
                {
                    var completion = LiarsDiceSessionReplay.Capture(next);
                    FirestoreGameAudit.WriteCompletion(tx, database, completion);
                    var analytics = GameAnalyticsOutbox.Create(new GameAnalyticsSettlement("liars-dice", id.ToString("N"),
                        userId, "free", true, next.CompletedAtUtc!.Value, next.StartedAtUtc, 0, 0,
                        RoundAuditDocuments.Summaries, completion.Summary.Id));
                    if (analytics is not null)
                        tx.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(analytics.Id), analytics.Fields);
                }
            }
            return next.ToResponse();
        }, token);
        // The match is durable before a lease side effect. A failed release/heartbeat is retried by reads.
        await MaintainLeases(Open(await Match(id).GetSnapshotAsync(token), userId), token);
        return result;
    }

    private async Task MaintainLeases(LiarsDiceSession session, CancellationToken token)
    {
        var players = session.ManagedActors.Keys.ToArray();
        if (session.State.Winner is not null)
            await managedPlayers.ReleaseAsync(LiarsDiceSession.AssignmentId(session.Id), players, UtcNow(), token);
        else await managedPlayers.HeartbeatAsync(LiarsDiceSession.AssignmentId(session.Id), players, UtcNow(), token);
    }

    private void Write(Transaction tx, LiarsDiceSession session, DateTime now) =>
        FirestoreGameAudit.WriteObservation(tx, database, "liars-dice", session.Id.ToString("N"), session.EventVersion,
            now, session, [session.UserId], session.EventKind, session.Id.ToString("N"));
    private Task<T> Execute<T>(string userId, string? bodySha256, Func<Transaction, CancellationToken, Task<T>> action, CancellationToken token) =>
        FirestoreGameCommands.ExecuteAsync(database, "liarsDiceRequestReceipts", userId, bodySha256, action,
            () => new LiarsDiceRuleException("The request identifier was already used for a different Liar's Dice action."), token);
    private DocumentReference Match(Guid id) => database.Collection("liarsDiceMatches").Document(id.ToString("N"));
    private static Dictionary<string, object> Fields(LiarsDiceSession session) => new()
    {
        ["userId"] = session.UserId, ["json"] = JsonSerializer.Serialize(session, RoundAuditCapture.Json),
        ["opponentsThinking"] = session.OpponentsThinking, ["version"] = session.EventVersion, ["schemaVersion"] = 1L,
    };
    private static LiarsDiceSession Open(DocumentSnapshot snapshot, string userId) =>
        snapshot.Exists && snapshot.GetValue<string>("userId") == userId ? Decode(snapshot) : throw new LiarsDiceAccessException();
    private static LiarsDiceSession Decode(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<LiarsDiceSession>(snapshot.GetValue<string>("json"), LiarsDiceStateJson.Options)
        ?? throw new InvalidOperationException("The saved Liar's Dice match is invalid.");
    private static uint NextSeed() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    private DateTime UtcNow()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Firestore stores microseconds. Use the same instant in JSON evidence and indexed timestamps.
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
}
