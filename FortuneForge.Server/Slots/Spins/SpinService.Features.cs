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
        if (!SlotSpecialRoundProfiles.TryGet(game.Id, out var profile))
        {
            return outcome;
        }

        var reels = outcome.VisibleReels
            .Select(reel => reel.ToArray())
            .ToArray();

        if (SlotSpecialRoundProfiles.HasFeatureMode(freeSpinFeatureMode, SyncedReelsFeatureMode))
        {
            var sourceReel = random.Next(reels.Length);
            var destinationReel = (sourceReel + 1 + random.Next(reels.Length - 1)) % reels.Length;
            reels[destinationReel] = reels[sourceReel].ToArray();
        }

        if (profile.UsesDirectValueTokens &&
            SlotSpecialRoundProfiles.HasFeatureMode(freeSpinFeatureMode, RandColumnFeatureMode))
        {
            var reel = random.Next(reels.Length);
            for (var row = 0; row < reels[reel].Length; row++)
            {
                reels[reel][row] = PickRandSymbol();
            }
        }
        else if (profile.UsesDirectValueTokens)
        {
            var moneyCount = RollMoneySymbolCount(game.Id);
            InjectSymbols(reels, moneyCount, PickRandSymbol);
        }
        else if (SlotSpecialRoundProfiles.HasFeatureMode(freeSpinFeatureMode, RandColumnFeatureMode))
        {
            FillWildReel(reels);
        }

        if (profile.UsesDirectValueTokens)
        {
            InjectSymbols(reels, RollMonkeyPawCount(game.Id, freeSpinFeatureMode), () => MonkeyPawSymbolId);
        }
        else if (SlotSpecialRoundProfiles.HasFeatureMode(freeSpinFeatureMode, PawBoostFeatureMode))
        {
            InjectSymbols(reels, 2, () => "ACE");
        }
        InjectSymbols(reels, RollBananaCount(), () => BananaSymbolId);
        if (profile.UsesCollections)
        {
            InjectSymbols(reels, RollSealCount(game.Id, currentEnergyBalance), PickSealSymbol);
        }

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
        SlotSpecialRoundProfiles.TryGet(game.Id, out var profile);
        if (!SlotSpecialRoundProfiles.HasFeatureMode(freeSpinFeatureMode, ExtraRowsFeatureMode))
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
            FreeGames = game.FreeGames is null || profile is null
                ? game.FreeGames
                : profile.Configure(game.FreeGames),
            SpecialPoints = game.SpecialPoints,
            Energy = game.Energy,
            Paylines = game.Paylines
        };
    }

    private int RollMonkeyPawCount(string gameId, string? freeSpinFeatureMode)
    {
        if (SlotSpecialRoundProfiles.HasFeatureMode(freeSpinFeatureMode, PawBoostFeatureMode))
        {
            if (string.Equals(gameId, SlotSpecialRoundProfiles.ClassicGameId, StringComparison.Ordinal))
            {
                // Wukong's Monkey Paw Rush is a visible feature transformation,
                // not a small probability bump: every free spin is paw-heavy.
                return random.Next(4) + 2;
            }

            // Other themes reuse this internal mode for their own collection
            // feature and retain the math profile they were calibrated against.
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

    private int RollMoneySymbolCount(string gameId) =>
        GetMoneySymbolCountForRoll(gameId, random.Next(100));

    internal static int GetMoneySymbolCountForRoll(string gameId, int roll)
    {
        var normalizedRoll = Math.Clamp(roll, 0, 99);
        if (string.Equals(gameId, SlotSpecialRoundProfiles.PiratesFortuneGameId, StringComparison.Ordinal))
        {
            // Fifteen-gem chests and twice-common gems make Broadside Runs much
            // more frequent, so direct doubloon tokens stay deliberately rare.
            return normalizedRoll switch
            {
                < 91 => 0,
                < 99 => 1,
                _ => 2
            };
        }

        return normalizedRoll switch
        {
            < 20 => 0,
            < 80 => 1,
            < 96 => 2,
            _ => 3
        };
    }

    private int RollBananaCount() =>
        random.Next(100) switch
        {
            < 48 => 0,
            < 88 => 1,
            < 98 => 2,
            _ => 3
        };

    private int RollSealCount(string gameId, long currentEnergyBalance)
    {
        if (random.Next(100) >= GetSealAppearanceChance(gameId, currentEnergyBalance))
        {
            return 0;
        }

        return random.Next(25) == 0 ? 2 : 1;
    }

    internal static int GetSealCountForRoll(
        string gameId,
        long currentEnergyBalance,
        int appearanceRoll,
        int extraSealRoll)
    {
        if (Math.Clamp(appearanceRoll, 0, 99) >= GetSealAppearanceChance(gameId, currentEnergyBalance))
        {
            return 0;
        }

        return Math.Clamp(extraSealRoll, 0, 24) == 0 ? 2 : 1;
    }

    internal static int GetSealAppearanceChance(string gameId, long currentEnergyBalance)
    {
        if (string.Equals(gameId, SlotSpecialRoundProfiles.PiratesFortuneGameId, StringComparison.Ordinal))
        {
            // Pirates has no energy meter. Its 50% fixed gem chance keeps the
            // smaller fifteen-gem chests meaningfully faster than the previous
            // twenty-two-gem setup while keeping the feature RTP sustainable.
            return 50;
        }

        return currentEnergyBalance switch
        {
            >= 75 => 67,
            >= 50 => 50,
            >= 25 => 40,
            _ => 33
        };
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

    private void FillWildReel(IReadOnlyList<string[]> reels)
    {
        var reel = reels[random.Next(reels.Count)];
        for (var row = 0; row < reel.Length; row++)
        {
            reel[row] = "ACE";
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
