using FortuneForge.Games.HorseFlight.Server;
using Google.Cloud.Firestore;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Server.Arcade.HorseFlight;

/// <summary>Legacy hosting adapter for game-owned state. Detached workers use the signed account reader.</summary>
public sealed class HorseFlightFirestoreStore : HorseFlightFreeRunStore
{
    public HorseFlightFirestoreStore(FirestoreDb database) : base(database,
        new LegacyAccountReader(database)) { }

    private sealed class LegacyAccountReader(FirestoreDb database) : IGameWalletAccountReader
    {
        public async Task<GameWalletAccountState> GetStateAsync(string userId, CancellationToken cancellationToken)
        {
            var snapshot = await database.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync(cancellationToken);
            var units = snapshot.Exists && snapshot.TryGetValue<long>("available", out var amount) ? amount : 0;
            var cents = snapshot.Exists && snapshot.TryGetValue<long>("availableFractionalCents", out var fraction) ? fraction : 0;
            return new GameWalletAccountState(0, checked(units * 100 + Math.Clamp(cents, 0, 99)));
        }
    }
}
