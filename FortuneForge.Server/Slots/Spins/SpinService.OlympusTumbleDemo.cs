using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService
{
    internal SpinResult SpinOlympusTumbleDemo(long wagerPoints, bool showcase) =>
        SpinOlympusTumble(OlympusTumbleDemo.EngineVersion, wagerPoints, showcase,
            pityMissesBefore: 0, energyBalanceBefore: 0);

    private SpinResult SpinOlympusTumble(
        string engineVersion, long wagerPoints, bool showcase, int pityMissesBefore, long energyBalanceBefore)
    {
        const string gameId = SlotSpecialRoundProfiles.GodsOfOlympusGameId;
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        ValidateWager(game, wagerPoints);

        // Capture the registered game's wager rules. Tumbles have their own
        // versioned grid and payout rules, independent of the legacy reel strips.
        var symbolSet = definitions.GetSymbolSet(game.Symbols.SymbolSetId)
            ?? throw new KeyNotFoundException($"Symbol set '{game.Symbols.SymbolSetId}' was not found.");
        var reelSet = definitions.GetReelSet(game.Math.ReelSetId)
            ?? throw new KeyNotFoundException($"Reel set '{game.Math.ReelSetId}' was not found.");
        var paytable = definitions.GetPaytable(game.Math.PaytableId)
            ?? throw new KeyNotFoundException($"Paytable '{game.Math.PaytableId}' was not found.");
        var drawIndexes = new List<int>();
        var outcome = OlympusTumbleDemo.Resolve(engineVersion, wagerPoints, showcase, maximum =>
        {
            var index = random.Next(maximum);
            drawIndexes.Add(index);
            return index;
        });

        return new SpinResult(
            Guid.NewGuid(), gameId, engineVersion, symbolSet.Id,
            engineVersion, wagerPoints, game.Wagering.PointValueInCents,
            new int[outcome.InitialReels.Count], outcome.InitialReels, new SpinPayout(outcome.TotalPoints, []),
            ConsecutiveFiveMisses: pityMissesBefore, FiveMatchPityTriggered: false, FreeSpinsAwarded: 0,
            SpecialPointsAwarded: 0, EnergyAwarded: 0, SpecialBoostApplied: false)
        {
            OlympusTumble = outcome,
            EngineAudit = new SlotSpinEngineAudit(
                engineVersion, DateTime.UtcNow, game, game,
                reelSet, symbolSet, paytable, null, pityMissesBefore, energyBalanceBefore, null, 1,
                outcome.InitialReels, outcome.InitialReels)
            {
                OlympusTumble = new OlympusTumbleEvidence(showcase, drawIndexes.ToArray())
            }
        };
    }
}
