using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Solitaire;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Tests.Solitaire;

internal static class CompetitiveSolitaireTestStore
{
    internal static FirestoreCompetitiveSolitaireStore Create(FirestoreDb database, bool allowManagedPlayers = false)
    {
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        var queuer = new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles), profiles,
            new FirestoreManagedPlayerAssignmentStore(database));
        return Create(database, queuer, TimeProvider.System,
            new CompetitiveSolitaireOptions { AllowSingleHumanBotFill = allowManagedPlayers, AllowNewPaidRounds = true });
    }

    internal static FirestoreCompetitiveSolitaireStore Create(FirestoreDb database, IManagedPlayerQueuer queuer,
        TimeProvider clock, CompetitiveSolitaireOptions options,
        ILogger<FirestoreCompetitiveSolitaireStore>? logger = null)
    {
        var wallet = new PlatformWallet(database, clock);
        return new FirestoreCompetitiveSolitaireStore(database, wallet, wallet, new PlatformRoster(queuer), options, logger);
    }

    internal static Task SeedBalanceAsync(FirestoreDb database, string account, DateTime now, long available = 100) =>
        database.Collection("userBalances").Document($"{account}_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = account, ["currencyId"] = "slotsCredits", ["available"] = available,
            ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L,
            ["updatedAt"] = Timestamp.FromDateTime(now),
        });

    private sealed class PlatformWallet(FirestoreDb database, TimeProvider clock)
        : IGameWalletBatchClient, IGameWalletAccountReader
    {
        private readonly GameWalletBatchFirestoreStore settlements = new(database);

        public Task<GameWalletBatchReceipt> SettleBatchAsync(string actorAccountId, GameWalletBatchRequest request,
            CancellationToken cancellationToken)
        {
            var frozen = request with { Effects = request.Effects.ToArray() };
            var body = JsonSerializer.SerializeToUtf8Bytes(frozen, GameWalletAuthentication.WireJson);
            var digest = Convert.ToHexString(SHA256.HashData(body));
            return settlements.SettleAsync(CompetitiveSolitaireEndpoints.GameId, actorAccountId, frozen, digest,
                clock.GetUtcNow(), cancellationToken);
        }

        public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
        {
            if (!GameWalletBatchVerifier.IsValidAccountId(accountId))
                throw new ArgumentException("The Solitaire wallet account is invalid.", nameof(accountId));
            var cents = await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, accountId,
                useLegacyLedger: false, cancellationToken);
            return new GameWalletAccountState(0, cents);
        }
    }

    private sealed class PlatformRoster(IManagedPlayerQueuer queuer) : IManagedPlayerRosterClient
    {
        private const string GameId = CompetitiveSolitaireEndpoints.GameId;

        public async Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var profiles = await queuer.ReserveAsync(GameId, assignmentId, count, excludedProfileIds, nowUtc, cancellationToken);
            return profiles.Select(profile => new ManagedPlayerRosterProfile(profile.UserId, profile.PlayerName, profile.SkillLevel)).ToArray();
        }

        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc,
            CancellationToken cancellationToken) => queuer.HeartbeatAsync(GameId, assignmentId, profileIds, nowUtc, cancellationToken);

        public Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc,
            CancellationToken cancellationToken) => queuer.TryRenewAsync(GameId, assignmentId, profileIds, nowUtc, cancellationToken);

        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc,
            CancellationToken cancellationToken) => queuer.ReleaseForGameAsync(GameId, assignmentId, profileIds, nowUtc, cancellationToken);
    }
}
