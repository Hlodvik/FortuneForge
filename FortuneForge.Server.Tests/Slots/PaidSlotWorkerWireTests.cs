using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class PaidSlotWorkerWireTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LegacyArcaneHostOptionsRetainTheirWireShapeAndReadBack()
    {
        var options = new PaidArcaneSpinOptions(ArcaneArchivesEngine.PreviousPotionEngineVersion, true,
            new(1, "MANDRAKE_ROOT", 4));
        var legacy = new LegacyOptions(false, null, options);
        var request = CreateRequest(ArcaneArchivesEngine.GameId);
        var incoming = FromLegacy(request, legacy);
        Assert.Equal(options, PaidArcaneSpinOptions.Read(incoming));

        var current = options.WriteTo(request);
        var serialized = JsonSerializer.SerializeToElement(current, Json);
        Assert.False(serialized.TryGetProperty("gameOptions", out _));
        Assert.False(serialized.GetProperty("arcane").GetProperty("potion").TryGetProperty("variantId", out _));
        Assert.Equal(legacy, serialized.Deserialize<LegacyOptions>(Json));
        Assert.Empty(request.GameOptions);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, 2)]
    public void LegacyNeonHostOptionsRetainTheirWireShapeAndReadBack(bool isDisco, int? multiplier)
    {
        var request = CreateRequest(NeonArcadeSlotDefinitions.GameId);
        var legacy = new LegacyOptions(isDisco, multiplier, null);
        var options = new PaidNeonSpinOptions(isDisco, multiplier);
        Assert.Equal(options, PaidNeonSpinOptions.Read(FromLegacy(request, legacy)));

        var serialized = JsonSerializer.SerializeToElement(options.WriteTo(request), Json);
        Assert.False(serialized.TryGetProperty("gameOptions", out _));
        Assert.Equal(isDisco, serialized.GetProperty("isNeonDiscoPlay").GetBoolean());
        Assert.Equal(legacy, serialized.Deserialize<LegacyOptions>(Json));
    }

    [Fact]
    public void OldHostPlaceholdersStayValidAndForeignFeatureOptionsAreRejected()
    {
        var request = CreateRequest("other-slot-v1");
        PaidSlotOutcomeProtocol.ValidateRequest(FromLegacy(request, new(false, null, null)), request.GameId);
        Assert.Throws<ArgumentException>(() =>
            PaidSlotOutcomeProtocol.ValidateRequest(FromLegacy(request, new(true, 2, null)), request.GameId));

        var arcane = new PaidArcaneSpinOptions(ArcaneArchivesEngine.EngineVersion, false, null);
        var neonRequest = CreateRequest(NeonArcadeSlotDefinitions.GameId);
        Assert.Throws<ArgumentException>(() => PaidNeonSpinOptions.Read(arcane.WriteTo(neonRequest)));
    }

    [Fact]
    public void NeonCodecPreservesWebJsonNumberAndPropertyNameHandling()
    {
        var request = CreateRequest(NeonArcadeSlotDefinitions.GameId);
        var wire = JsonSerializer.Serialize(request, Json);
        wire = wire[..^1] + """ ,"IsNeonDiscoPlay":true,"NeonDiscoMultiplier":"3"}""";
        var legacy = JsonSerializer.Deserialize<PaidSlotOutcomeRequest>(wire, Json)!;

        Assert.Equal(new PaidNeonSpinOptions(true, 3), PaidNeonSpinOptions.Read(legacy));
    }

    [Fact]
    public void MalformedOwnedOptionsAreReportedAsInvalidAdmissions()
    {
        var request = CreateRequest(NeonArcadeSlotDefinitions.GameId);
        var wire = JsonSerializer.Serialize(request, Json);
        wire = wire[..^1] + """ ,"isNeonDiscoPlay":"yes"}""";
        var malformed = JsonSerializer.Deserialize<PaidSlotOutcomeRequest>(wire, Json)!;

        Assert.Throws<ArgumentException>(() => PaidNeonSpinOptions.Read(malformed));
    }

    private static PaidSlotOutcomeRequest CreateRequest(string gameId) =>
        new(Guid.NewGuid(), gameId, "player", 12, false, false, 0, null, 0, null);

    private static PaidSlotOutcomeRequest FromLegacy(PaidSlotOutcomeRequest request, LegacyOptions legacy)
    {
        var common = JsonSerializer.SerializeToElement(request, Json);
        var oldOptions = JsonSerializer.SerializeToElement(legacy, Json);
        var wire = common.EnumerateObject().Concat(oldOptions.EnumerateObject())
            .ToDictionary(property => property.Name, property => property.Value);
        return JsonSerializer.Deserialize<PaidSlotOutcomeRequest>(JsonSerializer.Serialize(wire, Json), Json)!;
    }

    private sealed record LegacyOptions(bool IsNeonDiscoPlay, int? NeonDiscoMultiplier, PaidArcaneSpinOptions? Arcane);
}
