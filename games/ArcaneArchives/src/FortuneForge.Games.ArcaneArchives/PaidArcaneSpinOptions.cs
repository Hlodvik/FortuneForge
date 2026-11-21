using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.ArcaneArchives;

public sealed record PaidArcaneSpinOptions(string EngineVersion, bool TomeActive, PaidArcanePotion? Potion)
{
    private const string WireName = "arcane";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PaidArcaneSpinOptions? Read(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, ArcaneArchivesEngine.GameId, WireName);
        if (!request.GameOptions.TryGetValue(WireName, out var data)) return null;

        try
        {
            return data.Deserialize<PaidArcaneSpinOptions>(Json);
        }
        catch (JsonException error)
        {
            throw new ArgumentException("The Arcane admission options are invalid.", nameof(request), error);
        }
    }

    public PaidSlotOutcomeRequest WriteTo(PaidSlotOutcomeRequest request) =>
        request with
        {
            GameOptions = new(request.GameOptions, StringComparer.OrdinalIgnoreCase)
            {
                [WireName] = JsonSerializer.SerializeToElement(this, Json)
            }
        };
}

public sealed record PaidArcanePotion(long Id, string IngredientId, int Potency)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariantId { get; init; }
}
