using System.Collections.Concurrent;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService
{
    private ReelOutcome ApplyFeatureSymbols(
        ReelOutcome outcome,
        GameDefinition game,
        long currentEnergyBalance,
        string? freeSpinFeatureMode)
    {
        if (!string.Equals(game.Id, WukongGameId, StringComparison.Ordinal))
        {
            return outcome;
        }

        var reels = outcome.VisibleReels
            .Select(reel => reel.ToArray())
            .ToArray();

        if (string.Equals(freeSpinFeatureMode, SyncedReelsFeatureMode, StringComparison.Ordinal))
        {
            var sourceReel = random.Next(reels.Length);
            var destinationReel = (sourceReel + 1 + random.Next(reels.Length - 1)) % reels.Length;
            reels[destinationReel] = reels[sourceReel].ToArray();
        }

        if (string.Equals(freeSpinFeatureMode, RandColumnFeatureMode, StringComparison.Ordinal))
        {
            var reel = random.Next(reels.Length);
            for (var row = 0; row < reels[reel].Length; row++)
            {
                reels[reel][row] = PickRandSymbol();
            }
        }
        else
        {
            var moneyCount = RollMoneySymbolCount();
            InjectSymbols(reels, moneyCount, PickRandSymbol);
        }

        InjectSymbols(reels, RollMonkeyPawCount(freeSpinFeatureMode), () => MonkeyPawSymbolId);
        InjectSymbols(reels, RollBananaCount(), () => BananaSymbolId);
        InjectSymbols(reels, RollSealCount(currentEnergyBalance), PickSealSymbol);

        return outcome with
        {
            VisibleReels = reels
                .Select(reel => (IReadOnlyList<string>)reel)
                .ToArray()
        };
    }

    private static GameDefinition CreateEffectiveSpinGame(
        GameDefinition game,
        string? freeSpinFeatureMode)
    {
        if (!string.Equals(freeSpinFeatureMode, ExtraRowsFeatureMode, StringComparison.Ordinal))
        {
            return game;
        }

        return new GameDefinition
        {
            Id = game.Id,
            Layout = new GameLayoutDefinition
            {
                ReelCount = game.Layout.ReelCount,
                VisibleRows = checked(game.Layout.VisibleRows + 2),
                PaylineCount = game.Layout.PaylineCount
            },
            Symbols = game.Symbols,
            Matching = game.Matching,
            Math = game.Math,
            Wagering = game.Wagering,
            FreeGames = game.FreeGames,
            SpecialPoints = game.SpecialPoints,
            Energy = game.Energy,
            Paylines = game.Paylines
        };
    }

    private int RollMonkeyPawCount(string? freeSpinFeatureMode)
    {
        if (string.Equals(freeSpinFeatureMode, PawBoostFeatureMode, StringComparison.Ordinal))
        {
            return random.Next(6) switch
            {
                0 => 2,
                1 => 1,
                _ => 0
            };
        }

        if (random.Next(777) == 0)
        {
            return 2;
        }

        return random.Next(14) == 0 ? 1 : 0;
    }

    private int RollMoneySymbolCount() =>
        random.Next(100) switch
        {
            < 20 => 0,
            < 80 => 1,
            < 96 => 2,
            _ => 3
        };

    private int RollBananaCount() =>
        random.Next(100) switch
        {
            < 48 => 0,
            < 88 => 1,
            < 98 => 2,
            _ => 3
        };

    private int RollSealCount(long currentEnergyBalance)
    {
        var chance = currentEnergyBalance switch
        {
            >= 75 => 67,
            >= 50 => 50,
            >= 25 => 40,
            _ => 33
        };

        if (random.Next(100) >= chance)
        {
            return 0;
        }

        return random.Next(25) == 0 ? 2 : 1;
    }

    private void InjectSymbols(
        IReadOnlyList<string[]> reels,
        int count,
        Func<string> symbolFactory)
    {
        for (var index = 0; index < count; index++)
        {
            var position = PickReplacementPosition(reels);
            if (position is null)
            {
                return;
            }

            reels[position.Value.Reel][position.Value.Row] = symbolFactory();
        }
    }

    private GridPosition? PickReplacementPosition(IReadOnlyList<string[]> reels)
    {
        var candidates = reels
            .SelectMany((reel, reelIndex) => reel.Select((symbol, rowIndex) =>
                new { Symbol = symbol, Position = new GridPosition(reelIndex, rowIndex) }))
            .Where(candidate => CommonReplacementSymbols.Contains(candidate.Symbol, StringComparer.Ordinal))
            .Select(candidate => candidate.Position)
            .ToArray();

        return candidates.Length == 0 ? null : candidates[random.Next(candidates.Length)];
    }

    private string PickRandSymbol() =>
        random.Next(100) switch
        {
            < 28 => "RAND_05",
            < 55 => "RAND_1",
            < 74 => "RAND_15",
            < 88 => "RAND_2",
            < 96 => "RAND_3",
            < 99 => "RAND_4",
            _ => "RAND_5"
        };

    private string PickSealSymbol() => SealSymbolIds[random.Next(SealSymbolIds.Length)];
}
