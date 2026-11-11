using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FortuneForge.Games.Slots;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

if (args.Length != 2) throw new ArgumentException("Usage: exporter <repository-root> <new-output-directory>");
var repository = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var compiledAssembly = typeof(SpinService).Assembly.Location;

var configuration = Path.Combine(repository, "FortuneForge.Server/appsettings.json");
using var configurationDocument = JsonDocument.Parse(File.ReadAllText(configuration));
var options = configurationDocument.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(json)!;
var validation = new SlotsOptionsValidator().Validate(null, options);
Require(!validation.Failed, string.Join("\n", validation.Failures ?? []));
var definitions = new OptionsSlotsDefinitionProvider(Options.Create(options));
var routeFile = "fortuneforge.client/src/games/slots/routeRegistry.ts";
var routesSource = File.ReadAllText(Path.Combine(repository, routeFile));
var routePattern = "id: '([^']+)', title: (?:'([^']*)'|\"([^\"]*)\"),.*?playPath: '([^']+)', demoPath: '([^']+)', serverGameIds: \\['([^']+)'\\]";
var routes = Regex.Matches(routesSource, routePattern, RegexOptions.Singleline).Select(match =>
    new Route(match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value,
        match.Groups[4].Value, match.Groups[5].Value, match.Groups[6].Value)).ToArray();
// Jade Dragon is a blank handoff surface, outside the playable slot registry.
Require(routes.Length == 19 && routes.Select(route => route.GameId).Distinct().Count() == 19, "Actual route registry must supply exactly19 unique playable profiles.");
Require(routes.All(route => SlotSpecialRoundProfiles.TryGet(route.GameId, out _)), "Catalogue profile missing.");
var sourceDirectories = new[]
{
    "FortuneForge.Server/Slots",
    "games/MidnightVault/src/FortuneForge.Games.MidnightVault",
    "games/GoldFoundry/src/FortuneForge.Games.GoldFoundry",
    "games/NeonNights/src/FortuneForge.Games.NeonNights",
    "games/JadeDragon/src/FortuneForge.Games.JadeDragon",
    "games/shared/FortuneForge.Games.Slots.Contracts"
};
var sourcePaths = sourceDirectories.SelectMany(directory =>
        Directory.EnumerateFiles(Path.Combine(repository, directory), "*", SearchOption.AllDirectories))
    .Where(file => (Path.GetExtension(file) is ".cs" or ".json" or ".csproj") &&
        !file.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal) &&
        !file.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
    .Select(file => Path.GetRelativePath(repository, file).Replace('\\', '/'))
    .Append("FortuneForge.Server/appsettings.json").Append("FortuneForge.Server/Controllers/DemoSlotsController.cs")
    .Append(routeFile).Append("FortuneForge.Server/RoundAudit/DemoSlotRoundRecording.cs")
    .Append("FortuneForge.Server/Accounts/Security/AntiAbuseRateLimiting.cs")
    .Append("tools/FortuneForge.SlotContracts/Program.cs")
    .Append("tools/FortuneForge.SlotContracts/FortuneForge.SlotContracts.csproj")
    .Order(StringComparer.Ordinal).ToArray();
var sourceHashes = sourcePaths.ToDictionary(path => path, path => Hash(Path.Combine(repository, path)));
var modes = new[] { "sync", "rows", "paw", "rand" };
var seals = new[] { "SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND" };
var summaries = new List<object>();
var allScenarios = new List<FixtureCase>();
Directory.CreateDirectory(output);

foreach (var route in routes)
{
    var game = definitions.GetGame(route.GameId)!;
    var paytable = definitions.GetPaytable(game.Math.PaytableId)!;
    var usesFoundryCollector = GoldFoundrySlotDefinitions.IsModel(game);
    SlotSpecialRoundProfiles.TryGet(route.GameId, out var profile);
    var usesCharmHarvest = route.GameId == SlotSpecialRoundProfiles.RainbowRealmGameId && !profile.UsesDirectValueTokens;
    var wager = game.Wagering.MinimumWagerPoints;
    var gameDirectory = Path.Combine(output, route.Id);
    Directory.CreateDirectory(gameDirectory);
    var scenarios = new List<FixtureCase>();
    var misses = new List<object>();
    var unavailable = new List<string>();
    var controllerForStatus = Controller(0);
    Require(controllerForStatus.Status(route.GameId) is NoContentResult, $"Actual controller status rejected {route.GameId}.");
    Search("base-loss", null, 0, result => result.Payout.TotalPoints == 0 && result.FreeSpinsRemaining == 0);
    Search("base-win", null, 0, result => result.Payout.TotalPoints > 0 && result.FreeSpinsRemaining == 0);
    if (game.FreeGames is not null && profile.ScatterAwardedSpins > 0)
        Search("scatter", null, 0, result => result.FreeSpinsAwarded == profile.ScatterAwardedSpins &&
            result.FreeSpinsRemaining == profile.ScatterAwardedSpins && result.FreeSpinFeatureMode == profile.ScatterFeatureMode &&
            result.Reels.SelectMany(reel => reel).Count(symbol => symbol == game.FreeGames.SymbolId) >= profile.ScatterRequiredSymbols);
    else unavailable.Add("Scatter awards disabled in actual engine profile; no scatter scenario invented.");
    if (profile.UsesCollections)
        for (var index = 0; index < modes.Length; index++)
        {
            var mode = modes[index];
            var seal = seals[index];
            Search($"collection-{mode}", mode, 0, result => result.FreeSpinsRemaining == profile.CollectionAwardedSpins &&
                result.FreeSpinFeatureMode == (profile.CollectionFeatureMode ?? mode) && result.SealsAwarded.GetValueOrDefault(seal) > 0);
        }
    else unavailable.Add("Collections disabled in actual engine profile; no collection scenario invented.");
    if (usesFoundryCollector)
        Search("energy-completion", null, 99, result => result.CruciblePoured == true &&
            result.CruciblePayoutCents > 0 && result.EnergyBalance == 0 && result.FreeSpinsRemaining == 0);
    else if (profile.UsesEnergy && game.Energy is not null)
        Search("energy-completion", null, 99, result => result.EnergyMultiplierApplied && result.EnergyBalance == 0 && result.FreeSpinsRemaining > 0);
    else unavailable.Add("Energy disabled in actual engine profile.");
    if (usesFoundryCollector)
        Search("reel-collector-award", null, 0, result =>
            result.MoneyGrabPoints > 0 && result.MonkeyPawCount > 0 && result.FreeSpinsRemaining == 0);
    else if (usesCharmHarvest)
        Search("charm-harvest", null, 0, result => result.MoneyGrabPoints > 0 && result.MonkeyPawCount > 0 && result.FreeSpinsRemaining == 0);
    else if (profile.UsesDirectValueTokens)
        Search("direct-token-award", null, 0, result => result.MoneyGrabPoints > 0 && result.MonkeyPawCount > 0 && result.FreeSpinsRemaining == 0);
    else unavailable.Add("Direct-value tokens and collectors disabled in actual engine profile.");

    Require(misses.Count == 0, $"Required actual engine scenarios were not found for {route.Id}.");
    var contract = new
    {
        schemaVersion = 1, route, gameId = route.GameId, profile, layout = game.Layout,
        symbols = game.Symbols, symbolSet = definitions.GetSymbolSet(game.Symbols.SymbolSetId), matching = game.Matching, wagering = game.Wagering,
        freeGames = game.FreeGames, energy = game.Energy, specialPoints = game.SpecialPoints,
        math = game.Math, paylines = game.Paylines,
        paytable = paytable.Rules,
        observedRows = scenarios.SelectMany(scenario => scenario.Steps).Select(step => step.Response.Reels[0].Count).Distinct().Order().ToArray(),
        observedActiveModes = scenarios.SelectMany(scenario => scenario.Steps).Select(step => step.Response.FreeSpinFeatureMode).Distinct().ToArray(),
        statusEndpoint = 204, scenarios = scenarios.Select(ScenarioSummary), missingScenarios = misses, unavailable
    };
    WriteNew(gameDirectory, "contract.json", contract);
    foreach (var scenario in scenarios)
        WriteNew(gameDirectory, $"{scenario.Name}.json", new
        {
            scenario = scenario.Name, source = new
            {
                generator = "Actual in-process DemoSlotsController + SpinService routed game engine",
                scenario.Seed, engineVersion = scenario.Steps[0].Response.EngineAudit?.EngineVersion ?? SpinService.AuditEngineVersion,
                gameId = route.GameId,
                noHttpServerOrAccountSettlement = true, scenario.ReplayMatchesExceptSpinId,
                deterministicCaveat = route.GameId == "pirates-fortune-v1" ?
                    "Pirates gem chance also depends on fresh engine spin GUID; replay may differ beyond response.spinId. Near-maximum seeds guarantee the initial gem award." :
                    route.GameId == SlotSpecialRoundProfiles.DragonHoardGameId
                        ? "Public replay compares every field except fresh spinId and round IDs derived from that same spin. Existing carried round IDs remain exact."
                        : "Public response replay compared field-for-field except fresh response.spinId."
            },
            wagerPoints = wager,
            initialState = new { scenario.InitialCollectionMode, scenario.InitialEnergy, collectionCount = scenario.InitialCollectionMode is null ? 0 : profile.CollectionTarget - 1 },
            steps = scenario.Steps
        });
    summaries.Add(new
    {
        route, statusEndpoint = 204, game.Layout.PaylineCount, profile,
        game.Math.PayoutMultiplier, observedRows = contract.observedRows,
        scenarios = scenarios.Select(ScenarioSummary).ToArray(), missingScenarios = misses.ToArray(),
        responses = scenarios.Sum(scenario => scenario.Steps.Count), unavailable
    });
    allScenarios.AddRange(scenarios);
    Console.WriteLine($"{route.Id}: {scenarios.Count} scenarios / {scenarios.Sum(scenario => scenario.Steps.Count)} actual responses, missing {misses.Count}; paths {game.Layout.PaylineCount}, collection {profile.CollectionTarget}/{profile.CollectionAwardedSpins}, scatter {profile.ScatterRequiredSymbols}/{profile.ScatterAwardedSpins}, calibration {game.Math.PayoutMultiplier}.");

    void Search(string name, string? nearingMode, int initialEnergy, Func<SpinResult, bool> accept)
    {
        const int seedLimit = 100_000;
        for (var seed = 0; seed < seedLimit; seed++)
        {
            var controller = Controller(seed);
            var initial = new DemoSpinRequest(route.GameId, wager, false, 0, null, initialEnergy,
                profile.UsesCollections ? modes.Select(mode => new SlotSealCollection(mode,
                    mode == nearingMode ? profile.CollectionTarget - 1 : 0,
                    mode == nearingMode ? wager : 0, profile.CollectionTarget)).ToArray() : [], null);
            var trigger = Spin(controller, initial);
            if (!accept(trigger)) continue;
            var steps = new List<FixtureStep> { new(initial, trigger) };
            var previous = trigger;
            var complete = true;
            for (var continuation = 0; previous.FreeSpinsRemaining > 0; continuation++)
            {
                if (continuation >= 250) { complete = false; break; }
                var request = new DemoSpinRequest(route.GameId, previous.FreeSpinWagerPoints ?? wager, true,
                    previous.FreeSpinsRemaining, previous.FreeSpinWagerPoints, previous.EnergyBalance,
                    previous.SealCollections, previous.FreeSpinFeatureMode, previous.MidnightBonusState);
                previous = Spin(controller, request);
                steps.Add(new(request, previous));
            }
            if (!complete) continue;
            var replay = Controller(seed);
            var replayMatches = true;
            foreach (var step in steps)
            {
                var repeated = Spin(replay, step.Request);
                var expectedNode = JsonSerializer.SerializeToNode(step.Response, json)!.AsObject();
                var actualNode = JsonSerializer.SerializeToNode(repeated, json)!.AsObject();
                expectedNode.Remove("spinId"); actualNode.Remove("spinId");
                if (route.GameId == SlotSpecialRoundProfiles.DragonHoardGameId)
                {
                    NormalizeNewRoundIds(expectedNode, step.Response.SpinId);
                    NormalizeNewRoundIds(actualNode, repeated.SpinId);
                }
                replayMatches &= JsonNode.DeepEquals(expectedNode, actualNode);
            }
            if (route.GameId != "pirates-fortune-v1") Require(replayMatches, $"Seeded public replay differs: {route.Id}/{name}.");
            scenarios.Add(new FixtureCase(name, seed, nearingMode, initialEnergy, steps, replayMatches));
            return;
        }
        misses.Add(new { scenario = name, seedsTried = seedLimit, status = "not-found-within-bound", note = "Not fabricated; requires further investigation." });
    }

    DemoSlotsController Controller(int seed)
    {
        var random = new SeededRandom(seed);
        return new DemoSlotsController(new SpinService(definitions, new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random), new NoOpAudit(),
            NullLogger<DemoSlotsController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    SpinResult Spin(DemoSlotsController controller, DemoSpinRequest request)
    {
        var action = controller.Spin(request, CancellationToken.None).GetAwaiter().GetResult();
        var result = (action as OkObjectResult)?.Value as SpinResult ??
            throw new InvalidOperationException($"Actual controller rejected {route.Id}: {action.GetType().Name}");
        var expectedRows = game.Layout.VisibleRows + (request.UseFreeSpin && SlotSpecialRoundProfiles.HasFeatureMode(request.FreeSpinFeatureMode, "rows") ? 2 : 0);
        Require(result.GameId == route.GameId && result.Reels.Count == game.Layout.ReelCount &&
            result.Reels.All(reel => reel.Count == expectedRows), $"Grid/identity mismatch: {route.Id}.");
        Require(result.WagerPoints == request.WagerPoints && result.IsFreeSpin == request.UseFreeSpin, $"Wager/free-spin identity mismatch: {route.Id}.");
        Require(result.SlotsCreditsBalance is null, "Demo unexpectedly includes account balance.");
        var offPaylinePoints = usesFoundryCollector || usesCharmHarvest ? result.MoneyGrabPoints : 0;
        Require(result.Payout.TotalPoints == result.Payout.Paylines.Sum(line => line.AmountPoints) + offPaylinePoints &&
            result.Payout.Paylines.All(line => line.AmountPoints == line.Matches.Sum(match => match.AmountPoints)), "Payout does not reconcile.");
        if (usesFoundryCollector)
            Require(result.Payout.Paylines.All(line => line.Matches.All(match =>
                paytable.Rules.Any(rule => rule.SymbolId == match.Match.SymbolId))) &&
                result.FreeSpinsAwarded == 0 && result.FreeSpinsRemaining == 0, "Unexpected Foundry payline or free spin.");
        Require(result.FreeSpinsRemaining >= 0 && (result.FreeSpinsRemaining == 0 ? result.FreeSpinWagerPoints is null : result.FreeSpinWagerPoints == wager), "Bonus locked wager mismatch.");
        Require(result.FreeSpinsRemaining > 0 || result.FreeSpinFeatureMode is null, "Ended bonus retained active mode.");
        if (!profile.UsesEnergy) Require(result.EnergyAwarded == 0 && result.EnergyBalance == 0 && !result.EnergyMultiplierApplied, "Disabled energy appeared.");
        if (!profile.UsesDirectValueTokens && !usesCharmHarvest && !usesFoundryCollector)
            Require(result.MoneyGrabPoints == 0 && result.MonkeyPawCount == 0, "Disabled direct tokens appeared.");
        if (!profile.UsesCollections) Require(result.SealCollections.Count == 0 && result.SealsAwarded.Count == 0, "Disabled collection appeared.");
        return result;
    }
}

foreach (var (path, expected) in sourceHashes)
    Require(Hash(Path.Combine(repository, path)) == expected, $"Source changed during export: {path}");
WriteNew(output, "source-provenance.json", new
{
    checkedUtc = DateTime.UtcNow, engineVersion = SpinService.AuditEngineVersion,
    assemblySha256 = Hash(compiledAssembly), compilationEvidence = "ProjectReference build; run this tool from freshly built source.",
    sourceSha256 = sourceHashes, recordedSourceCount = sourceHashes.Count, hashesUnchangedDuringExport = true,
    scope = "Current slot/controller/config/route source hashes and loaded assembly identity; this is demo contract evidence, not security certification."
});
WriteNew(output, "index.json", new
{
    schemaVersion = 1, exportedUtc = DateTime.UtcNow, games = summaries,
    gameCount = routes.Length, scenarioCount = allScenarios.Count, responseCount = allScenarios.Sum(scenario => scenario.Steps.Count),
    generator = "Actual in-process DemoSlotsController + shared engine; no HTTP listener, live request, account settlement, Firestore, tuning or backend source edits.",
    randomness = "System.Random(seed), fresh controller/service/pity state for each searched scenario; production RNG is unchanged.",
    collectionState = "Collection cases seed the real engine track at target-1; they do not prove a complete natural collection history.",
    boundedContinuations = 250, boundedSeedsPerScenario = 100000,
    proofLimit = "Evidence validates current local public demo contracts. It is not a paid-settlement/security audit, statistical certification, owner approval or release authorization."
});
Console.WriteLine($"COMPLETE: {routes.Length} games, {allScenarios.Count} scenarios, {allScenarios.Sum(scenario => scenario.Steps.Count)} actual responses.");

static object ScenarioSummary(FixtureCase item) => new
{
    scenario = item.Name, item.Seed, responses = item.Steps.Count,
    triggerRows = item.Steps[0].Response.Reels[0].Count,
    continuationRows = item.Steps.Skip(1).Select(step => step.Response.Reels[0].Count).Distinct().ToArray(),
    triggerFreeSpinsAwarded = item.Steps[0].Response.FreeSpinsAwarded,
    triggerCollectionFreeSpinsAwarded = item.Steps[0].Response.CollectionFreeSpinsAwarded,
    triggerRemaining = item.Steps[0].Response.FreeSpinsRemaining,
    triggerMode = item.Steps[0].Response.FreeSpinFeatureMode,
    finalRemaining = item.Steps[^1].Response.FreeSpinsRemaining,
    item.ReplayMatchesExceptSpinId
};
void WriteNew(string directory, string filename, object value)
{
    using var file = new FileStream(Path.Combine(directory, filename), FileMode.CreateNew, FileAccess.Write);
    // Match the repository's LF policy on Windows as well as Linux.
    file.Write(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, json).ReplaceLineEndings("\n") + "\n"));
}
static void NormalizeNewRoundIds(JsonNode? node, Guid spinId)
{
    var prefix = spinId.ToString("D") + ":";
    if (node is JsonObject map)
        foreach (var pair in map.ToArray())
        {
            if (pair.Key == "id" && pair.Value is JsonValue value && value.TryGetValue<string>(out var id) &&
                id.StartsWith(prefix, StringComparison.Ordinal))
                map[pair.Key] = "current-spin:" + id[prefix.Length..];
            else NormalizeNewRoundIds(pair.Value, spinId);
        }
    else if (node is JsonArray array)
        foreach (var item in array) NormalizeNewRoundIds(item, spinId);
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
sealed class SeededRandom(int seed) : IRandomIndexSource { private readonly Random random = new(seed); public int Next(int maximumExclusive) => random.Next(maximumExclusive); }
sealed class NoOpAudit : IDemoSlotAuditStore { public Task RecordAsync(DemoSlotAuditEntry entry, CancellationToken cancellationToken) => Task.CompletedTask; }
sealed record Route(string Id, string Title, string PlayPath, string DemoPath, string GameId);
sealed record FixtureStep(DemoSpinRequest Request, SpinResult Response);
sealed record FixtureCase(string Name, int Seed, string? InitialCollectionMode, int InitialEnergy, IReadOnlyList<FixtureStep> Steps, bool ReplayMatchesExceptSpinId);
