using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;
using FortuneForge.MidnightAudit;

var arguments = args.Select((value, index) => (value, index))
    .Where(pair => pair.value.StartsWith("--", StringComparison.Ordinal))
    .ToDictionary(pair => pair.value, pair => pair.index + 1 < args.Length && !args[pair.index + 1].StartsWith("--") ? args[pair.index + 1] : "true");
var root = Path.GetFullPath(arguments.GetValueOrDefault("--root", Path.Combine(AppContext.BaseDirectory, "../../../../..")));
var configurationPath = Path.Combine(root, "FortuneForge.Server/appsettings.json");
using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
var options = document.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidOperationException("Missing Slots options.");
var underlying = new OptionsSlotsDefinitionProvider(Options.Create(options));
var actualGame = underlying.GetGame(Definitions.GameId) ?? throw new InvalidOperationException("Midnight game missing.");
if (actualGame.Symbols.SymbolSetId != "midnight-vault-symbols-v1") throw new InvalidOperationException("Dedicated fifteen-symbol Midnight definition is required.");
if (!SlotSpecialRoundProfiles.TryGet(Definitions.GameId, out var profile)) throw new InvalidOperationException("Midnight profile missing.");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var loadedServerPath = typeof(FortuneForge.Server.Slots.Spins.SpinService).Assembly.Location;
var loadedServerHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loadedServerPath)));
if (arguments.TryGetValue("--require-server-sha", out var requiredServerHash) && loadedServerHash != requiredServerHash)
    throw new InvalidOperationException("Loaded server DLL does not match the coordinated engine handoff.");
var runtimeEngine = new { assemblyPath = loadedServerPath, sha256 = loadedServerHash,
    bytes = new FileInfo(loadedServerPath).Length };
string[] SourceFiles() => Directory.EnumerateFiles(Path.Combine(root, "FortuneForge.Server/Slots"), "*.cs", SearchOption.AllDirectories)
    .Concat(Directory.EnumerateFiles(Path.Combine(root, "tools/FortuneForge.MidnightVaultMath"), "*.cs", SearchOption.TopDirectoryOnly))
    .Append(configurationPath).Append(Path.Combine(root, "FortuneForge.Server/Controllers/DemoSlotsController.cs"))
    .Append(Path.Combine(root, "FortuneForge.Server.Tests/Slots/MidnightVaultIntegerPayoutTests.cs"))
    .Append(Path.Combine(root, "tools/FortuneForge.MidnightVaultMath/FortuneForge.MidnightVaultMath.csproj"))
    .OrderBy(path => path, StringComparer.Ordinal).ToArray();
Dictionary<string, string> Hashes() => SourceFiles().ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'),
    path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
bool EngineAssemblyStable() => loadedServerHash == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(loadedServerPath)));
var hashesBefore = Hashes();
var contracts = ContractExport.Create(underlying, profile, hashesBefore);
if (arguments.TryGetValue("--contract", out var contractPath)) Write(contractPath, contracts);
if (arguments.ContainsKey("--contract-only")) return;
if (arguments.ContainsKey("--integer-proof"))
{
    var allWagers = IntegerPayoutOracle.AllWagers(underlying);
    var queueArithmetic = IntegerPayoutOracle.QueueArithmetic(underlying);
    var coupledCycles = int.Parse(arguments.GetValueOrDefault("--coupled-cycles", "2000"), CultureInfo.InvariantCulture);
    var coupling = new List<object>();
    foreach (var seed in arguments.GetValueOrDefault("--seeds", "20260720,20260721").Split(',').Select(int.Parse))
    foreach (var boost in new[] { false, true })
        coupling.Add(IntegerCoupledCycles.Run(underlying, profile, coupledCycles, seed, boost));
    var after = Hashes();
    var unchanged = hashesBefore.Count == after.Count && hashesBefore.All(pair => after.GetValueOrDefault(pair.Key) == pair.Value);
    var assemblyStable = EngineAssemblyStable();
    if (arguments.TryGetValue("--out", out var proofPath)) Write(proofPath, new
    {
        schemaVersion = "midnight-vault-integer-homogeneity-v1", gameId = Definitions.GameId,
        runtimeEngine, engineAssemblyStable = assemblyStable, allWagers, queueArithmetic, coupling, sourceStable = unchanged, sourceHashesBefore = hashesBefore, sourceHashesAfter = after,
        scope = "Fixed cash wagers with homogeneous initial state; exact weighted queue arithmetic and derived wager admission are separate explicit checks. No Firestore or wallet transactions.",
        proof = "Positive integer coefficients at scalar1 preserve candidate filtering, amount/tie ordering and geometry dedup for every positive wager. Disabled energy/direct-token paths are guarded. RNG/pity/free/POWER decisions do not read wager. Contiguous cycles use the real FIFO round helper and are coupled at2/100/2000; exact oracle covers all1000 cash wagers and all1999 possible derived bonus wagers."
    });
    if (!unchanged || !assemblyStable) throw new InvalidOperationException("Source or server assembly changed during integer proof.");
    Console.WriteLine("PASS: all 1000 admitted wagers match independent arithmetic/geometry and coupled complete cycles.");
    return;
}

var cycles = int.Parse(arguments.GetValueOrDefault("--cycles", "10000"), CultureInfo.InvariantCulture);
if (cycles <= 0) throw new ArgumentOutOfRangeException(nameof(cycles));
var seeds = arguments.GetValueOrDefault("--seeds", "20260720").Split(',').Select(int.Parse).ToArray();
var multipliers = arguments.GetValueOrDefault("--multipliers", actualGame.Math.PayoutMultiplier.ToString(CultureInfo.InvariantCulture))
    .Split(',').Select(value => decimal.Parse(value, CultureInfo.InvariantCulture)).ToArray();
if (multipliers.Any(value => value != actualGame.Math.PayoutMultiplier))
    throw new InvalidOperationException("Queued round models pin their own scalar. Fractional scalar overrides are no longer a valid calibration path; use --integer-search.");
var wagers = arguments.GetValueOrDefault("--wagers", "100").Split(',');
var boostModes = arguments.GetValueOrDefault("--boost", "auto") is "both" ? new[] { false, true }
    : new[] { arguments.GetValueOrDefault("--boost", "auto") == "auto" };
var started = DateTime.UtcNow;
var results = new List<CalibrationMeasurement>();
foreach (var multiplier in multipliers)
foreach (var seed in seeds)
foreach (var wager in wagers)
foreach (var autoBoost in boostModes)
{
    var schedule = wager == "mixed" ? new long[] { 2, 6, 20, 100, 2000, 100, 20, 6 }
        : new[] { long.Parse(wager, CultureInfo.InvariantCulture) };
    var provider = new Definitions(underlying, multiplier);
    var result = CalibrationRunner.Run(provider, profile, schedule, cycles, seed, autoBoost, arguments.ContainsKey("--integer-search"));
    results.Add(result);
    Console.WriteLine($"{wager} points; multiplier {multiplier}; seed {seed}; boost {autoBoost}: RTP {result.Rtp:P4}; hit {result.HitRate:P3}; letters {result.CombinedLetterVisibleShare:P3}; total spins {result.TotalSpins}; pass {result.PolicyPass}");
}
var hashesAfter = Hashes();
var stable = hashesBefore.Count == hashesAfter.Count && hashesBefore.All(pair => hashesAfter.GetValueOrDefault(pair.Key) == pair.Value);
var serverAssemblyStable = EngineAssemblyStable();
var report = new
{
    schemaVersion = "midnight-vault-queued-calibration-v2", gameId = Definitions.GameId, startedUtc = started, finishedUtc = DateTime.UtcNow,
    engineVersion = FortuneForge.Server.Slots.Spins.SpinService.AuditEngineVersion,
    runtimeEngine, engineAssemblyStable = serverAssemblyStable,
    scope = "Headless real SpinService.SpinAdmitted with pure MidnightVaultBonusRounds queue and exact weighted gem totals. No account store, wallet, HTTP, or other games are exercised.",
    policy = new { rtpTarget = actualGame.Math.Targets.Rtp, hitRateTarget = actualGame.Math.Targets.HitRate, tolerance = 0.02m,
        rtpMustBeBelow = 1m, visibleLetterShareTarget = .39, visibleLetterShareTolerance = .02,
        lettersTopFourIsDescriptiveOnly = true, source = "Existing SlotMath RTP/hit/ruin rules; current user-directed letter share39% +/-2pp" },
    featureCarry = "Exact gem wager-point totals, pity, special points and FIFO rounds persist between paid cycles. Every awarded round locks its own wager/mode/model. All bonus spins and retriggers resolve before the next paid cycle.",
    mixedStakeLimit = "Mixed stakes use the same pure weighted-total/round-queue helper as paid settlement, including odd derived wagers via SpinAdmitted. This headless run does not exercise Firestore admission/idempotency/wallet transactions.",
    confidence = "Approximate 95% interval: mean of up to 50 contiguous batch RTPs +/- 2 standard errors; feature-state correlation is retained within batches. Not an exact mathematical RTP proof.",
    sourceStable = stable, sourceHashesBefore = hashesBefore, sourceHashesAfter = hashesAfter, results,
    integerSearchSelection = arguments.ContainsKey("--integer-search") ? IntegerSearchSelection.Create(results) : null
};
if (arguments.TryGetValue("--out", out var outputPath)) Write(outputPath, report);
if (!stable || !serverAssemblyStable) throw new InvalidOperationException("Source or server assembly changed while measuring; results are not a final calibration.");
if (arguments.ContainsKey("--enforce") && results.Any(result => !result.PolicyPass)) Environment.ExitCode = 2;
if (arguments.ContainsKey("--enforce-intervals") && results.Any(result => !result.RtpIntervalInsidePolicy)) Environment.ExitCode = 2;

void Write(string path, object value)
{
    var target = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target, JsonSerializer.Serialize(value, json) + Environment.NewLine);
}

internal sealed class Definitions(ISlotsDefinitionProvider source, decimal multiplier) : ISlotsDefinitionProvider
{
    public const string GameId = "dragon-hoard-v1";
    public GameDefinition? GetGame(string id)
    {
        var game = source.GetGame(id);
        if (game is null || id != GameId) return game;
        return new GameDefinition
        {
            Id = game.Id, Layout = game.Layout, Symbols = game.Symbols, Matching = game.Matching, Wagering = game.Wagering,
            FreeGames = game.FreeGames, SpecialPoints = game.SpecialPoints, Energy = game.Energy, Paylines = game.Paylines,
            Math = new GameMathDefinition { ReelSetId = game.Math.ReelSetId, PaytableId = game.Math.PaytableId,
                PayoutMultiplier = multiplier, PaylinePayoutSteps = game.Math.PaylinePayoutSteps,
                FiveMatchPityMissLimit = game.Math.FiveMatchPityMissLimit, Targets = game.Math.Targets }
        };
    }
    public SymbolSetDefinition? GetSymbolSet(string id) => source.GetSymbolSet(id);
    public ReelSetDefinition? GetReelSet(string id) => source.GetReelSet(id);
    public PaytableDefinition? GetPaytable(string id) => source.GetPaytable(id);
}
