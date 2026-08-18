using System.Text.Json;
using FortuneForge.Server.Cards.History;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.History;

public sealed class CardRoomHistoryContractTests
{
    [Fact]
    public void Public_summary_contains_no_identity_or_game_state_fields()
    {
        var value = new CardRoomHistoryItemResponse(
            "result_0000000001",
            "solitaire",
            "competitive",
            "match_00000000001",
            new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 16, 12, 10, 0, DateTimeKind.Utc),
            true,
            true,
            9m,
            820,
            74,
            1);

        var json = JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

        Assert.Contains("\"requiresClaim\":true", json, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "userId", "accountId", "seed", "deck", "hidden", "idempotency",
            "isBot", "skillLevel", "difficulty", "actorKind", "balance"
        })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
