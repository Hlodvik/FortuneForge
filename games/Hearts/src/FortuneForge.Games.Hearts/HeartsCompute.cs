using FortuneForge.Games.Cards;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Hearts;

public static class HeartsCompute
{
    public const string StartRoute = "/internal/v1/matches/start";
    public const string PassRoute = "/internal/v1/matches/pass";
    public const string PlayRoute = "/internal/v1/matches/play";
    public const string NextRoundRoute = "/internal/v1/matches/next-round";
}

public sealed record HeartsComputeStartRequest(uint? Seed = null, int TargetScore = HeartsMatchEngine.DefaultTargetScore);
public sealed record HeartsComputePassRequest(HeartsMatchState PrivateState, PlayerSeat Seat, IReadOnlyList<PlayingCard> Cards);
public sealed record HeartsComputePlayRequest(HeartsMatchState PrivateState, PlayerSeat Seat, PlayingCard Card);
public sealed record HeartsComputeNextRoundRequest(HeartsMatchState PrivateState, uint? Seed = null);
