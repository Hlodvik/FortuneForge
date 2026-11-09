using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed class OptionsSlotsDefinitionProvider(IOptions<SlotsOptions> options) : ISlotsDefinitionProvider
{
    public GameDefinition? GetGame(string id)
    {
        if (id == SlotSpecialRoundProfiles.EmptySlotsGameId)
            return Games.EmptySlots.EmptySlotsEngine.CreateGame();
        if (id == NeonArcadeSlotDefinitions.GameId)
            return NeonArcadeSlotDefinitions.CreateGame();
        if (id == GoldFoundrySlotDefinitions.GameId)
            return GoldFoundrySlotDefinitions.CreateGame();
        var configured = options.Value.GameDefinitions
            .SingleOrDefault(game => string.Equals(game.Id, id, StringComparison.Ordinal));
        if (configured is not null)
        {
            return SlotSpecialRoundProfiles.TryGet(id, out var configuredProfile)
                ? ApplyProfile(configured, configuredProfile)
                : configured;
        }
        if (id == SlotSpecialRoundProfiles.WukongGameId ||
            !SlotSpecialRoundProfiles.TryGet(id, out var profile))
        {
            return null;
        }

        var template = options.Value.ClassicProfileTemplate;
        return template is null ? null : ApplyProfile(template, profile);
    }

    public SymbolSetDefinition? GetSymbolSet(string id) =>
        id == "empty-slots-symbols-v1"
            ? Games.EmptySlots.EmptySlotsEngine.CreateSymbolSet()
            : id == GoldFoundrySlotDefinitions.SymbolSetId
            ? GoldFoundrySlotDefinitions.CreateSymbolSet()
            : id == RainbowRealmSlotDefinitions.SymbolSetId
            ? RainbowRealmSlotDefinitions.CreateSymbolSet()
            : string.Equals(id, NeonArcadeSlotDefinitions.SymbolSetId, StringComparison.Ordinal)
            ? NeonArcadeSlotDefinitions.CreateSymbolSet()
            : string.Equals(id, MidnightVaultSlotDefinitions.SymbolSetId, StringComparison.Ordinal)
            ? MidnightVaultSlotDefinitions.CreateSymbolSet()
            : options.Value.SymbolSets.SingleOrDefault(set => string.Equals(set.Id, id, StringComparison.Ordinal));

    public ReelSetDefinition? GetReelSet(string id)
    {
        if (id == "empty-slots-reels-v1")
            return Games.EmptySlots.EmptySlotsEngine.CreateReelSet();
        if (id == ArcaneLightningCollector.ReelSetId)
        {
            var game = options.Value.GameDefinitions.SingleOrDefault(game =>
                game.Id == SlotSpecialRoundProfiles.ArcaneArchivesGameId) ?? options.Value.ClassicProfileTemplate;
            var source = game is null ? null : options.Value.ReelSets.SingleOrDefault(set => set.Id == game.Math.ReelSetId);
            return source is null ? null : ArcaneArchivesBaseReelSet.Create(source);
        }
        if (id == GoldFoundrySlotDefinitions.ReelSetId)
            return GoldFoundrySlotDefinitions.CreateReelSet();
        if (id == RainbowRealmSlotDefinitions.ReelSetId)
            return RainbowRealmSlotDefinitions.Reels;
        if (string.Equals(id, NeonArcadeSlotDefinitions.ReelSetId, StringComparison.Ordinal))
            return NeonArcadeSlotDefinitions.CreateReelSet();
        if (string.Equals(id, MidnightVaultBaseReelSet.Id, StringComparison.Ordinal))
            return MidnightVaultBaseReelSet.Create();
        if (string.Equals(id, MidnightVaultBaseReelSet.CurrentId, StringComparison.Ordinal))
            return MidnightVaultBaseReelSet.CreateCurrent();
        if (string.Equals(id, PiratesFortuneBaseReelSet.Id, StringComparison.Ordinal))
        {
            return PiratesFortuneBaseReelSet.Definition;
        }
        if (string.Equals(id, WukongBaseReelSet.Id, StringComparison.Ordinal))
        {
            var prototype = options.Value.GameDefinitions.SingleOrDefault(game =>
                string.Equals(game.Id, SlotSpecialRoundProfiles.WukongGameId, StringComparison.Ordinal));
            var source = prototype is null
                ? null
                : options.Value.ReelSets.SingleOrDefault(set =>
                    string.Equals(set.Id, prototype.Math.ReelSetId, StringComparison.Ordinal));
            return source is null ? null : WukongBaseReelSet.Create(source);
        }

        return options.Value.ReelSets.SingleOrDefault(set => string.Equals(set.Id, id, StringComparison.Ordinal));
    }

    public PaytableDefinition? GetPaytable(string id)
    {
        if (id == "empty-slots-paytable-v1")
            return Games.EmptySlots.EmptySlotsEngine.CreatePaytable();
        if (id == GoldFoundrySlotDefinitions.PaytableId)
            return GoldFoundrySlotDefinitions.CreatePaytable();
        if (id == RainbowRealmSlotDefinitions.PaytableId)
            return RainbowRealmSlotDefinitions.CreatePaytable();
        if (string.Equals(id, NeonArcadeSlotDefinitions.PaytableId, StringComparison.Ordinal))
            return NeonArcadeSlotDefinitions.CreatePaytable();
        if (string.Equals(id, MidnightVaultSlotDefinitions.PaytableId, StringComparison.Ordinal))
            return MidnightVaultSlotDefinitions.CreatePaytable();
        if (string.Equals(id, MidnightVaultSlotDefinitions.PreviousPaytableId, StringComparison.Ordinal))
            return MidnightVaultSlotDefinitions.CreatePreviousPaytable();
        if (string.Equals(id, MidnightVaultSlotDefinitions.LegacyPaytableId, StringComparison.Ordinal))
            return MidnightVaultSlotDefinitions.CreateLegacyPaytable();

        return options.Value.Paytables.SingleOrDefault(table => string.Equals(table.Id, id, StringComparison.Ordinal));
    }

    private static GameDefinition ApplyProfile(GameDefinition source, SlotSpecialRoundProfile profile)
    {
        var paylineIndexes = profile.PaylinePatternIds?
            .Select(patternId => patternId - 1)
            .ToArray();

        return new GameDefinition
        {
            Id = profile.GameId,
            Layout = paylineIndexes is null
                ? source.Layout
                : new GameLayoutDefinition
                {
                    ReelCount = source.Layout.ReelCount,
                    VisibleRows = source.Layout.VisibleRows,
                    PaylineCount = paylineIndexes.Length
                },
            Symbols = profile.GameId == SlotSpecialRoundProfiles.RainbowRealmGameId
                ? RainbowRealmSlotDefinitions.CreateSymbolRules(source.Symbols)
                : string.Equals(profile.GameId, SlotSpecialRoundProfiles.DragonHoardGameId, StringComparison.Ordinal)
                ? MidnightVaultSlotDefinitions.CreateSymbolRules(source.Symbols)
                : source.Symbols,
            Matching = source.Matching,
            Math = CloneMath(source.Math, profile, paylineIndexes),
            Wagering = source.Wagering,
            FreeGames = source.FreeGames is null || profile.ScatterAwardedSpins <= 0
                ? null
                : profile.Configure(source.FreeGames),
            SpecialPoints = source.SpecialPoints,
            Energy = profile.UsesEnergy ? source.Energy : null,
            Paylines = paylineIndexes is null
                ? source.Paylines
                : paylineIndexes.Select(index => source.Paylines[index]).ToList()
        };
    }

    private static GameMathDefinition CloneMath(
        GameMathDefinition source,
        SlotSpecialRoundProfile profile,
        IReadOnlyList<int>? paylineIndexes) => new()
    {
        ReelSetId = profile.BaseReelSetId ?? source.ReelSetId,
        PaytableId = profile.GameId == SlotSpecialRoundProfiles.RainbowRealmGameId
            ? RainbowRealmSlotDefinitions.PaytableId
            : string.Equals(profile.GameId, SlotSpecialRoundProfiles.DragonHoardGameId, StringComparison.Ordinal)
            ? MidnightVaultSlotDefinitions.PaytableId
            : source.PaytableId,
        PayoutMultiplier = profile.PayoutMultiplier,
        PaylinePayoutSteps = paylineIndexes is null
            ? source.PaylinePayoutSteps
            : paylineIndexes.Select(index => source.PaylinePayoutSteps[index]).ToList(),
        FiveMatchPityMissLimit = source.FiveMatchPityMissLimit,
        Targets = profile.TargetHitRate is { } targetHitRate
            ? new GameMathTargets
            {
                Rtp = source.Targets.Rtp,
                HitRate = targetHitRate,
                Volatility = source.Targets.Volatility
            }
            : source.Targets
    };
}
