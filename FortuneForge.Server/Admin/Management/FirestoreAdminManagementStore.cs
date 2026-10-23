using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Admin.Management;

internal sealed class FirestoreAdminManagementStore(FirestoreDb database) : IAdminManagementStore
{
    internal const string StatePath = "adminRuntimeConfiguration/current";
    internal const string AuditCollection = "adminManagementAudit";
    private const string GuardsCollection = "adminManagementIdempotency";

    public async Task<AdminRuntimeState> ReadAsync(CancellationToken cancellationToken) =>
        ReadState(await database.Document(StatePath).GetSnapshotAsync(cancellationToken));

    public Task<AdminManagementResponse> MutateAsync(AdminManagementMutation mutation, CancellationToken cancellationToken)
    {
        var stateReference = database.Document(StatePath);
        var guard = database.Collection(GuardsCollection).Document(Hash($"guard\n{mutation.ActorUserId}\n{mutation.IdempotencyKey}"));
        var fingerprint = Hash(JsonSerializer.Serialize(new { mutation.Operation, mutation.GameId, mutation.Value, mutation.ExpectedVersion, mutation.Reason }));
        return RunTransactionAsync(async transaction =>
        {
            var snapshots = await Task.WhenAll(transaction.GetSnapshotAsync(stateReference, cancellationToken), transaction.GetSnapshotAsync(guard, cancellationToken));
            var prior = ReadState(snapshots[0]);
            if (snapshots[1].Exists)
            {
                if (snapshots[1].GetValue<string>("fingerprint") != fingerprint)
                    throw new AdminManagementConflictException("idempotency-conflict", "This request key was already used for a different change.");
                return JsonSerializer.Deserialize<AdminManagementResponse>(snapshots[1].GetValue<string>("response"))
                    ?? throw new InvalidOperationException("The management request receipt is invalid.");
            }
            if (prior.Version != mutation.ExpectedVersion)
                throw new AdminManagementConflictException("version-conflict", "Management settings changed. Refresh them before trying again.");

            var gameBots = new Dictionary<string, bool>(prior.GameBots, StringComparer.Ordinal);
            var visibility = new Dictionary<string, bool>(prior.Visibility, StringComparer.Ordinal);
            bool before;
            switch (mutation.Operation)
            {
                case "bots-global": before = prior.BotsEnabled; break;
                case "bots-game":
                    _ = AdminManagementCatalog.Require(mutation.GameId!);
                    before = gameBots.GetValueOrDefault(mutation.GameId!, true);
                    gameBots[mutation.GameId!] = mutation.Value;
                    break;
                case "catalog-visibility":
                    var game = AdminManagementCatalog.Require(mutation.GameId!);
                    before = prior.IsVisible(game);
                    visibility[game.Id] = mutation.Value;
                    break;
                default: throw new AdminManagementInputException("The management operation is not supported.");
            }
            var next = new AdminRuntimeState(checked(prior.Version + 1),
                mutation.Operation == "bots-global" ? mutation.Value : prior.BotsEnabled,
                gameBots, visibility, mutation.NowUtc);
            var response = next.Response();
            var auditId = next.Version.ToString("D20", CultureInfo.InvariantCulture);
            transaction.Set(stateReference, new Dictionary<string, object>
            {
                ["version"] = next.Version, ["botsEnabled"] = next.BotsEnabled,
                ["gameBots"] = gameBots.ToDictionary(pair => pair.Key, pair => (object)pair.Value),
                ["visibility"] = visibility.ToDictionary(pair => pair.Key, pair => (object)pair.Value),
                ["updatedAt"] = Timestamp.FromDateTime(mutation.NowUtc), ["schemaVersion"] = 1L,
            });
            transaction.Create(database.Collection(AuditCollection).Document(auditId), new Dictionary<string, object>
            {
                ["version"] = next.Version, ["operation"] = mutation.Operation,
                ["gameId"] = mutation.GameId ?? string.Empty,
                ["actorId"] = Hash($"admin-actor\n{mutation.ActorUserId}"),
                ["occurredAt"] = Timestamp.FromDateTime(mutation.NowUtc),
                ["reason"] = mutation.Reason, ["before"] = before, ["after"] = mutation.Value,
                ["schemaVersion"] = 1L,
            });
            transaction.Create(guard, new Dictionary<string, object>
            {
                ["fingerprint"] = fingerprint, ["response"] = JsonSerializer.Serialize(response),
                ["createdAt"] = Timestamp.FromDateTime(mutation.NowUtc), ["schemaVersion"] = 1L,
            });
            return response;
        }, cancellationToken);
    }

    private async Task<T> RunTransactionAsync<T>(Func<Transaction, Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await database.RunTransactionAsync(action, cancellationToken: cancellationToken); }
            catch (RpcException exception) when (attempt < 8 &&
                (exception.StatusCode == StatusCode.Aborted ||
                 exception.StatusCode == StatusCode.InvalidArgument && exception.Status.Detail.Contains("Transaction is invalid or closed", StringComparison.OrdinalIgnoreCase)))
            {
                // The receipt and audit share the state transaction, so an uncertain
                // commit can be retried without applying the change twice.
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500, attempt * 50) + Random.Shared.Next(20, 100)), cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<AdminManagementAuditItem>> AuditAsync(int limit, long? beforeVersion, CancellationToken cancellationToken)
    {
        Query query = database.Collection(AuditCollection).OrderByDescending("version");
        if (beforeVersion.HasValue) query = query.StartAfter(beforeVersion.Value);
        var snapshot = await query.Limit(limit).GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(document => new AdminManagementAuditItem(
            document.Id, document.GetValue<long>("version"), document.GetValue<string>("operation"),
            document.GetValue<string>("gameId") is { Length: > 0 } gameId ? gameId : null,
            document.GetValue<string>("actorId"), document.GetValue<Timestamp>("occurredAt").ToDateTime(),
            document.GetValue<string>("reason"), document.GetValue<bool>("before"), document.GetValue<bool>("after"))).ToArray();
    }

    internal static AdminRuntimeState ReadState(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) return AdminRuntimeState.Default;
        var version = snapshot.GetValue<long>("version");
        if (version < 0) throw new InvalidOperationException("The management settings version is invalid.");
        return new(version, snapshot.GetValue<bool>("botsEnabled"), ReadFlags(snapshot, "gameBots"),
            ReadFlags(snapshot, "visibility"), snapshot.GetValue<Timestamp>("updatedAt").ToDateTime());
    }

    private static IReadOnlyDictionary<string, bool> ReadFlags(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.TryGetValue<Dictionary<string, object>>(field, out var values)) return new Dictionary<string, bool>();
        var allowlist = AdminManagementCatalog.Games.Select(game => game.Id).ToHashSet(StringComparer.Ordinal);
        return values.Where(pair => allowlist.Contains(pair.Key)).ToDictionary(pair => pair.Key,
            pair => pair.Value is bool flag ? flag : throw new InvalidOperationException("A management switch is invalid."), StringComparer.Ordinal);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
