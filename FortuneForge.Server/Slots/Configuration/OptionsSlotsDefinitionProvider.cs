using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed class OptionsSlotsDefinitionProvider(IOptions<SlotsOptions> options) : ISlotsDefinitionProvider
{
    public GameDefinition? GetGame(string id)
    {
        var configured = options.Value.GameDefinitions
            .SingleOrDefault(game => string.Equals(game.Id, id, StringComparison.Ordinal));
        if (configured is not null || !SlotSpecialRoundProfiles.TryGet(id, out var profile))
        {
            return configured;
        }

        var prototype = options.Value.GameDefinitions.SingleOrDefault(game =>
            string.Equals(game.Id, SlotSpecialRoundProfiles.ClassicGameId, StringComparison.Ordinal));
        return prototype is null ? null : ClonePrototype(prototype, profile);
    }

    public SymbolSetDefinition? GetSymbolSet(string id) =>
        options.Value.SymbolSets.SingleOrDefault(set => string.Equals(set.Id, id, StringComparison.Ordinal));

    public ReelSetDefinition? GetReelSet(string id) =>
        string.Equals(id, PiratesFortuneBaseReelSet.Id, StringComparison.Ordinal)
            ? PiratesFortuneBaseReelSet.Definition
            : options.Value.ReelSets.SingleOrDefault(set => string.Equals(set.Id, id, StringComparison.Ordinal));

    public PaytableDefinition? GetPaytable(string id) =>
        options.Value.Paytables.SingleOrDefault(table => string.Equals(table.Id, id, StringComparison.Ordinal));

    private static GameDefinition ClonePrototype(GameDefinition source, SlotSpecialRoundProfile profile)
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
            Symbols = source.Symbols,
            Matching = source.Matching,
            Math = CloneMath(source.Math, profile, paylineIndexes),
            Wagering = source.Wagering,
            FreeGames = source.FreeGames is null ? null : profile.Configure(source.FreeGames),
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
        PaytableId = source.PaytableId,
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
