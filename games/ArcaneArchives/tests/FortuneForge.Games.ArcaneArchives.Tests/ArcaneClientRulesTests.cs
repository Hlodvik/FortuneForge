using System.Runtime.CompilerServices;
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using Xunit;

namespace FortuneForge.Games.ArcaneArchives.Tests;

public sealed class ArcaneClientRulesTests
{
    [Fact]
    public void ClientGuideAndWagerMenuMatchTheNativeCurrentGameContract()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ClientContractPath()));
        var contract = document.RootElement;
        var game = ArcaneArchivesEngine.CreateGame();
        var paytable = ArcaneArchivesEngine.CreatePaytable();
        Assert.Equal(ArcaneArchivesEngine.EngineVersion, contract.GetProperty("engineVersion").GetString());
        Assert.Equal(paytable.Id, contract.GetProperty("paytableId").GetString());
        Assert.Equal(game.Wagering.PointValueInCents, contract.GetProperty("pointValueInCents").GetDecimal());
        Assert.Equal(game.Wagering.MinimumWagerPoints, contract.GetProperty("minimumWagerPoints").GetInt64());
        Assert.Equal(game.Wagering.MaximumWagerPoints, contract.GetProperty("maximumWagerPoints").GetInt64());
        Assert.Equal(game.Wagering.WagerIncrementPoints, contract.GetProperty("wagerIncrementPoints").GetInt64());
        Assert.Equal(game.Math.PayoutMultiplier, contract.GetProperty("payoutMultiplier").GetDecimal());
        Assert.Equal(ArcaneArchivesEngine.Profile.PaylinePatternIds!,
            contract.GetProperty("paylinePatternIds").EnumerateArray().Select(id => id.GetInt32()).ToArray());
        var awards = contract.GetProperty("paylinePayouts").EnumerateObject().SelectMany(symbol => new[]
        {
            (Symbol: symbol.Name, Length: 3, Multiplier: symbol.Value.GetProperty("three").GetInt64()),
            (Symbol: symbol.Name, Length: 5, Multiplier: symbol.Value.GetProperty("five").GetInt64())
        }).OrderBy(rule => rule.Symbol).ThenBy(rule => rule.Length).ToArray();
        Assert.Equal(paytable.Rules.Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier))
            .OrderBy(rule => rule.SymbolId).ThenBy(rule => rule.MatchLength).ToArray(), awards);
    }

    private static string ClientContractPath([CallerFilePath] string sourcePath = "") => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(sourcePath)!, "../../client/FortuneForge.Games.ArcaneArchives.Client/src/assets/rules/client-rules.json"));
}
