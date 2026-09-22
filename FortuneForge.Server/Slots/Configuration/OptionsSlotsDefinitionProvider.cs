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

    private static GameDefinition ClonePrototype(GameDefinition source, SlotSpecialRoundProfile profile) => new()
    {
        Id = profile.GameId,
        Layout = source.Layout,
        Symbols = source.Symbols,
        Matching = source.Matching,
        Math = CloneMath(source.Math, profile),
        Wagering = source.Wagering,
        FreeGames = source.FreeGames is null ? null : profile.Configure(source.FreeGames),
        SpecialPoints = source.SpecialPoints,
        Energy = profile.UsesEnergy ? source.Energy : null,
        Paylines = source.Paylines
    };

    private static GameMathDefinition CloneMath(GameMathDefinition source, SlotSpecialRoundProfile profile) => new()
    {
        ReelSetId = profile.BaseReelSetId ?? source.ReelSetId,
        PaytableId = source.PaytableId,
        PaylinePayoutSteps = source.PaylinePayoutSteps,
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
