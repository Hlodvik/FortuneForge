using System.Globalization;
using FortuneForge.Games.ThreeCardPoker;

namespace FortuneForge.Server.Bots.ThreeCardPoker;

internal static class ThreeCardPokerManagedSeat
{
    private const string ManagedKey = "managed-profile";
    private const string SkillKey = "managed-skill";
    private const string BaseWagerKey = "managed-base-wager";
    private const string ChangeChanceKey = "managed-wager-change-chance";
    private const string ConsecutiveWinsKey = "managed-consecutive-wins";
    private const string ConsecutiveLossesKey = "managed-consecutive-losses";
    private const string LastNetKey = "managed-last-net";
    private const string DepartureRoundKey = "managed-departure-round";
    private const string ActionReadyAtKey = "managed-action-ready-at";
    private const string WagerReadyAtKey = "managed-wager-ready-at";

    public static Dictionary<string, string> Create(int skillLevel, long baseWager, int changeChance) =>
        new(StringComparer.Ordinal)
        {
            [ManagedKey] = bool.TrueString,
            [SkillKey] = Format(skillLevel),
            [BaseWagerKey] = Format(baseWager),
            [ChangeChanceKey] = Format(changeChance)
        };

    public static bool IsManaged(ThreeCardPokerTablePlayer player) =>
        (player.HostMetadata.TryGetValue(ManagedKey, out var value) &&
         bool.TryParse(value, out var managed) && managed) ||
        player.ActorId.StartsWith("managed-", StringComparison.Ordinal);

    public static int SkillLevel(ThreeCardPokerTablePlayer player) => GetInt(player, SkillKey, ManagedPlayerSkillLevels.Average);
    public static long BaseWager(ThreeCardPokerTablePlayer player) => GetLong(player, BaseWagerKey);
    public static void SetBaseWager(ThreeCardPokerTablePlayer player, long value) => Set(player, BaseWagerKey, value);
    public static int ChangeChance(ThreeCardPokerTablePlayer player) => GetInt(player, ChangeChanceKey, 600);
    public static int ConsecutiveWins(ThreeCardPokerTablePlayer player) => GetInt(player, ConsecutiveWinsKey);
    public static void SetConsecutiveWins(ThreeCardPokerTablePlayer player, int value) => Set(player, ConsecutiveWinsKey, value);
    public static int ConsecutiveLosses(ThreeCardPokerTablePlayer player) => GetInt(player, ConsecutiveLossesKey);
    public static void SetConsecutiveLosses(ThreeCardPokerTablePlayer player, int value) => Set(player, ConsecutiveLossesKey, value);
    public static void SetLastNet(ThreeCardPokerTablePlayer player, long value) => Set(player, LastNetKey, value);
    public static int? DepartureRound(ThreeCardPokerTablePlayer player) =>
        player.HostMetadata.TryGetValue(DepartureRoundKey, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    public static void SetDepartureRound(ThreeCardPokerTablePlayer player, int value) => Set(player, DepartureRoundKey, value);
    public static DateTime? ActionReadyAt(ThreeCardPokerTablePlayer player) => GetUtc(player, ActionReadyAtKey);
    public static void SetActionReadyAt(ThreeCardPokerTablePlayer player, DateTime value) => SetUtc(player, ActionReadyAtKey, value);
    public static void ClearActionReadyAt(ThreeCardPokerTablePlayer player) => player.HostMetadata.Remove(ActionReadyAtKey);
    public static DateTime? WagerReadyAt(ThreeCardPokerTablePlayer player) => GetUtc(player, WagerReadyAtKey);
    public static void SetWagerReadyAt(ThreeCardPokerTablePlayer player, DateTime value) => SetUtc(player, WagerReadyAtKey, value);
    public static void ClearWagerReadyAt(ThreeCardPokerTablePlayer player) => player.HostMetadata.Remove(WagerReadyAtKey);

    private static int GetInt(ThreeCardPokerTablePlayer player, string key, int fallback = 0) =>
        player.HostMetadata.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    private static long GetLong(ThreeCardPokerTablePlayer player, string key) =>
        player.HostMetadata.TryGetValue(key, out var value) &&
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static DateTime? GetUtc(ThreeCardPokerTablePlayer player, string key) =>
        player.HostMetadata.TryGetValue(key, out var value) &&
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    private static void Set(ThreeCardPokerTablePlayer player, string key, long value) =>
        player.HostMetadata[key] = Format(value);
    private static void SetUtc(ThreeCardPokerTablePlayer player, string key, DateTime value) =>
        player.HostMetadata[key] = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
