namespace FortuneForge.Server.Tests.Catalog;

/// <summary>Independent release expectations shared by the public catalogue contracts.</summary>
internal static class CatalogContract
{
    internal static IReadOnlyList<(string Id, string Route)> Entries { get; } = new (string Id, string Route)[]
    {
        ("blackjack", "/cards/blackjack"),
        ("texas-holdem", "/cards/texas-holdem"),
        ("solitaire", "/cards/solitaire"),
        ("free-solitaire", "/cards/free-solitaire"),
        ("video-poker", "/games/video-poker"),
        ("baccarat", "/games/baccarat"),
        ("dragon-tiger", "/games/dragon-tiger"),
        ("hi-lo", "/games/hi-lo"),
        ("andar-bahar", "/games/andar-bahar"),
        ("mines", "/games/mines"),
        ("casino-war", "/games/casino-war"),
        ("hearts", "/cards/hearts"),
        ("three-card-poker", "/cards/three-card-poker"),
        ("house-holdem", "/cards/house-holdem"),
        ("keno", "/games/keno"),
        ("sic-bo", "/games/sic-bo"),
        ("roulette", "/games/roulette"),
        ("craps", "/games/craps"),
        ("liars-dice", "/games/liars-dice"),
        ("asteroids", "/games/asteroids"),
        ("flappy", "/games/flappy"),
        ("2048", "/games/2048"),
        ("drop-merge", "/games/drop-merge"),
        ("snake", "/games/snake"),
        ("horse-flight", "/games/horse-flight"),
        ("wukong-journey-to-the-west", "/slots/wukong"),
        ("rainbow-realm", "/slots/rainbow-realm"),
        ("pirates-fortune", "/slots/pirates-fortune"),
        ("gods-of-olympus", "/slots/gods-of-olympus"),
        ("reel-riches", "/slots/reel-riches"),
        ("high-noon-fortune", "/slots/high-noon-fortune"),
        ("royal-draw", "/slots/royal-draw"),
        ("arcane-archives", "/slots/arcane-archives"),
        ("cosmic-fortune", "/slots/cosmic-fortune"),
        ("gold-foundry", "/slots/gold-foundry"),
        ("neon-nights", "/slots/neon-nights"),
        ("empty-slots-game", "/slots/empty-slots-game"),
        ("ocean-odyssey", "/slots/ocean-odyssey"),
        ("samurai-fortune", "/slots/samurai-fortune"),
        ("candy-carnival", "/slots/candy-carnival"),
        ("phantom-manor", "/slots/phantom-manor"),
        ("nordic-legends", "/slots/nordic-legends"),
        ("desert-treasures", "/slots/desert-treasures"),
        ("robot-revolution", "/slots/robot-revolution"),
        ("dragon-hoard", "/slots/dragon-hoard"),
    }.OrderBy(game => game.Id, StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<string> DefaultHiddenIds { get; } = ["asteroids", "flappy", "horse-flight", "liars-dice", "snake"];
    internal static IReadOnlyList<string> UntrackedArcadeIds { get; } = ["asteroids", "flappy", "horse-flight", "snake"];
    internal static IReadOnlyList<string> Ids { get; } = Entries.Select(game => game.Id).ToArray();
    internal static IReadOnlyList<string> DefaultVisibleIds { get; } = Ids.Except(DefaultHiddenIds, StringComparer.Ordinal).ToArray();
    internal static IReadOnlyList<string> AnalyticsIds { get; } = Ids.Except(UntrackedArcadeIds, StringComparer.Ordinal).ToArray();
}
