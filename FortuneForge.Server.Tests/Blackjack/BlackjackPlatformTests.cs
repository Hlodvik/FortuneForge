using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Tests.Accounts;
using Google.Cloud.Firestore;
using Managed = FortuneForge.Games.Abstractions.ManagedPlayers;

namespace FortuneForge.Server.Tests.Blackjack;

/// <summary>Runs package-owned games against the real generic platform wallet and roster in host integration tests.</summary>
internal static class BlackjackPlatformTests
{
    internal static BlackjackSoloStore Solo(FirestoreDb database)
    {
        var wallet = new GameWalletFirestoreTestClient(database, ManagedPlayerGames.Blackjack);
        return new(database, wallet, wallet);
    }

    internal static FirestoreBlackjackTableStore Table(FirestoreDb database,
        Func<IReadOnlyList<string>>? deckFactory = null, Func<ulong>? seedFactory = null,
        IMultiplayerMatchmaker? multiplayerMatchmaker = null, Managed.IManagedTablePopulationDirector? managedPopulationDirector = null,
        bool multiplayerEnabled = true)
    {
        var wallet = new GameWalletFirestoreTestClient(database, ManagedPlayerGames.Blackjack);
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        var matchmaker = multiplayerMatchmaker ?? new MultiplayerMatchmaker(new ManagedPlayerQueuer(
            new ManagedPlayerProfileGenerator(profiles), profiles, new FirestoreManagedPlayerAssignmentStore(database)));
        return new(database, wallet, wallet, new Roster(matchmaker), deckFactory, seedFactory,
            managedPopulationDirector, multiplayerEnabled);
    }

    private sealed class Roster(IMultiplayerMatchmaker matchmaker) : Managed.IManagedPlayerRosterClient
    {
        public async Task<IReadOnlyList<Managed.ManagedPlayerRosterProfile>> ReserveAsync(string assignment, int count,
            IReadOnlyCollection<string> exclusions, DateTime now, CancellationToken token) =>
            (await matchmaker.ReserveManagedPlayersAsync("blackjack", assignment, count, exclusions, now, token))
                .Select(profile => new Managed.ManagedPlayerRosterProfile(profile.UserId, profile.PlayerName, profile.SkillLevel)).ToArray();
        public Task HeartbeatAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) =>
            matchmaker.HeartbeatManagedPlayersAsync("blackjack", assignment, profiles, now, token);
        public Task<bool> TryRenewAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) =>
            matchmaker.TryRenewManagedPlayersAsync("blackjack", assignment, profiles, now, token);
        public Task ReleaseAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) =>
            matchmaker.ReleaseManagedPlayersAsync(assignment, profiles, now, token);
    }
}
