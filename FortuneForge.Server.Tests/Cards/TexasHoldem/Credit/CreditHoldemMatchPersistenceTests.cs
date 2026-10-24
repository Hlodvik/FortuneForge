using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemMatchPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTime Start = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LegacyPlayerFlagsAndSkillsMigrateWithoutChangingTheHandOrAccounting()
    {
        var match = SampleMatch();
        var document = LegacyDocument(match);

        var restored = CreditHoldemMatchPersistence.Deserialize(document.ToJsonString());

        Assert.Equal(JsonSerializer.Serialize(match, JsonOptions), JsonSerializer.Serialize(restored, JsonOptions));
        Assert.True(restored.Players.Single(player => player.ActorId == "account-one").IsAccountBacked);
        Assert.All(restored.Players.Where(player => player.ActorId != "account-one"), player => Assert.False(player.IsAccountBacked));
        Assert.DoesNotContain("isBot", JsonSerializer.Serialize(restored, JsonOptions));
        Assert.DoesNotContain("botSkillLevel", JsonSerializer.Serialize(restored, JsonOptions));
    }

    [Fact]
    public void CurrentPlayerFormatAndPrivateMetadataRoundTripUnchanged()
    {
        var match = SampleMatch();
        match.Players[1].HostMetadata["managed-departure-hand"] = "7";
        var json = JsonSerializer.Serialize(match, JsonOptions);

        Assert.Equal(json, JsonSerializer.Serialize(CreditHoldemMatchPersistence.Deserialize(json), JsonOptions));
    }

    [Fact]
    public void CurrentPrivateSkillTakesPrecedenceOverAnOldSkillField()
    {
        var document = JsonSerializer.SerializeToNode(SampleMatch(), JsonOptions)!.AsObject();
        var player = document["players"]![1]!.AsObject();
        player["isBot"] = true;
        player["botSkillLevel"] = 4;
        player["hostMetadata"]!["managed-skill"] = "2";

        var restored = CreditHoldemMatchPersistence.Deserialize(document.ToJsonString());

        Assert.False(restored.Players[1].IsAccountBacked);
        Assert.Equal("2", restored.Players[1].HostMetadata["managed-skill"]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "null")]
    [InlineData(null, "\"false\"")]
    [InlineData("null", "false")]
    [InlineData("\"true\"", "false")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public void MissingMalformedOrContradictoryRolesFailClosed(string? currentJson, string? legacyJson)
    {
        var document = JsonSerializer.SerializeToNode(SampleMatch(), JsonOptions)!.AsObject();
        var player = document["players"]![0]!.AsObject();
        player.Remove("isAccountBacked");
        if (currentJson is not null) player["isAccountBacked"] = JsonNode.Parse(currentJson);
        if (legacyJson is not null) player["isBot"] = JsonNode.Parse(legacyJson);

        Assert.Throws<JsonException>(() => CreditHoldemMatchPersistence.Deserialize(document.ToJsonString()));
    }

    [Fact]
    public void LegacyManagedPlayerWithoutASkillRetainsTheDefaultPolicy()
    {
        var document = LegacyDocument(SampleMatch());
        document["players"]![1]!["botSkillLevel"] = null;

        var restored = CreditHoldemMatchPersistence.Deserialize(document.ToJsonString());

        Assert.False(restored.Players[1].IsAccountBacked);
        Assert.Empty(restored.Players[1].HostMetadata);
    }

    private static JsonObject LegacyDocument(CreditHoldemMatch match)
    {
        var document = JsonSerializer.SerializeToNode(match, JsonOptions)!.AsObject();
        foreach (var node in document["players"]!.AsArray())
        {
            var player = node!.AsObject();
            var accountBacked = player["isAccountBacked"]!.GetValue<bool>();
            player.Remove("isAccountBacked");
            player["isBot"] = !accountBacked;
            player["botSkillLevel"] = accountBacked ? null : int.Parse(player["hostMetadata"]!["managed-skill"]!.GetValue<string>());
            player.Remove("hostMetadata");
        }
        return document;
    }

    private static CreditHoldemMatch SampleMatch() => CreditHoldemEngine.Deal(
        "compatibility-match",
        [
            new("account-one", "seat-one", "Alice", true, 0, 5_000),
            new("managed-two", "seat-two", "Maya", false, 1, 4_500, new Dictionary<string, string> { ["managed-skill"] = "4" }),
            new("managed-three", "seat-three", "Theo", false, 2, 5_500, new Dictionary<string, string> { ["managed-skill"] = "2" })
        ],
        CreditHoldemMoney.OpenPartition,
        42,
        Start);
}
