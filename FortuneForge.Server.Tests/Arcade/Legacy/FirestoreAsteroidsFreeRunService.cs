using FortuneForge.Games.Asteroids.Server;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade.Asteroids;

/// <summary>Legacy host composition adapter; the package owns practice run state and replay.</summary>
public sealed class FirestoreAsteroidsFreeRunService : AsteroidsFreeRunStore
{
    internal FirestoreAsteroidsFreeRunService(FirestoreDb database, TimeProvider timeProvider,
        Func<ulong>? createSeed = null)
        : base(database, timeProvider, createSeed)
    {
    }
}
