namespace FortuneForge.Games.TrickTaking;

public static class PlayerSeatExtensions
{
    public static PlayerSeat Next(this PlayerSeat seat) => seat.Advance(1);

    public static PlayerSeat Advance(this PlayerSeat seat, int positions)
    {
        if (!Enum.IsDefined(seat))
            throw new ArgumentOutOfRangeException(nameof(seat));
        if (positions < 0)
            throw new ArgumentOutOfRangeException(nameof(positions));

        return (PlayerSeat)(((int)seat + positions) % TrickTakingRules.PlayerCount);
    }

    public static PlayerSeat Partner(this PlayerSeat seat) => seat.Advance(2);
}
