using Google.Cloud.Firestore;
namespace FortuneForge.Server.Games.Snake;

/// <summary>Compatibility facade over the authoritative package-owned Snake backend.</summary>
public sealed class FirestoreSnakeGameService : FortuneForge.Games.Snake.Server.FirestoreSnakeGameService
{
    public FirestoreSnakeGameService(FirestoreDb database, TimeProvider clock) : base(database, clock) { }
    internal FirestoreSnakeGameService(FirestoreDb database, TimeProvider clock, Func<uint> nextSeed) : base(database, clock, nextSeed) { }
}
