using System.Security.Cryptography;

namespace FortuneForge.Server.Bots;

internal sealed record ManagedPlayerProfile(
    string UserId,
    string PlayerName,
    int SkillLevel,
    IReadOnlySet<string> SupportedGames,
    DateTime CreatedAtUtc);

internal static class ManagedPlayerProfileSchema
{
    public const string AuthenticationProvider = "managed-game-player";
    public const string BotTag = "bot";
    public const string ProfileTagsField = "profileTags";
}

internal static class ManagedPlayerGames
{
    public const string Blackjack = "blackjack";
    public const string Hearts = "hearts";
    public const string LiarsDice = "liars-dice";
    public const string Solitaire = "solitaire";
    public const string TexasHoldem = "texas-holdem";
    public static readonly IReadOnlyList<string> All =
        [Blackjack, Hearts, LiarsDice, Solitaire, TexasHoldem];
}

internal interface IManagedPlayerProfileGenerator
{
    Task<ManagedPlayerProfile> GenerateAsync(
        string gameId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal interface IManagedPlayerProfileRepository
{
    Task<bool> TryCreateAsync(
        ManagedPlayerProfile profile,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(
        string gameId,
        int limit,
        CancellationToken cancellationToken);

    Task MarkLastActiveAsync(
        string profileId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal interface IManagedPlayerAssignmentStore
{
    Task<bool> TryReserveAsync(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<bool> ReleaseAsync(
        string profileId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal static class ManagedPlayerAvailabilityPolicy
{
    private static readonly TimeSpan UsageWindow = TimeSpan.FromHours(8);
    private static readonly TimeSpan MaximumUsagePerWindow = TimeSpan.FromHours(2);

    public static bool IsAvailable(
        string profileId,
        DateTime nowUtc,
        DateTime windowStartedAtUtc,
        long activeSecondsInWindow,
        DateTime nextAvailableAtUtc)
    {
        if (nextAvailableAtUtc > nowUtc || IsSleeping(profileId, nowUtc)) return false;
        var used = nowUtc - windowStartedAtUtc >= UsageWindow ? 0 : activeSecondsInWindow;
        return used < MaximumUsagePerWindow.TotalSeconds;
    }

    internal static bool IsSleeping(string profileId, DateTime nowUtc)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"managed-schedule\n{profileId}"));
        var nightOwl = hash[0] % 4 == 0;
        var durationHours = 6 + hash[1] % 4;
        var startHourSouthAfrica = nightOwl
            ? 6 + hash[2] % 4
            : 21 + hash[2] % 4;
        var southAfricaTime = nowUtc.AddHours(2);
        var hour = southAfricaTime.Hour + southAfricaTime.Minute / 60d;
        var elapsed = (hour - startHourSouthAfrica + 24) % 24;
        return elapsed < durationHours;
    }

    internal static DateTime WindowStart(DateTime nowUtc, DateTime stored) =>
        stored == DateTime.UnixEpoch || nowUtc - stored >= UsageWindow ? nowUtc : stored;

    internal static long ActiveSeconds(DateTime nowUtc, DateTime windowStart, long stored, DateTime reservedAt) =>
        nowUtc - windowStart >= UsageWindow
            ? Math.Clamp((long)(nowUtc - reservedAt).TotalSeconds, 0, 3_600)
            : checked(stored + Math.Clamp((long)(nowUtc - reservedAt).TotalSeconds, 0, 3_600));

    internal static DateTime NextAvailable(string profileId, DateTime nowUtc)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"managed-break\n{profileId}\n{nowUtc:yyyyMMddHH}"));
        return nowUtc.AddMinutes(15 + hash[0] % 21);
    }
}

internal interface IManagedPlayerQueuer
{
    Task<IReadOnlyList<ManagedPlayerProfile>> ReserveAsync(
        string gameId,
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal static class ManagedPlayerIdentityFactory
{
    internal const int PlayerNameStyleCount = 20;

    private static readonly string[] First =
    [
        "Maya", "Noah", "Avery", "Milo", "Lena", "Theo", "Nina", "Eli",
        "Sasha", "Remy", "Zara", "Ivy", "Kai", "Nico", "Mara", "Jules",
        "Amara", "Diego", "Priya", "Ren", "Hana", "Omar", "Luca", "Mei",
        "Imani", "Arlo", "Leila", "Mateo", "Sora", "Talia", "Zane", "Esme"
    ];
    private static readonly string[] Last =
    [
        "River", "Stone", "Vale", "Brooks", "Lane", "Reed", "Hart", "Gray",
        "Quinn", "Blake", "Wells", "Fox", "Parker", "Rowan", "Lake", "Sage",
        "Chen", "Diaz", "Patel", "Kim", "Okafor", "Silva", "Khan", "Ito",
        "Morgan", "Bell", "Flores", "Young", "Shaw", "Moon", "Cruz", "West"
    ];
    private static readonly string[] HandleRoots =
    [
        "mossandmoon", "slowmeteor", "papercrane", "sundaystatic", "softsignal",
        "littleorbit", "teacupnova", "pixelmoth", "mapleghost", "cloudymaybe",
        "smallvictory", "afterhours", "foxglove", "bluejay", "coffeebreak",
        "sidequest", "nightwindow", "daydreamer", "northbound", "secondcoffee",
        "tinylantern", "velvetnoise", "rainsong", "goodomens", "wanderingstar",
        "wildflower", "latenight", "quietcorner", "weekendmode", "greentea"
    ];
    private static readonly string[] Adjectives =
    [
        "Amber", "Blue", "Cozy", "Electric", "Gentle", "Golden", "Hazy", "Lucky",
        "Mellow", "Midnight", "Quiet", "Rapid", "Silver", "Sleepy", "Sunny", "Velvet"
    ];
    private static readonly string[] Nouns =
    [
        "Badger", "Comet", "Fern", "Fox", "Garden", "Jay", "Lantern", "Maple",
        "Meteor", "Moth", "Orbit", "Pebble", "Raven", "River", "Sparrow", "Willow"
    ];

    public static ManagedPlayerProfile Create(string gameId, DateTime nowUtc)
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var seed = BitConverter.ToUInt64(bytes);
        return new(
            $"managed-{Guid.NewGuid():N}",
            CreatePlayerName(seed),
            2 + (int)((seed >> 24) % 3),
            new HashSet<string>(ManagedPlayerGames.All.Append(gameId), StringComparer.OrdinalIgnoreCase),
            nowUtc);
    }

    internal static string CreatePlayerName(ulong entropy)
    {
        var style = (int)(entropy % PlayerNameStyleCount);
        var state = entropy ^ 0x9E3779B97F4A7C15UL;
        var first = Pick(First, ref state);
        var last = Pick(Last, ref state);
        var handle = Pick(HandleRoots, ref state);
        var adjective = Pick(Adjectives, ref state);
        var noun = Pick(Nouns, ref state);
        var digit = 2 + NextIndex(ref state, 8);
        var threeDigits = 100 + NextIndex(ref state, 900);
        var firstInitial = char.ToLowerInvariant(first[0]);
        var lastInitial = char.ToLowerInvariant(last[0]);

        return style switch
        {
            0 => $"{first}{last}",
            1 => $"{first} {last}",
            2 => $"{first.ToLowerInvariant()}_{last.ToLowerInvariant()}",
            3 => $"{first}-{last}",
            4 => $"{char.ToUpperInvariant(first[0])}{last}",
            5 => $"{first}{char.ToUpperInvariant(last[0])}",
            6 => $"{first.ToLowerInvariant()}{lastInitial}",
            7 => $"its{first}",
            8 => $"just{first}",
            9 => handle,
            10 => Capitalize(handle),
            11 => $"{adjective.ToLowerInvariant()}{noun.ToLowerInvariant()}",
            12 => $"{adjective.ToLowerInvariant()}_{noun.ToLowerInvariant()}",
            13 => $"{adjective}-{noun}",
            14 => $"{Capitalize(handle)}{digit}",
            15 => $"{digit}{handle}",
            16 => $"{first.ToLowerInvariant()}_{noun.ToLowerInvariant()}",
            17 => $"{noun.ToLowerInvariant()}-{firstInitial}",
            18 => $"{firstInitial}_{noun.ToLowerInvariant()}{digit}",
            19 => $"{handle}_{threeDigits}",
            _ => throw new InvalidOperationException("Unknown managed-player name style.")
        };
    }

    private static string Pick(string[] values, ref ulong state) =>
        values[NextIndex(ref state, values.Length)];

    private static int NextIndex(ref ulong state, int count)
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return (int)((state * 2685821657736338717UL) % (ulong)count);
    }

    private static string Capitalize(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];
}
