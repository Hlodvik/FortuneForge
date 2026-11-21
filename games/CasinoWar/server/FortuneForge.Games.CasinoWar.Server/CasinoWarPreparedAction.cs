using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CasinoWar;
using static FortuneForge.Server.Cards.CasinoWar.CasinoWarRoundCodec;

namespace FortuneForge.Server.Cards.CasinoWar;

internal sealed record CasinoWarPreparedAction(string Id, string AccountId, string RoundId, string Key,
    string Kind, CasinoWarSavedRound? Before, CasinoWarSavedRound After, DateTimeOffset PreparedAt,
    GameRequestObservation? Observation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CasinoWarFinancialSnapshot? PriorFinancialEvidence = null)
{
    [JsonIgnore] internal string WalletOperationId => Hash(CasinoWarModule.GameId + "\n" + Id);
    [JsonIgnore] internal string Digest => Hash(Serialize(this));
    [JsonIgnore] internal long WagerCents => Kind == "opening"
        ? checked(After.PrimaryStakeCents + After.TieStakeCents)
        : After.Decision == "go-to-war" ? After.PrimaryStakeCents : 0;
    [JsonIgnore] internal long PayoutCents
    {
        get
        {
            var saved = After.Restore();
            if (Kind == "decision") return CasinoWarMoney.ToCents(saved.Round.Settlement!.TotalReturn);
            var opening = CasinoWarRoundEngine.Start(saved.Deck, saved.Round.PrimaryStake);
            return checked(CasinoWarMoney.ToCents(opening.Settlement?.TotalReturn ?? 0m) +
                CasinoWarMoney.ToCents(saved.TieSettlement?.TotalReturn ?? 0m));
        }
    }
    internal GameWalletSettlementRequest WalletRequest() => new(WalletOperationId, Digest, WagerCents, PayoutCents);
}

internal sealed record CasinoWarActionCompletion(long BalanceCents, GameWalletSettlementReceipt? Wallet, bool ImportedLegacy = false);
internal sealed record CasinoWarActionRecord(CasinoWarPreparedAction Prepared, CasinoWarActionCompletion? Completion);
internal sealed class CasinoWarAdmissionPausedException() : Exception("Casino War is not accepting new paid rounds.");
internal sealed class CasinoWarPendingActionException(string roundId)
    : Exception("Recover the pending Casino War action before making another decision.")
{
    internal string RoundId { get; } = roundId;
}

public sealed record CasinoWarRecoveryResponse(CasinoWarRoundResponse? RecoveredRound,
    IReadOnlyList<CasinoWarRoundResponse> OpenRounds);
