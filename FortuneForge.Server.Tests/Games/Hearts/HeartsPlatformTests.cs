using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Tests.Accounts;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Tests.Games.Hearts;

/// <summary>Composes the package owner with real generic platform storage for host integration tests.</summary>
internal static class HeartsPlatformTests
{
    internal static FirestorePaidHeartsStore Paid(FirestoreDb database, IMultiplayerMatchmaker? matchmaker = null,
        PaidHeartsOptions? options = null, ILogger<FirestorePaidHeartsStore>? logger = null)
    {
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        matchmaker ??= new MultiplayerMatchmaker(new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles),
            profiles, new FirestoreManagedPlayerAssignmentStore(database)));
        return new(database, new BatchWallet(database), new GameWalletFirestoreTestClient(database, HeartsBackend.GameId),
            new Roster(matchmaker), options, logger, () => true);
    }

    private sealed class BatchWallet(FirestoreDb database) : IGameWalletBatchClient
    {
        public Task<GameWalletBatchReceipt> SettleBatchAsync(string actorAccountId, GameWalletBatchRequest request, CancellationToken token)
        {
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson)));
            return new GameWalletBatchFirestoreStore(database).SettleAsync(HeartsBackend.GameId, actorAccountId, request, hash,
                DateTimeOffset.UtcNow, token);
        }
    }

    private sealed class Roster(IMultiplayerMatchmaker matchmaker) : IManagedPlayerRosterClient
    {
        public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignment, int count,
            IReadOnlyCollection<string> exclusions, DateTime now, CancellationToken token) =>
            throw new InvalidOperationException("New Hearts cohorts use labelled virtual house seats.");
        public Task HeartbeatAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) =>
            matchmaker.HeartbeatManagedPlayersAsync(HeartsBackend.GameId, assignment, profiles, now, token);
        public Task<bool> TryRenewAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) =>
            matchmaker.TryRenewManagedPlayersAsync(HeartsBackend.GameId, assignment, profiles, now, token);
        public Task ReleaseAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) =>
            matchmaker.ReleaseManagedPlayersAsync(assignment, profiles, now, token);
    }
}
