using FortuneForge.Games.FrontierFortune;
using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class FrontierOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericSpinsRejectFrontierBeforeDrawingAnyClassicOutcome(bool customProvider)
    {
        var (service, random) = CreateService(customProvider);

        var error = Assert.Throws<ArgumentException>(() => service.Spin(
            FrontierFortuneEngine.GameId, FrontierRules.Definition.MinWagerPoints, "frontier-owner", false));

        Assert.Contains("Frontier", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, random.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmittedClassicSpinsCannotSettleFrontierThroughEitherDefinitionProvider(bool customProvider)
    {
        var (service, random) = CreateService(customProvider);

        Assert.Throws<ArgumentException>(() => service.SpinAdmitted(
            FrontierFortuneEngine.GameId, Admission(), "frontier-owner"));

        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void HostExposesFrontierMetadataWithoutAClassicSpinnerOrSpecialRoundProfile()
    {
        var definitions = new OptionsSlotsDefinitionProvider();
        var game = Assert.IsType<GameDefinition>(definitions.GetGame(FrontierFortuneEngine.GameId));
        var symbols = Assert.IsType<SymbolSetDefinition>(definitions.GetSymbolSet(game.Symbols.SymbolSetId));

        Assert.Equal(FrontierFortuneEngine.SymbolSetId, game.Symbols.SymbolSetId);
        Assert.Equal(FrontierRules.Definition.WildSymbol, game.Symbols.WildSymbolId);
        Assert.Equal(FrontierRules.Definition.Symbols.Select(symbol => symbol.Id), symbols.Symbols.Select(symbol => symbol.Id));
        Assert.Contains(symbols.Symbols, symbol => symbol.Id == "BADGE");
        Assert.Contains(symbols.Symbols, symbol => symbol.Id == "BAR");
        Assert.Contains(symbols.Symbols, symbol => symbol.Id == "WILD");
        Assert.DoesNotContain(symbols.Symbols, symbol => symbol.Id is "PAW" or "FREE_GAME" or "ACE");
        Assert.Null(game.FreeGames);
        Assert.Null(game.SpecialPoints);
        Assert.Null(game.Energy);
        Assert.False(SlotSpecialRoundProfiles.TryGet(game.Id, out _));

        var random = new RecordingRandom();
        var packages = new StandaloneSlotPackageRegistry(random);
        Assert.False(packages.TrySpin(game.Id, FrontierRules.Definition.MinWagerPoints,
            "frontier-owner", false, 0, null, null, false, out _));
        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void PreparedWorkerUsesTheFrontierProtocolAndOwnershipField()
    {
        var route = Assert.IsType<PreparedSlotWorkerRoute>(PreparedSlotWorkerRegistry.Find(FrontierFortuneEngine.GameId));

        Assert.Equal("Slots:FrontierFortuneWorkerRoundsEnabled", route.EnabledSetting);
        Assert.Equal(FrontierPaidRouteContract.SpinPath, route.SpinPath);
        Assert.Equal(FrontierPaidRouteContract.StatePath, route.StatePath);
        Assert.Equal(FrontierRoundStore.OwnerField, route.LegacyOwnerField);
        Assert.Contains("/frontier-fortune/", route.SpinPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassicPaidWorkerProtocolIsRejectedBeforeContactingAnyWorker()
    {
        using var transport = new RecordingTransport();
        using var http = new HttpClient(transport);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{FrontierFortuneEngine.GameId}:Url"] = "http://localhost:8123",
            [$"GameWorkers:{FrontierFortuneEngine.GameId}:SigningKey"] = Convert.ToBase64String(new byte[32])
        }).Build();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        var worker = new GameWorkerClient(http, configuration, builder.Environment,
            TimeProvider.System, NullLogger<GameWorkerClient>.Instance);

        await Assert.ThrowsAsync<GameWorkerUnavailableException>(() => PaidSlotWorker.ResolveAsync(
            worker, FrontierFortuneEngine.GameId, "frontier-owner", Admission(), default));

        Assert.Equal(0, transport.Calls);
    }

    private static SlotSpinAdmission Admission() => new(FrontierRules.Definition.MinWagerPoints,
        FrontierRules.Definition.MinWagerPoints * FrontierRules.Definition.PointValueInCents,
        false, 0, false, 0, null, 0, null)
    {
        AdmissionId = Guid.NewGuid(), StartedAtUtc = DateTime.UtcNow
    };

    private static (SpinService Service, RecordingRandom Random) CreateService(bool customProvider)
    {
        ISlotsDefinitionProvider definitions = new OptionsSlotsDefinitionProvider();
        if (customProvider) definitions = new FixtureDefinitions(definitions);
        var random = new RecordingRandom();
        return (new SpinService(definitions, new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random), random);
    }

    private sealed class FixtureDefinitions(ISlotsDefinitionProvider source) : ISlotsDefinitionProvider
    {
        public GameDefinition? GetGame(string id) => source.GetGame(id);
        public SymbolSetDefinition? GetSymbolSet(string id) => source.GetSymbolSet(id);
        public ReelSetDefinition? GetReelSet(string id) => source.GetReelSet(id);
        public PaytableDefinition? GetPaytable(string id) => source.GetPaytable(id);
    }

    private sealed class RecordingRandom : IRandomIndexSource
    {
        public int Calls { get; private set; }
        public int Next(int maximumExclusive) { Calls++; return 0; }
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException("The classic transport must never be contacted for Frontier Fortune.");
        }
    }
}
