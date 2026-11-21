using Google.Cloud.Firestore;

namespace FortuneForge.Games.Firestore;

/// <summary>Reads the runtime switches for admitting new managed players.</summary>
public static class FirestoreManagedPlayerPolicy
{
    public const string StatePath = "adminRuntimeConfiguration/current";
    public const bool DefaultEnabled = true;

    public static bool AllowsBots(DocumentSnapshot snapshot, string gameId)
    {
        var (botsEnabled, gameBots) = Read(snapshot, id => id == gameId);
        return AllowsBots(botsEnabled, gameBots, gameId);
    }

    public static bool AllowsBots(bool botsEnabled, IReadOnlyDictionary<string, bool> gameBots, string gameId) =>
        botsEnabled && gameBots.GetValueOrDefault(gameId, DefaultEnabled);

    public static (bool BotsEnabled, IReadOnlyDictionary<string, bool> GameBots) Read(DocumentSnapshot snapshot,
        Func<string, bool> includesGame)
    {
        var gameBots = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (!snapshot.Exists) return (DefaultEnabled, gameBots);

        var fields = snapshot.ToDictionary();
        if (fields.GetValueOrDefault("botsEnabled") is not bool botsEnabled)
            throw new InvalidOperationException("The global managed player switch is invalid.");
        if (!fields.TryGetValue("gameBots", out var storedGames)) return (botsEnabled, gameBots);
        if (storedGames is not IReadOnlyDictionary<string, object> values)
            throw new InvalidOperationException("The managed player game switches are invalid.");

        foreach (var (gameId, value) in values)
        {
            if (!includesGame(gameId)) continue;
            if (value is not bool enabled) throw new InvalidOperationException("A management switch is invalid.");
            gameBots.Add(gameId, enabled);
        }
        return (botsEnabled, gameBots);
    }
}
