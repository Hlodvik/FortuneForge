using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using System.Text.Json.Serialization;

namespace FortuneForge.Server.Cards.HouseHoldem;

internal sealed record HouseHoldemPreparedCommand(string Id, string AccountId, string RoundId,
    string IdempotencyKey, string Command, long AnteCents, long TripsCents,
    HouseHoldemStoredRound? Before, HouseHoldemStoredRound After, string StartKey,
    long WagerCents, long PayoutCents, long MinimumBalanceBeforeCents,
    DateTimeOffset PreparedAt, GameRequestObservation? Observation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HouseHoldemFinancialSnapshot? PriorFinancialEvidence = null)
{
    internal string? FrozenSha256 { get; init; }
    internal string Digest => FrozenSha256 ?? HouseHoldemRoundCodec.Hash(HouseHoldemRoundCodec.Serialize(this));
    internal string WalletOperationId => $"{HouseHoldemBackend.GameId}-{Id}";
    internal GameWalletSettlementRequest WalletRequest() => new(WalletOperationId, Digest, WagerCents, PayoutCents,
        MinimumBalanceBeforeCents: MinimumBalanceBeforeCents);
}

internal sealed record HouseHoldemCommandCompletion(HouseHoldemStoreResult? Result,
    GameWalletSettlementReceipt? Wallet, bool LegacyAlreadyApplied = false, long RequiredCents = 0);
internal sealed record HouseHoldemCommandRecord(HouseHoldemPreparedCommand Prepared, HouseHoldemCommandCompletion? Completion);
