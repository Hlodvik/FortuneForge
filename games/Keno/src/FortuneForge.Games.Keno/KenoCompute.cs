namespace FortuneForge.Games.Keno;

public static class KenoCompute
{
    public const string ResolveRoute = "/internal/v1/rounds/resolve";

    public static KenoComputeResult Resolve(KenoComputeRequest request)
    {
        var played = KenoRoundEngine.Play(new KenoTicket(request.Ticket), new KenoDraw(request.Draw),
            StandardKenoPaytable.Instance);
        var outcome = played.Result.PaytableOutcome
            ?? throw new InvalidOperationException("The Keno paytable did not resolve the round.");
        return new KenoComputeResult(played.Result.HitCount, outcome);
    }
}

public sealed record KenoComputeRequest(IReadOnlyList<int> Ticket, IReadOnlyList<int> Draw);
public sealed record KenoComputeResult(int HitCount, KenoPaytableOutcome PaytableOutcome);
