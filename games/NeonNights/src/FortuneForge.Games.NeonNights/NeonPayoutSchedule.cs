using System.Text.Json;

namespace FortuneForge.Games.NeonNights;

internal static class NeonPayoutSchedule
{
    private static readonly JsonElement Schedule = Load();

    internal static decimal BaseLineScale => Schedule.GetProperty("base").GetDecimal();
    internal static decimal CarLineScale => Schedule.GetProperty("car").GetDecimal();
    internal static long DiscoFourSymbolAward(string symbolId) =>
        Schedule.GetProperty("discoFourSymbolAwards").GetProperty(symbolId).GetInt64();

    private static JsonElement Load()
    {
        using var source = typeof(NeonPayoutSchedule).Assembly
            .GetManifestResourceStream("NeonNights.PayoutSchedule.v15.json")
            ?? throw new InvalidOperationException("The Neon Nights payout schedule is missing.");
        using var schedule = JsonDocument.Parse(source);
        return schedule.RootElement.Clone();
    }
}
