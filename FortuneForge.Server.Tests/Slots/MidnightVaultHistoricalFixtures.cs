using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Slots.Models;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

internal static class MidnightVaultHistoricalFixtures
{
    internal const string V1 = "midnight-vault-paytable-v1";
    internal const string V2 = "midnight-vault-paytable-v2";
    internal const string OldReels = "midnight-vault-reels-v1";
    internal const string OldStripHash = "2ea94368b00549eed68bdaf47b8c7698437d13ac9dffc7f6e576ca1a0418b929";
    internal const string OldBoostedStripHash = "0bbe613062f583a183e09039f80e9064fffbf5fd90c2d97999d409782f4e9e04";
    private const string V2ContractHash = "51ff01fdbbb759fb80bbd410f4bbf742dcc6a8c47783dd2a7fcaac44f52c0ecc";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string StripHash(IEnumerable<IEnumerable<string>> strips) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', strips.Select(strip => string.Join(',', strip))))));

    internal static void AssertHistoricalReels(ReelSetDefinition reels, bool boosted = false)
    {
        Assert.Equal(OldReels + (boosted ? "-power-boost" : ""), reels.Id);
        Assert.Equal(boosted ? OldBoostedStripHash : OldStripHash, StripHash(reels.Reels));
    }

    internal static JsonDocument V2Contract()
    {
        var bytes = File.ReadAllBytes(Path.Combine(FixtureRoot(), "midnight-vault-paid-v2", "effective-contract.json"));
        Assert.Equal(V2ContractHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var document = JsonDocument.Parse(bytes);
        Assert.Equal(V2, document.RootElement.GetProperty("paytableId").GetString());
        Assert.Equal(OldReels, document.RootElement.GetProperty("reelSetId").GetString());
        Assert.Equal(1m, document.RootElement.GetProperty("payoutMultiplier").GetDecimal());
        var strips = document.RootElement.GetProperty("reelStrips").Deserialize<List<List<string>>>(Json)!;
        Assert.Equal(OldStripHash, StripHash(strips));
        return document;
    }

    internal static IReadOnlyList<PayoutRule> V2Rules()
    {
        using var document = V2Contract();
        return document.RootElement.GetProperty("paytableRules").Deserialize<List<PayoutRule>>(Json)!;
    }

    internal static SlotSpinEngineAudit V1Audit()
    {
        var bytes = File.ReadAllBytes(Path.Combine(FixtureRoot(), "midnight-vault-paid-v1", "v1-paid-win.json"));
        Assert.Equal("16fb8ddb8aabad76f83203794017ababa895291b228949558aa92301a4166d85", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using var saved = JsonDocument.Parse(bytes);
        using var payload = JsonDocument.Parse(saved.RootElement.GetProperty("audit").GetProperty("payloadJson").GetString()!);
        var audit = payload.RootElement.GetProperty("data").GetProperty("engineAudit").Deserialize<SlotSpinEngineAudit>(Json)!;
        AssertHistoricalReels(audit.ReelSet);
        return audit;
    }

    private static string FixtureRoot([CallerFilePath] string sourcePath = "") => Path.Combine(Path.GetDirectoryName(sourcePath)!, "__fixtures__");
}
