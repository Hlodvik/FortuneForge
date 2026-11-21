using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.ThreeCardPoker.Server;
using FortuneForge.Server.Bots.ThreeCardPoker;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed partial class FirestoreThreeCardPokerTableStore(
    FirestoreDb database, IGameWalletClient wallet, IGameWalletAccountReader accounts,
    IManagedPlayerRosterClient roster, IManagedTablePopulationDirector population,
    Func<IReadOnlyList<string>>? deckFactory = null, Func<ulong>? seedFactory = null) : IThreeCardPokerTableStore
{
    private const string CurrencyId = "slotsCredits";
    private const int MaximumSweepBatch = 25;
    private static readonly TimeSpan GuardRetention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string leaseOwner = $"three-card-poker-table-worker-{Guid.NewGuid():N}";

    private async Task<long> BalanceAsync(string userId, CancellationToken token)
    {
        var state = await accounts.GetStateAsync(userId, token);
        return state.SlotsCreditsBalanceCents is >= 0 ? state.SlotsCreditsBalanceCents.Value : throw new GameWalletUnavailableException();
    }

    private async Task<LoadedBalances> ReadBalancesAsync(ThreeCardPokerTableLobbyState state, string? requestedUserId, CancellationToken token)
    {
        var users = Users(state, requestedUserId);
        var values = await Task.WhenAll(users.Select(user => BalanceAsync(user, token)));
        var original = users.Select((user, index) => (user, values[index])).ToDictionary(pair => pair.user, pair => pair.Item2, StringComparer.Ordinal);
        return new(new(original, StringComparer.Ordinal), original);
    }

    private static string[] Users(ThreeCardPokerTableLobbyState state, string? requestedUserId) => state.Sessions.Keys
        .Concat(state.Tickets.Select(ticket => ticket.UserId))
        .Concat(state.Tables.Values.SelectMany(table => table.Players.Where(player => !ThreeCardPokerManagedSeat.IsManaged(player)).Select(player => player.ActorId)))
        .Append(requestedUserId ?? string.Empty).Where(user => user.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private DocumentReference PendingAccount(string user) => database.Collection("threeCardPokerTablePendingAccounts").Document(ThreeCardPokerTableIds.Hash(user));
    private DocumentReference PendingShard(string stateId) => database.Collection("threeCardPokerTablePendingShards").Document(stateId);
    private DocumentReference Operation(string operationId) => database.Collection(ThreeCardPokerBackend.TableOperations).Document(operationId);
    private DocumentReference WalletReceipt(string operationId, string user) => WalletReceipt(Operation(operationId), user);
    private static DocumentReference WalletReceipt(DocumentReference operation, string user) =>
        operation.Collection("walletReceipts").Document(ThreeCardPokerTableIds.Hash(user));
    private DocumentReference CommandReceipt(string user, string key) => database.Collection("threeCardPokerTableRequestReceipts").Document(ThreeCardPokerTableIds.Hash($"{user}\n{key}"));

    internal async Task<bool> HasCommandAsync(string userId, string key, CancellationToken token)
    {
        var snapshots = await Task.WhenAll(CommandReceipt(userId, key).GetSnapshotAsync(token), GuardDocument(userId, key).GetSnapshotAsync(token));
        return snapshots.Any(snapshot => snapshot.Exists);
    }
}
