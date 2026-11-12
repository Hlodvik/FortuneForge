namespace FortuneForge.Games.SicBo;

public static class SicBoCompute
{
    public const string ResolveRoute = "/internal/v1/rounds/resolve";

    public static SicBoComputeResult Resolve(SicBoComputeRequest request)
    {
        var roll = new SicBoRoll(request.Dice);
        ArgumentNullException.ThrowIfNull(request.Bets);
        var returns = request.Bets.Select(bet =>
        {
            ArgumentNullException.ThrowIfNull(bet);
            return SicBoPaytable.Settle(bet.ToDomain(), roll).TotalReturn;
        }).ToArray();
        return new SicBoComputeResult(returns);
    }
}

public sealed record SicBoComputeRequest(IReadOnlyList<int> Dice, IReadOnlyList<SicBoComputeBet> Bets);
public sealed record SicBoComputeResult(IReadOnlyList<decimal> Returns);

public sealed record SicBoComputeBet(SicBoBetKind Kind, decimal Stake, IReadOnlyList<int>? Selection = null)
{
    public static SicBoComputeBet FromDomain(SicBoBet bet)
    {
        if (bet.Combination is { } pair)
            return new SicBoComputeBet(bet.Kind, bet.Stake, [pair.FirstFace, pair.SecondFace]);
        return new SicBoComputeBet(bet.Kind, bet.Stake, (bet.Face ?? bet.Total) is { } value ? [value] : null);
    }

    internal SicBoBet ToDomain()
    {
        IReadOnlyList<int> selection = Selection ?? [];
        return (Kind, selection) switch
        {
            (SicBoBetKind.SingleNumber, [var face]) => SicBoBet.SingleNumber(Stake, face),
            (SicBoBetKind.SpecificDouble, [var face]) => SicBoBet.SpecificDouble(Stake, face),
            (SicBoBetKind.SpecificTriple, [var face]) => SicBoBet.SpecificTriple(Stake, face),
            (SicBoBetKind.Total, [var total]) => SicBoBet.ForTotal(Stake, total),
            (SicBoBetKind.TwoNumberCombination, [var first, var second]) =>
                SicBoBet.TwoNumberCombination(Stake, first, second),
            (_, []) => SicBoBet.CreateWithoutSelection(Kind, Stake),
            _ => throw new ArgumentException("The Sic Bo selections do not match the bet kind."),
        };
    }
}
