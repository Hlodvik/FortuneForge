using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.GodsOfOlympus;

public sealed class GodsOfOlympusEngine
{
    public const string GameId = "gods-of-olympus-v1";
    public const string EngineVersion = OlympusTumbleRules.EngineVersion;
    public const string ClassicEngineVersion = "gods-of-olympus-classic-v1";
    public static SlotProfile Profile { get; } = new(GameId, 4, 6, 28, 6,
        UsesCollections: true, UsesEnergy: false, UsesDirectValueTokens: false,
        CollectionFeatureMode: "paw-rand");
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private readonly IGameRandomSource random;
    private readonly ClassicSlotEngine classic;

    public GodsOfOlympusEngine(IGameRandomSource random)
    {
        this.random = random ?? throw new ArgumentNullException(nameof(random));
        classic = new(Definitions, Profile, random, ClassicEngineVersion,
            new ClassicSlotEngineOptions { AllowFreeSpinCarryWager = true });
    }

    public static decimal WagerPointValueInCents => Definitions.Game.Wagering.PointValueInCents;
    public static long MinimumWagerPoints => Definitions.Game.Wagering.MinimumWagerPoints;
    public static void ValidateWager(long wagerPoints, bool isFreeSpin) =>
        ClassicSlotEngine.ValidateWager(Definitions.Game, wagerPoints, allowCarryWager: isFreeSpin);

    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false) => classic.Spin(wagerPoints, playerId,
        specialBoostApplied, currentEnergyBalance, featureMode, persistedPityMisses, isFreeSpin);

    public GodsOfOlympusTumbleSpinOutcome SpinTumble(long wagerPoints,
        bool showcase = false, string engineVersion = EngineVersion)
    {
        ValidateWager(wagerPoints, isFreeSpin: false);
        var draws = new List<int>();
        var outcome = OlympusTumbleRules.Resolve(engineVersion, wagerPoints, showcase, maximum =>
        {
            var value = random.Next(maximum);
            if (value < 0 || value >= maximum) throw new InvalidOperationException("Invalid random index.");
            draws.Add(value); return value;
        });
        return new(Guid.NewGuid(), GameId, engineVersion, wagerPoints, WagerPointValueInCents, outcome, draws,
            Definitions.CreateGame());
    }

    public static GameDefinition CreateGame() => Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => Definitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => Definitions.CreatePaytable();
}

public sealed record GodsOfOlympusTumbleSpinOutcome(Guid SpinId, string GameId,
    string EngineVersion, long WagerPoints, decimal PointValueInCents,
    OlympusTumbleOutcome Outcome, IReadOnlyList<int> DrawIndexes,
    GameDefinition BaseGame);
