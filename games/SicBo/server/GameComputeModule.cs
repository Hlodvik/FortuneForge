using FortuneForge.Games.Abstractions;
using FortuneForge.Games.SicBo;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => SicBoModule.Descriptor;

    internal static void Map(WebApplication app) =>
        app.MapComputeAction("/internal/v1/rounds/resolve", (SicBoComputeRequest request) =>
        {
            var roll = new SicBoRoll(request.Dice);
            ArgumentNullException.ThrowIfNull(request.Bets);
            var settlements = request.Bets.Select(bet => SicBoPaytable.Settle(CreateBet(bet), roll)).ToArray();
            return new { Roll = roll, Settlements = settlements };
        });

    private static SicBoBet CreateBet(SicBoComputeBet bet)
    {
        ArgumentNullException.ThrowIfNull(bet);
        IReadOnlyList<int> selection = bet.Selection ?? [];
        return (bet.Kind, selection) switch
        {
            (SicBoBetKind.SingleNumber, [var face]) => SicBoBet.SingleNumber(bet.Stake, face),
            (SicBoBetKind.SpecificDouble, [var face]) => SicBoBet.SpecificDouble(bet.Stake, face),
            (SicBoBetKind.SpecificTriple, [var face]) => SicBoBet.SpecificTriple(bet.Stake, face),
            (SicBoBetKind.Total, [var total]) => SicBoBet.ForTotal(bet.Stake, total),
            (SicBoBetKind.TwoNumberCombination, [var first, var second]) =>
                SicBoBet.TwoNumberCombination(bet.Stake, first, second),
            (_, []) => SicBoBet.CreateWithoutSelection(bet.Kind, bet.Stake),
            _ => throw new ArgumentException("The Sic Bo selections do not match the bet kind.", nameof(bet)),
        };
    }

    private sealed record SicBoComputeRequest(IReadOnlyList<int> Dice, IReadOnlyList<SicBoComputeBet> Bets);
    private sealed record SicBoComputeBet(SicBoBetKind Kind, decimal Stake, IReadOnlyList<int>? Selection = null);
}
