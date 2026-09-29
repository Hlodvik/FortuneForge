using System.Globalization;
using FortuneForge.Games.Blackjack;

namespace FortuneForge.Server.Bots.Blackjack;

internal static class BlackjackManagedTablePolicy
{
    public const int MinimumStartOccupancy = 3;
    public const int MaximumOccupiedSeats = BlackjackTableEngine.Capacity - 1;
    public const int DisconnectedTableRounds = 10;
    public const int FirstPopulationChangeAfterRounds = 10;
    public const int PopulationChangeIntervalRounds = 5;
}

internal static class BlackjackManagedTableState
{
    private const string RoundsWithoutHumanKey = "managed-rounds-without-human";
    private const string NextPopulationRoundKey = "managed-next-population-round";
    private const string NextPopulationAddsKey = "managed-next-population-adds";

    public static int RoundsRemainingWithoutHuman(BlackjackTableState table) =>
        GetInt(table, RoundsWithoutHumanKey);

    public static void SetRoundsRemainingWithoutHuman(BlackjackTableState table, int value) =>
        Set(table, RoundsWithoutHumanKey, value);

    public static int? NextPopulationChangeRound(BlackjackTableState table) =>
        table.HostMetadata.TryGetValue(NextPopulationRoundKey, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    public static void SetNextPopulationChangeRound(BlackjackTableState table, int? value)
    {
        if (value is null) table.HostMetadata.Remove(NextPopulationRoundKey);
        else Set(table, NextPopulationRoundKey, value.Value);
    }

    public static bool NextPopulationChangeAddsPlayer(BlackjackTableState table) =>
        !table.HostMetadata.TryGetValue(NextPopulationAddsKey, out var value) ||
        !bool.TryParse(value, out var parsed) || parsed;

    public static void SetNextPopulationChangeAddsPlayer(BlackjackTableState table, bool value) =>
        table.HostMetadata[NextPopulationAddsKey] = value.ToString(CultureInfo.InvariantCulture);

    private static int GetInt(BlackjackTableState table, string key) =>
        table.HostMetadata.TryGetValue(key, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static void Set(BlackjackTableState table, string key, int value) =>
        table.HostMetadata[key] = value.ToString(CultureInfo.InvariantCulture);
}
