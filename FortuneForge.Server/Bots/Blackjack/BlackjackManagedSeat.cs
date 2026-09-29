using System.Globalization;
using FortuneForge.Games.Blackjack;
using FortuneForge.Games.Cards;

namespace FortuneForge.Server.Bots.Blackjack;

internal static class BlackjackManagedSeat
{
    private const string ManagedKey = "managed-profile";
    private const string SkillKey = "managed-skill";
    private const string BaseWagerKey = "managed-base-wager";
    private const string ChangeChanceKey = "managed-wager-change-chance";
    private const string ConsecutiveWinsKey = "managed-consecutive-wins";
    private const string ConsecutiveLossesKey = "managed-consecutive-losses";
    private const string LastNetKey = "managed-last-net";
    private const string DepartureRoundKey = "managed-departure-round";

    public static Dictionary<string, string> Create(int skillLevel, long baseWager, int changeChance) =>
        new(StringComparer.Ordinal)
        {
            [ManagedKey] = bool.TrueString,
            [SkillKey] = Format(skillLevel),
            [BaseWagerKey] = Format(baseWager),
            [ChangeChanceKey] = Format(changeChance)
        };

    public static bool IsManaged(BlackjackTablePlayer player) =>
        (player.HostMetadata.TryGetValue(ManagedKey, out var value) &&
         bool.TryParse(value, out var managed) && managed) ||
        player.ActorId.StartsWith("managed-", StringComparison.Ordinal);

    public static int SkillLevel(BlackjackTablePlayer player) => GetInt(player, SkillKey, CardBotSkillLevels.Average);
    public static long BaseWager(BlackjackTablePlayer player) => GetLong(player, BaseWagerKey);
    public static void SetBaseWager(BlackjackTablePlayer player, long value) => Set(player, BaseWagerKey, value);
    public static int ChangeChance(BlackjackTablePlayer player) => GetInt(player, ChangeChanceKey, 600);
    public static int ConsecutiveWins(BlackjackTablePlayer player) => GetInt(player, ConsecutiveWinsKey);
    public static void SetConsecutiveWins(BlackjackTablePlayer player, int value) => Set(player, ConsecutiveWinsKey, value);
    public static int ConsecutiveLosses(BlackjackTablePlayer player) => GetInt(player, ConsecutiveLossesKey);
    public static void SetConsecutiveLosses(BlackjackTablePlayer player, int value) => Set(player, ConsecutiveLossesKey, value);
    public static void SetLastNet(BlackjackTablePlayer player, long value) => Set(player, LastNetKey, value);
    public static int? DepartureRound(BlackjackTablePlayer player) =>
        player.HostMetadata.TryGetValue(DepartureRoundKey, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    public static void SetDepartureRound(BlackjackTablePlayer player, int value) => Set(player, DepartureRoundKey, value);

    private static int GetInt(BlackjackTablePlayer player, string key, int fallback = 0) =>
        player.HostMetadata.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    private static long GetLong(BlackjackTablePlayer player, string key) =>
        player.HostMetadata.TryGetValue(key, out var value) &&
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static void Set(BlackjackTablePlayer player, string key, long value) =>
        player.HostMetadata[key] = Format(value);
    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
}
