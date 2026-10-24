using FortuneForge.Server.Bots;

namespace FortuneForge.Server.Admin.Management;

internal sealed record AdminManagedGame(string Id, string DisplayName, string Category, string Route, bool DefaultVisible = true)
{
    public bool BotsSupported => ManagedPlayerGames.All.Contains(Id, StringComparer.Ordinal);
}

/// <summary>Release-owned allowlist. Runtime writes cannot introduce routes or games.</summary>
internal static class AdminManagementCatalog
{
    public static IReadOnlyList<AdminManagedGame> Games { get; } = [
        new("blackjack", "Fortune Blackjack", "cards", "/cards/blackjack"),
        new("texas-holdem", "Texas Hold’em", "cards", "/cards/texas-holdem"),
        new("solitaire", "Competitive Solitaire", "cards", "/cards/solitaire"),
        new("video-poker", "Video Poker", "cards", "/games/video-poker"),
        new("baccarat", "Baccarat", "cards", "/games/baccarat"),
        new("dragon-tiger", "Dragon Tiger", "casino", "/games/dragon-tiger"),
        new("hi-lo", "Hi-Lo", "casino", "/games/hi-lo"),
        new("andar-bahar", "Andar Bahar", "casino", "/games/andar-bahar"),
        new("casino-war", "Casino War", "cards", "/games/casino-war"),
        new("hearts", "Hearts", "cards", "/cards/hearts"),
        new("three-card-poker", "Three Card Poker", "cards", "/cards/three-card-poker"),
        new("house-holdem", "House Hold’em", "cards", "/cards/house-holdem"),
        new("keno", "Keno", "casino", "/games/keno"),
        new("sic-bo", "Sic Bo", "dice", "/games/sic-bo"),
        new("roulette", "Roulette", "casino", "/games/roulette"),
        new("craps", "Craps", "dice", "/games/craps"),
        new("liars-dice", "Liar’s Dice", "dice", "/games/liars-dice"),
        new("asteroids", "Asteroid Blaster", "arcade", "/games/asteroids", false),
        new("flappy", "Flappy", "arcade", "/games/flappy", false),
        new("2048", "2048", "arcade", "/games/2048", false),
        new("drop-merge", "Drop Merge", "arcade", "/games/drop-merge", false),
        new("snake", "Snake", "arcade", "/games/snake", false),
        new("horse-flight", "Horse Flight", "arcade", "/games/horse-flight", false),
        new("wukong-journey-to-the-west", "Wukong’s Journey to the West", "slots", "/slots/wukong"),
        new("rainbow-realm", "Rainbow Realm", "slots", "/slots/rainbow-realm"),
        new("pirates-fortune", "Pirates’ Fortune", "slots", "/slots/pirates-fortune"),
        new("gods-of-olympus", "Gods of Olympus", "slots", "/slots/gods-of-olympus"),
        new("reel-riches", "Reel Riches", "slots", "/slots/reel-riches"),
        new("high-noon-fortune", "High Noon Fortune", "slots", "/slots/high-noon-fortune"),
        new("royal-draw", "Royal Draw", "slots", "/slots/royal-draw"),
        new("arcane-archives", "Arcane Archives", "slots", "/slots/arcane-archives"),
        new("cosmic-fortune", "Cosmic Fortune", "slots", "/slots/cosmic-fortune"),
        new("dino-dominion", "Dino Dominion", "slots", "/slots/dino-dominion"),
        new("neon-nights", "Neon Nights", "slots", "/slots/neon-nights"),
        new("jungle-jackpot", "Jungle Jackpot", "slots", "/slots/jungle-jackpot"),
        new("ocean-odyssey", "Ocean Odyssey", "slots", "/slots/ocean-odyssey"),
        new("samurai-fortune", "Samurai Fortune", "slots", "/slots/samurai-fortune"),
        new("candy-carnival", "Candy Carnival", "slots", "/slots/candy-carnival"),
        new("phantom-manor", "Phantom Manor", "slots", "/slots/phantom-manor"),
        new("nordic-legends", "Nordic Legends", "slots", "/slots/nordic-legends"),
        new("desert-treasures", "Desert Treasures", "slots", "/slots/desert-treasures"),
        new("robot-revolution", "Robot Revolution", "slots", "/slots/robot-revolution"),
        new("dragon-hoard", "Dragon Hoard", "slots", "/slots/dragon-hoard"),
    ];

    public static AdminManagedGame Require(string id) => Games.FirstOrDefault(game => string.Equals(game.Id, id, StringComparison.Ordinal))
        ?? throw new AdminManagementInputException("Choose a game returned by the management catalogue.");
}
