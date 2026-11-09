using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using GameCollection = FortuneForge.Games.EmptySlots.Models.SlotCollectionProgress;
using GameEngine = FortuneForge.Games.EmptySlots.EmptySlotsEngine;
using GameRandom = FortuneForge.Games.EmptySlots.Reels.IRandomIndexSource;

namespace FortuneForge.Server.Slots.Games.EmptySlots;

// The host owns authentication, persisted entitlements and the wallet. This
// boundary adapts the game package's outcome to the existing audit envelope.
internal sealed class EmptySlotsEngine(IRandomIndexSource random)
{
    private static readonly ConditionalWeakTable<object, object> Contracts = new();
    private static readonly GameEngine Definitions = new();
    private readonly GameEngine engine = new(new RandomBridge(random));
    private readonly ConcurrentDictionary<string, PityState> pityStates = new(StringComparer.Ordinal);

    // Do not resolve this through the host profile catalog: that catalog reads
    // this property during its own initialization.
    internal static SlotSpecialRoundProfile Profile { get; } =
        MapContract<SlotSpecialRoundProfile>(Definitions.Profile);

    internal static GameDefinition CreateGame() => MapContract<GameDefinition>(Definitions.Definition);
    internal static ReelSetDefinition CreateReelSet() => MapContract<ReelSetDefinition>(Definitions.ReelSet);
    internal static SymbolSetDefinition CreateSymbolSet() => MapContract<SymbolSetDefinition>(Definitions.SymbolSet);
    internal static PaytableDefinition CreatePaytable() => MapContract<PaytableDefinition>(Definitions.Paytable);
    internal static void ValidateWager(long wagerPoints) => Definitions.ValidateWager(wagerPoints);

    internal static SlotSpecialRoundProgress SettleCollections(
        IReadOnlyList<SlotSealCollection> current,
        IReadOnlyDictionary<string, int> awarded,
        long wagerPoints)
    {
        var progress = Definitions.SettleCollections(
            current.Select(collection => new GameCollection(collection.SealId,
                collection.Count, collection.AverageWagerPoints, collection.RequiredCount)).ToArray(),
            awarded, wagerPoints);
        return new SlotSpecialRoundProgress(progress.FreeSpinsAwarded, progress.FeatureMode,
            progress.Collections.Select(collection => new SlotSealCollection(collection.TrackId,
                collection.Count, collection.AverageWagerPoints, collection.RequiredCount)).ToArray());
    }

    internal SpinResult Spin(long wagerPoints, string playerId, bool specialBoostApplied,
        long currentEnergyBalance, string? featureMode, int? persistedPityMisses, bool isFreeSpin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var pity = pityStates.GetOrAdd(playerId, static _ => new PityState());
        lock (pity.SyncRoot)
        {
            // Admitted paid spins carry persisted pity; the demo host historically
            // retains it in process between calls with no supplied count.
            var outcome = engine.Spin(wagerPoints, playerId, specialBoostApplied,
                persistedPityMisses ?? pity.Misses, featureMode,
                isFreeSpin || featureMode is not null, currentEnergyBalance);
            pity.Misses = outcome.ConsecutiveFiveMisses;
            var audit = outcome.EngineAudit
                ?? throw new InvalidOperationException("Empty Slots Game did not produce engine evidence.");
            var payout = new SpinPayout(outcome.Payout.TotalPoints,
                outcome.Payout.Paylines.Select(line => new PaylinePayout(line.PaylineId,
                    line.AmountPoints, line.Matches.Select(paid => new PaidMatch(
                        new SymbolMatch(paid.Match.PaylineId, paid.Match.SymbolId,
                            paid.Match.MatchLength,
                            paid.Match.Positions.Select(position =>
                                new GridPosition(position.Reel, position.Row)).ToArray(),
                            paid.Match.WildPositions.Select(position =>
                                new GridPosition(position.Reel, position.Row)).ToArray()),
                        paid.Multiplier, paid.AmountPoints)).ToArray())).ToArray());
            return new SpinResult(outcome.SpinId, outcome.GameId, outcome.ReelSetId,
                outcome.SymbolSetId, outcome.PaytableId, outcome.WagerPoints,
                outcome.PointValueInCents, outcome.ReelStops, outcome.Reels, payout,
                outcome.ConsecutiveFiveMisses, outcome.FiveMatchPityTriggered,
                outcome.FreeSpinsAwarded, outcome.SpecialPointsAwarded, outcome.EnergyAwarded,
                outcome.SpecialBoostApplied)
            {
                IsFreeSpin = isFreeSpin,
                MonkeyPawCount = outcome.CollectorCount,
                MoneyGrabPoints = outcome.CollectorPayoutPoints,
                BananaBonusPoints = outcome.PatternPayoutPoints,
                SealsAwarded = outcome.CollectionSymbolsAwarded,
                EngineAudit = new SlotSpinEngineAudit(audit.EngineVersion, audit.GeneratedAtUtc,
                    MapContract<GameDefinition>(audit.BaseGame),
                    MapContract<GameDefinition>(audit.EffectiveGame),
                    MapContract<ReelSetDefinition>(audit.ReelSet),
                    MapContract<SymbolSetDefinition>(audit.SymbolSet),
                    MapContract<PaytableDefinition>(audit.Paytable), Profile,
                    audit.PityMissesBefore, audit.EnergyBalanceBefore, audit.FreeSpinFeatureMode,
                    audit.GenerationAttempts, audit.RawGeneratedReels,
                    audit.ReelsAfterFreeGameFrequency)
                {
                    IsDerivedFreeSpin = audit.IsDerivedFreeSpin
                }
            };
        }
    }

    // The deployed host currently owns its DTOs; the package uses neutral slot
    // contracts. Cache structural conversions of frozen rule objects while
    // allowing ephemeral expanded-grid definitions to be collected.
    private static T MapContract<T>(object value) where T : class =>
        value is T compatible ? compatible : (T)Contracts.GetValue(value, static source =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source))
                ?? throw new InvalidOperationException($"Cannot map Empty Slots Game {typeof(T).Name}."));

    private sealed class RandomBridge(IRandomIndexSource source) : GameRandom
    {
        public int Next(int exclusiveMax) => source.Next(exclusiveMax);
    }

    private sealed class PityState
    {
        public object SyncRoot { get; } = new();
        public int Misses { get; set; }
    }
}
