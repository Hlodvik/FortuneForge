using System.Text.Json;
using System.Net;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

const int defaultPaidSpins = 250_000;
const int defaultSeed = 20_260_720;
const long wager = 100;
const int sealCompletionTarget = 40;
const int sealCompletionFreeSpins = 10;
string[] sealFeatureModes = ["sync", "rows", "paw", "rand"];
Dictionary<string, string> sealModesBySymbol = new(StringComparer.Ordinal)
{
    ["SEAL_SYNC"] = "sync",
    ["SEAL_ROWS"] = "rows",
    ["SEAL_PAW"] = "paw",
    ["SEAL_RAND"] = "rand"
};

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var configurationPath = args.FirstOrDefault(argument => argument.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    ?? Path.Combine(repositoryRoot, "FortuneForge.Server", "appsettings.json");
var paidSpinCount = args.Select(argument => int.TryParse(argument, out var parsed) ? parsed : 0)
    .FirstOrDefault(value => value > 0);
paidSpinCount = paidSpinCount > 0 ? paidSpinCount : defaultPaidSpins;

var root = JsonSerializer.Deserialize<RootConfiguration>(
    File.ReadAllText(configurationPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("The slot configuration could not be loaded.");
var validation = new SlotsOptionsValidator().Validate(null, root.Slots);
if (validation.Failed)
{
    throw new InvalidOperationException(
        "The slot configuration is invalid:\n- " + string.Join("\n- ", validation.Failures));
}
ValidateClientAddressParsing();

GameDefinition game = null!;
SymbolSetDefinition symbolSet = null!;
ReelSetDefinition reelSet = null!;
ReelSetDefinition boostedReelSet = null!;
PaytableDefinition paytable = null!;
CryptoReelGenerator generator = null!;
CombinationEvaluator evaluator = null!;
PayoutCalculator payoutCalculator = null!;
SpinService requestValidator = null!;
Statistics statistics = null!;
var specialPoints = 0;
var energyBalance = 0L;
var sealCounts = new Dictionary<string, long>(StringComparer.Ordinal);
var lastFreeSpinsAwarded = 0;
string? lastFreeSpinFeatureMode = null;

foreach (var configuredGame in root.Slots.GameDefinitions)
{
    game = configuredGame;
    symbolSet = root.Slots.SymbolSets.Single(set => set.Id == game.Symbols.SymbolSetId);
    reelSet = root.Slots.ReelSets.Single(set => set.Id == game.Math.ReelSetId);
    boostedReelSet = game.SpecialPoints is null
        ? reelSet
        : SpecialPointBonus.CreateBoostedReelSet(game, reelSet);
    paytable = root.Slots.Paytables.Single(table => table.Id == game.Math.PaytableId);
    var spinRandom = new SeededRandomIndexSource(defaultSeed);
    generator = new CryptoReelGenerator(spinRandom);
    evaluator = new CombinationEvaluator();
    payoutCalculator = new PayoutCalculator();
    requestValidator = new SpinService(
        new OptionsSlotsDefinitionProvider(Options.Create(root.Slots)),
        generator,
        evaluator,
        payoutCalculator,
        spinRandom);
    statistics = new Statistics(symbolSet.Symbols.Select(symbol => symbol.Id));
    specialPoints = 0;
    energyBalance = 0;
    sealCounts = sealFeatureModes.ToDictionary(mode => mode, _ => 0L, StringComparer.Ordinal);
    lastFreeSpinsAwarded = 0;
    lastFreeSpinFeatureMode = null;

    Console.WriteLine();
    Console.WriteLine($"Analyzing slot game: {game.Id}");
    ValidateServerWagerAllowlist();
    ValidateSpecialPointBoost();

    for (var paidSpin = 0; paidSpin < paidSpinCount; paidSpin++)
    {
        var cyclePayout = RunSpin(isFreeSpin: false, freeSpinFeatureMode: null);
        var freeSpinsPending = lastFreeSpinsAwarded;
        var freeSpinFeatureMode = lastFreeSpinFeatureMode;
        while (freeSpinsPending > 0)
        {
            freeSpinsPending--;
            cyclePayout = checked(cyclePayout + RunSpin(isFreeSpin: true, freeSpinFeatureMode));
            freeSpinsPending = checked(freeSpinsPending + lastFreeSpinsAwarded);
            freeSpinFeatureMode = lastFreeSpinFeatureMode ?? freeSpinFeatureMode;
        }

        statistics.RecordCycle(cyclePayout, wager);
    }

    PrintReport();
}

void ValidateServerWagerAllowlist()
{
    var supportedWagers = game.Wagering.AllowedWagerPoints.Count > 0
        ? game.Wagering.AllowedWagerPoints
        : Enumerable.Range(
                checked((int)game.Wagering.MinimumWagerPoints),
                checked((int)((game.Wagering.MaximumWagerPoints!.Value - game.Wagering.MinimumWagerPoints) /
                    game.Wagering.WagerIncrementPoints!.Value + 1)))
            .Select(index => game.Wagering.MinimumWagerPoints +
                (index - game.Wagering.MinimumWagerPoints) * game.Wagering.WagerIncrementPoints.Value)
            .ToList();
    foreach (var allowedWager in supportedWagers)
    {
        requestValidator.ValidateRequest(game.Id, allowedWager);
    }

    try
    {
        requestValidator.ValidateRequest(
            game.Id,
            checked((game.Wagering.MaximumWagerPoints ?? supportedWagers[^1]) + 1));
        throw new InvalidOperationException("The server accepted an off-menu wager.");
    }
    catch (ArgumentOutOfRangeException)
    {
        Console.WriteLine("PASS: server wager allowlist rejects off-menu wagers.");
    }
}

void ValidateClientAddressParsing()
{
    var directContext = new DefaultHttpContext();
    directContext.Connection.RemoteIpAddress = IPAddress.Loopback;
    directContext.Request.Headers["X-Forwarded-For"] = "192.0.2.25";
    if (ClientRequestIdentity.GetClientIpAddress(directContext) != "127.0.0.1")
    {
        throw new InvalidOperationException("A client-supplied forwarding address was trusted.");
    }

    var proxiedContext = new DefaultHttpContext();
    proxiedContext.Connection.RemoteIpAddress = IPAddress.Loopback;
    proxiedContext.Request.Headers["X-Forwarded-For"] =
        "192.0.2.25, 198.51.100.77, 203.0.113.5";
    if (ClientRequestIdentity.GetClientIpAddress(proxiedContext) != "198.51.100.77")
    {
        throw new InvalidOperationException("The trusted load-balancer client address was not selected.");
    }

    Console.WriteLine("PASS: spoofable forwarding-header prefixes are ignored.");
}

void ValidateSpecialPointBoost()
{
    if (game.SpecialPoints is not { } rules)
    {
        return;
    }

    for (var reelIndex = 0; reelIndex < reelSet.Reels.Count; reelIndex++)
    {
        foreach (var commonSymbolId in rules.CommonSymbolIds)
        {
            var ordinaryCount = reelSet.Reels[reelIndex].Count(symbol => symbol == commonSymbolId);
            var boostedCount = boostedReelSet.Reels[reelIndex].Count(symbol => symbol == commonSymbolId);
            if (boostedCount != ordinaryCount / 2)
            {
                throw new InvalidOperationException(
                    $"Boosted reel {reelIndex + 1} retained {boostedCount} copies of " +
                    $"'{commonSymbolId}' instead of {ordinaryCount / 2}.");
            }
        }
    }

    Console.WriteLine("PASS: the power boost halves every configured common symbol on every reel.");
}

long RunSpin(bool isFreeSpin, string? freeSpinFeatureMode)
{
    var specialBoostApplied = game.SpecialPoints is { } specialRules &&
        specialPoints >= specialRules.ActivationCost;
    if (specialBoostApplied)
    {
        specialPoints -= game.SpecialPoints!.ActivationCost;
    }
    var result = requestValidator.Spin(
        game.Id,
        wager,
        $"slot-math-{game.Id}",
        specialBoostApplied,
        energyBalance,
        freeSpinFeatureMode);
    var hasFullMatch = result.Payout.Paylines
        .SelectMany(payline => payline.Matches)
        .Any(match => match.Match.MatchLength == game.Layout.ReelCount);
    var meterBeforeReset = Math.Min(100, checked(energyBalance + result.EnergyAwarded));
    var energyMultiplierApplied = meterBeforeReset >= 100 && result.Payout.TotalPoints > 0;
    var payout = energyMultiplierApplied
        ? MultiplyPayout(result.Payout, 1.5m)
        : result.Payout;
    energyBalance = energyMultiplierApplied ? 0 : meterBeforeReset;
    var sealFreeSpinsAwarded = SettleSeals(result.SealsAwarded, energyMultiplierApplied);
    lastFreeSpinsAwarded = checked(result.FreeSpinsAwarded + sealFreeSpinsAwarded.FreeSpins);
    lastFreeSpinFeatureMode = sealFreeSpinsAwarded.FeatureMode;
    specialPoints = checked(specialPoints + result.SpecialPointsAwarded);
    statistics.RecordSpin(
        payout,
        hasFullMatch,
        result.FiveMatchPityTriggered,
        isFreeSpin,
        lastFreeSpinsAwarded,
        result.SpecialPointsAwarded,
        result.EnergyAwarded,
        specialBoostApplied,
        wager);
    return payout.TotalPoints;
}

(int FreeSpins, string? FeatureMode) SettleSeals(
    IReadOnlyDictionary<string, int> awardedSeals,
    bool energyCompleted)
{
    if (!string.Equals(game.Id, "classic-demo-v1", StringComparison.Ordinal))
    {
        return (0, null);
    }

    foreach (var awarded in awardedSeals)
    {
        if (sealModesBySymbol.TryGetValue(awarded.Key, out var mode) && awarded.Value > 0)
        {
            sealCounts[mode] = checked(sealCounts[mode] + awarded.Value);
        }
    }

    if (energyCompleted)
    {
        var nearestMode = sealFeatureModes
            .OrderByDescending(mode => Math.Min(sealCounts[mode], sealCompletionTarget - 1))
            .ThenBy(mode => Array.IndexOf(sealFeatureModes, mode))
            .First();
        sealCounts[nearestMode] = sealCompletionTarget;
    }

    var freeSpins = 0;
    string? featureMode = null;
    foreach (var mode in sealFeatureModes)
    {
        if (sealCounts[mode] < sealCompletionTarget)
        {
            continue;
        }

        freeSpins = checked(freeSpins + sealCompletionFreeSpins);
        featureMode ??= mode;
        sealCounts[mode] -= sealCompletionTarget;
    }

    return (freeSpins, featureMode);
}

SpinPayout MultiplyPayout(SpinPayout payout, decimal multiplier)
{
    var paylines = payout.Paylines.Select(payline =>
    {
        var matches = payline.Matches.Select(match => match with
        {
            AmountPoints = checked((long)Math.Round(
                match.AmountPoints * multiplier,
                MidpointRounding.AwayFromZero))
        }).ToArray();
        return payline with
        {
            AmountPoints = matches.Sum(match => match.AmountPoints),
            Matches = matches
        };
    }).ToArray();
    return payout with
    {
        TotalPoints = paylines.Sum(payline => payline.AmountPoints),
        Paylines = paylines
    };
}

void PrintReport()
{
    Console.WriteLine($"Configuration: {Path.GetFullPath(configurationPath)}");
    Console.WriteLine($"Seed: {defaultSeed:N0}; paid spins: {statistics.PaidSpins:N0}; total resolved spins: {statistics.TotalSpins:N0}");
    Console.WriteLine();
    Console.WriteLine("Configured reel frequency (before the visible FREE divisor):");
    foreach (var symbol in symbolSet.Symbols.Select(symbol => symbol.Id))
    {
        var counts = reelSet.Reels.Select(reel => reel.Count(value => value == symbol)).ToArray();
        Console.WriteLine($"  {symbol,-4} {string.Join(" / ", counts.Select(count => count.ToString().PadLeft(2)))}");
    }
    if (game.FreeGames is { VisibleFrequencyDivisor: > 1 } freeGames)
    {
        Console.WriteLine(
            $"  {freeGames.SymbolId} visible frequency: 1 in {freeGames.VisibleFrequencyDivisor} configured appearances");
    }

    Console.WriteLine();
    Console.WriteLine($"Overall RTP including free spins: {statistics.TotalPayout / (decimal)statistics.TotalPaidWager:P3}");
    Console.WriteLine($"Paid-spin payout RTP before free-spin value: {statistics.PaidSpinPayout / (decimal)statistics.TotalPaidWager:P3}");
    Console.WriteLine($"Resolved-spin hit rate: {statistics.HitSpins / (decimal)statistics.TotalSpins:P3}");
    Console.WriteLine($"Paid-cycle no-return rate: {statistics.ZeroCycles / (decimal)statistics.PaidSpins:P3}");
    Console.WriteLine($"Paid-cycle break-even rate: {statistics.BreakEvenCycles / (decimal)statistics.PaidSpins:P3}");
    Console.WriteLine($"Paid-cycle profitable rate: {statistics.ProfitCycles / (decimal)statistics.PaidSpins:P3}");
    Console.WriteLine($"Five-match rate: {statistics.FullMatchSpins / (decimal)statistics.TotalSpins:P3}");
    Console.WriteLine($"Forced-pity rate: {statistics.PitySpins / (decimal)statistics.TotalSpins:P3}");
    Console.WriteLine($"Payout from forced-pity spins: {statistics.PityPayout / (decimal)statistics.TotalPaidWager:P3} of paid wager");
    Console.WriteLine($"Free spins awarded per paid spin: {statistics.FreeSpinsAwarded / (decimal)statistics.PaidSpins:N4}");
    Console.WriteLine($"Special points awarded per paid spin: {statistics.SpecialPointsAwarded / (decimal)statistics.PaidSpins:N4}");
    Console.WriteLine($"Energy awarded per paid spin: {statistics.EnergyAwarded / (decimal)statistics.PaidSpins:N4}");
    Console.WriteLine($"Power-boosted resolved spins: {statistics.SpecialBoostSpins / (decimal)statistics.TotalSpins:P3}");
    Console.WriteLine($"Average return on a hit: {statistics.HitPayout / (decimal)Math.Max(1, statistics.HitSpins) / wager:N2}x wager");
    Console.WriteLine($"Largest resolved-spin payout: {statistics.MaxSpinPayout / (decimal)wager:N1}x wager");
    Console.WriteLine($"Largest paid-cycle payout including free games: {statistics.MaxCyclePayout / (decimal)wager:N1}x wager");

    Console.WriteLine();
    Console.WriteLine("Payout contribution by paid symbol:");
    foreach (var contribution in statistics.SymbolPayout.OrderBy(pair => pair.Value))
    {
        Console.WriteLine(
            $"  {contribution.Key,-4} {contribution.Value / (decimal)statistics.TotalPaidWager,8:P3} of paid wager " +
            $"({contribution.Value / (decimal)Math.Max(1, statistics.TotalPayout),7:P2} of payouts)");
    }

    Console.WriteLine();
    Console.WriteLine("Payout contribution by symbol and match length:");
    foreach (var contribution in statistics.MatchPayout.OrderBy(pair => pair.Key.Symbol).ThenBy(pair => pair.Key.Length))
    {
        Console.WriteLine(
            $"  {contribution.Key.Symbol,-4} x{contribution.Key.Length}: " +
            $"{contribution.Value / (decimal)statistics.TotalPaidWager,8:P3} of paid wager");
    }

    var bankroll = statistics.EstimateBankrollDepletion(wager, defaultSeed + 1);
    Console.WriteLine();
    Console.WriteLine(
        $"Bankroll trial (100 starting bets, up to 1,000 paid spins): {bankroll.RuinRate:P2} ran out; " +
        $"median ending balance {bankroll.MedianEndingBalance / (decimal)wager:N1} bets.");

    ValidateTargets(bankroll.RuinRate);
}

void ValidateTargets(decimal ruinRate)
{
    const decimal tolerance = 0.02m;
    var observedRtp = statistics.TotalPayout / (decimal)statistics.TotalPaidWager;
    var observedHitRate = statistics.HitSpins / (decimal)statistics.TotalSpins;
    var failures = new List<string>();

    if (observedRtp >= 1)
    {
        failures.Add($"RTP must remain below 100%, but measured {observedRtp:P3}.");
    }
    if (game.Math.Targets.Rtp is { } targetRtp && Math.Abs(observedRtp - targetRtp) > tolerance)
    {
        failures.Add($"RTP measured {observedRtp:P3}, outside target {targetRtp:P3} ± {tolerance:P0}.");
    }
    if (game.Math.Targets.HitRate is { } targetHitRate &&
        Math.Abs(observedHitRate - targetHitRate) > tolerance)
    {
        failures.Add(
            $"Hit rate measured {observedHitRate:P3}, outside target {targetHitRate:P3} ± {tolerance:P0}.");
    }
    if (ruinRate <= 0.5m)
    {
        failures.Add($"Only {ruinRate:P2} of bankroll trials depleted, below the required majority.");
    }

    if (failures.Count > 0)
    {
        throw new InvalidOperationException("Slot math target check failed:\n- " + string.Join("\n- ", failures));
    }

    Console.WriteLine();
    Console.WriteLine("PASS: configured RTP, hit-rate, and bankroll-depletion safeguards are satisfied.");
}
