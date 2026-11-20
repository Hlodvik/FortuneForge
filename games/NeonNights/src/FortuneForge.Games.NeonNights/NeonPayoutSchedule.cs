using System.Text.Json;

namespace FortuneForge.Games.NeonNights;

internal static class NeonPayoutSchedule
{
    private static readonly JsonElement Schedule = Load("v20");
    private static readonly JsonElement PreviousSchedule = Load("v15");

    internal static decimal BaseLineScale => Schedule.GetProperty("base").GetDecimal();
    internal static decimal CarLineScale => Schedule.GetProperty("car").GetDecimal();
    internal static decimal PreviousCarLineScale => PreviousSchedule.GetProperty("car").GetDecimal();
    internal static decimal MinimumCombinedRtp => Schedule.GetProperty("combinedRtpTarget").GetProperty("minimum").GetDecimal();
    internal static decimal MaximumCombinedRtp => Schedule.GetProperty("combinedRtpTarget").GetProperty("maximum").GetDecimal();
    internal static long DiscoFourSymbolAward(string symbolId) =>
        Schedule.GetProperty("discoFourSymbolAwards").GetProperty(symbolId).GetInt64();

    private static JsonElement Load(string version)
    {
        using var source = typeof(NeonPayoutSchedule).Assembly
            .GetManifestResourceStream($"NeonNights.PayoutSchedule.{version}.json")
            ?? throw new InvalidOperationException("The Neon Nights payout schedule is missing.");
        using var schedule = JsonDocument.Parse(source);
        return schedule.RootElement.Clone();
    }
}
