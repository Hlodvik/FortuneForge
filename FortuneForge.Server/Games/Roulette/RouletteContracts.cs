namespace FortuneForge.Server.Games.Roulette;

public sealed record PlaceRouletteBetRequest(string? Kind, decimal Stake, int? Number, int[]? Numbers);
public sealed record RouletteStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake, decimal StakeIncrement, decimal StartingBalance, string Mode);
public sealed record RouletteTablePlayerResponse(string SeatId, string DisplayName, bool IsCurrentPlayer);
public sealed record RouletteRoundResponse(
    Guid RoundId,
    decimal Balance,
    string Phase,
    IReadOnlyList<RouletteBetResponse> Bets,
    int? WinningPocket,
    IReadOnlyList<RouletteSettlementResponse> Settlements,
    string TableId,
    int RoundNumber,
    IReadOnlyList<RouletteTablePlayerResponse> Players);
public sealed record RouletteBetResponse(int BetIndex, string PlayerId, string Kind, decimal Stake, int? Number, IReadOnlyList<int> Numbers);
public sealed record RouletteSettlementResponse(string PlayerId, string Kind, decimal Stake, bool Won, decimal TotalReturn);
public sealed record RouletteErrorResponse(string Code, string Message);
