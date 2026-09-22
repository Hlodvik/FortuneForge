using System.Text.Json;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Slots.Models;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SlotSpinPersistenceTests
{
    [Fact]
    public void OutcomeAuditData_ContainsEveryVisibleSymbolAndPaidMatchAsJsonReadableMaps()
    {
        IReadOnlyList<IReadOnlyList<string>> reels =
        [
            ["WILD", "A", "K"],
            ["A", "WILD", "Q"]
        ];
        var firstMatch = new PaidMatch(
            new SymbolMatch(
                7,
                "A",
                2,
                [new GridPosition(0, 1), new GridPosition(1, 1)],
                [new GridPosition(1, 1)]),
            4,
            400);
        var secondMatch = new PaidMatch(
            new SymbolMatch(
                8,
                "K",
                1,
                [new GridPosition(0, 2)],
                []),
            2,
            200);
        var payout = new SpinPayout(
            600,
            [
                new PaylinePayout(7, 400, [firstMatch]),
                new PaylinePayout(8, 200, [secondMatch])
            ]);
        var auditData = new Dictionary<string, object>
        {
            ["visibleSymbolWindow"] = FirestoreAccountStore.CreateVisibleSymbolWindowData(reels),
            ["payoutBreakdown"] = FirestoreAccountStore.CreatePayoutBreakdownData(payout)
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(auditData));

        Assert.Equal(5, FirestoreAccountStore.SlotOutcomeSchemaVersion);
        var visibleReels = document.RootElement
            .GetProperty("visibleSymbolWindow")
            .GetProperty("reels");
        Assert.Equal(2, visibleReels.GetArrayLength());
        Assert.Equal(0, visibleReels[0].GetProperty("reel").GetInt64());
        Assert.Equal(
            new[] { "WILD", "A", "K" },
            visibleReels[0]
                .GetProperty("symbols")
                .EnumerateArray()
                .Select(symbol => symbol.GetProperty("symbolId").GetString())
                .ToArray());
        Assert.Equal(1, visibleReels[1].GetProperty("reel").GetInt64());
        Assert.Equal(
            new long[] { 0, 1, 2 },
            visibleReels[1]
                .GetProperty("symbols")
                .EnumerateArray()
                .Select(symbol => symbol.GetProperty("row").GetInt64())
                .ToArray());
        Assert.Equal(
            new[] { "A", "WILD", "Q" },
            visibleReels[1]
                .GetProperty("symbols")
                .EnumerateArray()
                .Select(symbol => symbol.GetProperty("symbolId").GetString())
                .ToArray());

        var payoutBreakdown = document.RootElement.GetProperty("payoutBreakdown");
        Assert.Equal(600, payoutBreakdown.GetProperty("totalPoints").GetInt64());
        var paylines = payoutBreakdown.GetProperty("paylines");
        Assert.Equal(2, paylines.GetArrayLength());
        Assert.Equal(7, paylines[0].GetProperty("paylineId").GetInt64());
        Assert.Equal(400, paylines[0].GetProperty("amountPoints").GetInt64());

        var match = paylines[0].GetProperty("matches")[0];
        Assert.Equal(7, match.GetProperty("paylineId").GetInt64());
        Assert.Equal("A", match.GetProperty("symbolId").GetString());
        Assert.Equal(2, match.GetProperty("length").GetInt64());
        Assert.Equal(4, match.GetProperty("multiplier").GetInt64());
        Assert.Equal(400, match.GetProperty("amountPoints").GetInt64());
        Assert.Equal(
            new[] { (Reel: 0L, Row: 1L), (Reel: 1L, Row: 1L) },
            Positions(match.GetProperty("positions")));
        Assert.Equal(
            new[] { (Reel: 1L, Row: 1L) },
            Positions(match.GetProperty("wildPositions")));

        var secondPayline = paylines[1];
        Assert.Equal(8, secondPayline.GetProperty("paylineId").GetInt64());
        Assert.Equal(200, secondPayline.GetProperty("amountPoints").GetInt64());
        var secondPaidMatch = secondPayline.GetProperty("matches")[0];
        Assert.Equal("K", secondPaidMatch.GetProperty("symbolId").GetString());
        Assert.Equal(1, secondPaidMatch.GetProperty("length").GetInt64());
        Assert.Equal(2, secondPaidMatch.GetProperty("multiplier").GetInt64());
        Assert.Equal(200, secondPaidMatch.GetProperty("amountPoints").GetInt64());
        Assert.Equal(
            new[] { (Reel: 0L, Row: 2L) },
            Positions(secondPaidMatch.GetProperty("positions")));
        Assert.Empty(secondPaidMatch.GetProperty("wildPositions").EnumerateArray());
    }

    private static (long Reel, long Row)[] Positions(JsonElement positions) =>
        positions
            .EnumerateArray()
            .Select(position => (
                position.GetProperty("reel").GetInt64(),
                position.GetProperty("row").GetInt64()))
            .ToArray();
}
