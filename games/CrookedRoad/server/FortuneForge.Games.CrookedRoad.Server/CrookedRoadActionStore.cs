using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.CrookedRoad.Server;

public sealed class CrookedRoadActionStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string CollectionName = "crookedRoadActions";
    internal Task<CrookedRoadStateReply> ActAsync(string account, string operationId, CrookedRoadAction action,
        GameWalletAccountState wallet, CancellationToken token)
    {
        if (!IClassicSlotRoundRequest.IsValidOperationId(operationId)) throw new ArgumentException("Include one valid road action operation ID.");
        var operation = database.Collection(CollectionName).Document(GameSecurityEventDocuments.Hash($"{CrookedRoadRules.GameId}\n{account}\n{operationId}"));
        var guard = database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(account, CrookedRoadRules.GameId));
        var requestHash = CrookedRoadEvidence.Hash(action);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, guard], token);
            if (snapshots[0].Exists)
            {
                var saved = snapshots[0];
                if (saved.GetValue<string>("userId") != account || saved.GetValue<string>("requestSha256") != requestHash)
                    throw new CrookedRoadConflictException("road-operation-conflict", "This road action ID already belongs to different options.");
                var stateJson = saved.GetValue<string>("afterJson");
                if (saved.GetValue<string>("afterSha256") != GameSecurityEventDocuments.Hash(stateJson))
                    throw new GameWalletUnavailableException();
                return CrookedRoadStateReply.From(CrookedRoadState.Parse(stateJson), wallet.SlotsCreditsBalanceCents);
            }
            var current = snapshots[1];
            if (Text(current, SlotSpinGuard.ActiveAdmissionField) is not null || Text(current, SlotSpinGuard.PreparedAdmissionField) is not null)
                throw new CrookedRoadConflictException("road-spin-pending", "Finish the prepared play before changing the road.");
            var classic = await CrookedRoadRoundStateCodec.Storage.ReadAsync(transaction, database, current, account, wallet.LegacyFreeGamesRemaining, token);
            if (classic.LegacyRemaining != 0 || classic.NativeRemaining != 0 || classic.Carry is not null)
                throw new GameWalletUnavailableException();
            var before = CrookedRoadState.Parse(classic.NativeStateJson);
            var tape = new CrookedRoadTape(random);
            var after = CrookedRoadMap.Act(before, action, (ticket, destination) => CrookedRoadGame.Begin(ticket, destination, tape));
            var draws = tape.Finish();
            var afterJson = CrookedRoadState.Serialize(after);
            var now = clock.GetUtcNow().UtcDateTime;
            var fields = CrookedRoadRoundStateCodec.Storage.Write(classic with { NativeStateJson = afterJson });
            fields["userId"] = account;
            fields["updatedAt"] = Timestamp.FromDateTime(now);
            transaction.Set(guard, fields, SetOptions.MergeAll);
            transaction.Create(operation, new Dictionary<string, object>
            {
                ["userId"] = account, ["operationId"] = operationId, ["requestSha256"] = requestHash,
                ["beforeJson"] = CrookedRoadState.Serialize(before), ["afterJson"] = afterJson,
                ["afterSha256"] = GameSecurityEventDocuments.Hash(afterJson), ["drawsJson"] = JsonSerializer.Serialize(draws),
                ["createdAt"] = Timestamp.FromDateTime(now)
            });
            var evidence = new { operationId, action, before, after, draws };
            var journal = GameSecurityEventDocuments.Capture(CrookedRoadPaidRouteContract.AuditGameId, operation.Id,
                after.Revision, now, evidence, [account], "road-action");
            CrookedRoadReport.WriteJournal(transaction, database, journal, operation.Id);
            return CrookedRoadStateReply.From(after, wallet.SlotsCreditsBalanceCents);
        }, cancellationToken: token);
    }

    private static string? Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed class CrookedRoadRoundService(CrookedRoadRoundStore rounds, CrookedRoadActionStore actions,
    IGameWalletAccountReader accounts, IGameWalletClient wallet)
{
    public async Task<CrookedRoadStateReply> ReadStateAsync(string account, CancellationToken token) =>
        await rounds.ReadAsync(account, await accounts.GetStateAsync(account, token), token);
    public Task<CrookedRoadRoundResult> SpinAsync(string account, CrookedRoadRoundRequest request, bool allowStart, CancellationToken token) =>
        rounds.SpinAsync(account, request, allowStart, wallet, accounts, token);
    public async Task<CrookedRoadStateReply> ActAsync(string account, string operation, CrookedRoadAction action, CancellationToken token) =>
        await actions.ActAsync(account, operation, action, await accounts.GetStateAsync(account, token), token);
}
