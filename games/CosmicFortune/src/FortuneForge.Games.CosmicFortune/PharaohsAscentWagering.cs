using System.Text.Json;
using System.Globalization;

namespace FortuneForge.Games.CosmicFortune;

public sealed record PharaohsAscentWageringDefinition(decimal PointValueInCents, long MinimumWagerPoints,
    long DefaultWagerPoints, long MaximumWagerPoints, long WagerIncrementPoints);

/// <summary>Current paid admission terms; previously funded rounds retain their frozen engine terms.</summary>
public static class PharaohsAscentWagering
{
    public static PharaohsAscentWageringDefinition Current { get; } = Load();

    public static void ValidateNewPaidWager(long wagerPoints)
    {
        if (!Allowed(Current, wagerPoints))
            throw new ArgumentOutOfRangeException(nameof(wagerPoints),
                $"Choose a bet from {Rand(Current.MinimumWagerPoints)} to {Rand(Current.MaximumWagerPoints)} in {Rand(Current.WagerIncrementPoints)} steps.");
    }

    private static PharaohsAscentWageringDefinition Load()
    {
        using var resource = typeof(PharaohsAscentWagering).Assembly.GetManifestResourceStream(
            "FortuneForge.Games.CosmicFortune.wagering.json") ?? throw new InvalidOperationException("The game wagering contract is missing.");
        var value = JsonSerializer.Deserialize<PharaohsAscentWageringDefinition>(resource,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("The game wagering contract is invalid.");
        var frozen = CosmicFortuneEngine.CreateGame().Wagering;
        if (value.PointValueInCents != frozen.PointValueInCents || value.MinimumWagerPoints < frozen.MinimumWagerPoints ||
            value.MaximumWagerPoints > frozen.MaximumWagerPoints || value.MaximumWagerPoints < value.MinimumWagerPoints ||
            value.WagerIncrementPoints <= 0 || !Allowed(value, value.DefaultWagerPoints))
            throw new InvalidOperationException("The paid wagering contract is incompatible with the frozen game model.");
        CosmicFortuneEngine.ValidateWager(value.MinimumWagerPoints, false);
        CosmicFortuneEngine.ValidateWager(value.MaximumWagerPoints, false);
        CosmicFortuneEngine.ValidateWager(value.DefaultWagerPoints, false);
        return value;
    }

    private static bool Allowed(PharaohsAscentWageringDefinition value, long points) =>
        points >= value.MinimumWagerPoints && points <= value.MaximumWagerPoints &&
        (points - value.MinimumWagerPoints) % value.WagerIncrementPoints == 0;

    private static string Rand(long points) => "R" +
        (points * Current.PointValueInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
