using System.Text.Json;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed record PaidNeonSpinOptions(bool IsDiscoPlay, int? DiscoMultiplier)
{
    private const string IsDiscoPlayWireName = "isNeonDiscoPlay";
    private const string DiscoMultiplierWireName = "neonDiscoMultiplier";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PaidNeonSpinOptions Read(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(
            request, NeonArcadeSlotDefinitions.GameId, IsDiscoPlayWireName, DiscoMultiplierWireName);

        try
        {
            var isDiscoPlay = request.GameOptions.TryGetValue(IsDiscoPlayWireName, out var discoFlag)
                && discoFlag.Deserialize<bool>(Json);
            var multiplier = request.GameOptions.TryGetValue(DiscoMultiplierWireName, out var multiplierData)
                ? multiplierData.Deserialize<int?>(Json) : null;
            return new(isDiscoPlay, multiplier);
        }
        catch (JsonException error)
        {
            throw new ArgumentException("The Neon admission options are invalid.", nameof(request), error);
        }
    }

    public PaidSlotOutcomeRequest WriteTo(PaidSlotOutcomeRequest request) =>
        request with
        {
            GameOptions = new(request.GameOptions, StringComparer.OrdinalIgnoreCase)
            {
                [IsDiscoPlayWireName] = JsonSerializer.SerializeToElement(IsDiscoPlay, Json),
                [DiscoMultiplierWireName] = JsonSerializer.SerializeToElement(DiscoMultiplier, Json)
            }
        };
}
