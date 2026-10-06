using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Roulette;

namespace FortuneForge.Server.Bots.Roulette;

internal static class RouletteManagedPlayers
{
    public static RouletteBet Bet(string profileId, int roundNumber, decimal balance)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"roulette-managed-bet\n{profileId}\n{roundNumber}"));
        var stake = Math.Min(balance, 1m + digest[0] % 5);
        var kind = (digest[1] % 4) switch
        {
            0 => RouletteBetKind.Red,
            1 => RouletteBetKind.Black,
            2 => RouletteBetKind.Even,
            _ => RouletteBetKind.Odd
        };
        return new RouletteBet(profileId, kind, stake, null, []);
    }
}
