using FortuneForge.Games.Flappy.Server;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade.Flappy;

/// <summary>Legacy host adapter; the game package owns durable run state and replay.</summary>
public sealed class FirestoreFlappyFreeRunService : FlappyFreeRunStore
{
    internal FirestoreFlappyFreeRunService(FirestoreDb database, TimeProvider timeProvider, Func<uint>? createSeed = null)
        : base(database, timeProvider, createSeed) { }
}
