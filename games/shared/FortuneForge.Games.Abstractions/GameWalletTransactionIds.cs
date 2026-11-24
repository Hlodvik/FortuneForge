using System;

namespace FortuneForge.Games.Abstractions.Wallet;

/// <summary>The wallet's ledger naming contract is independent of legacy entitlement tracking.</summary>
public enum GameWalletTransactionIdLayout
{
    LegacyOperation,
    StandardSettlement,
}

public readonly record struct GameWalletTransactionIds(string Prefix)
{
    public string WagerId => Prefix + "-wager";
    public string PayoutId => Prefix + "-payout";
    public string LegacyFreeGameUseId => Prefix + "-free-game-use";

    public static GameWalletTransactionIdLayout ValidateLayout(GameWalletTransactionIdLayout layout) => layout switch
    {
        GameWalletTransactionIdLayout.LegacyOperation or GameWalletTransactionIdLayout.StandardSettlement => layout,
        _ => throw new ArgumentOutOfRangeException(nameof(layout), "Choose a supported wallet transaction ID layout."),
    };

    public static GameWalletTransactionIds Create(string operationId, string baseSettlementId, GameWalletTransactionIdLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseSettlementId);
        return new(ValidateLayout(layout) == GameWalletTransactionIdLayout.LegacyOperation ? operationId : baseSettlementId);
    }
}
