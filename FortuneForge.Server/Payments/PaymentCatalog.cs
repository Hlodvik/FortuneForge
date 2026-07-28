using FortuneForge.Server.Payments.Models;

namespace FortuneForge.Server.Payments;

public static class PaymentCatalog
{
    private static readonly IReadOnlyList<PaymentMethodOption> RegionalBankTransferMethods =
    [
        new(
            "regional-bank-transfer",
            "bank_transfer",
            "Regional bank transfer",
            "Use the invoice reference when making a transfer from a supported local bank.",
            "Manual confirmation")
    ];

    private static readonly IReadOnlyList<long> RegionalSuggestedAmounts = [10, 20, 50, 100, 500];

    public static IReadOnlyList<PaymentMarketOption> Markets { get; } =
    [
        new(
            "ZA",
            "South Africa",
            "ZAR",
            "en-ZA",
            "For players in South Africa paying in South African rand (ZAR).",
            "This pathway is specifically for South African bank transfers in ZAR.",
            10,
            100_000,
            10,
            RegionalSuggestedAmounts,
            RegionalBankTransferMethods),
        new(
            "LS",
            "Lesotho",
            "LSL",
            "en-LS",
            "For players in Lesotho paying in Lesotho loti (LSL).",
            "This pathway is specifically for Lesotho bank transfers in LSL. The loti is pegged to the rand, but this checkout records LSL explicitly.",
            10,
            100_000,
            10,
            RegionalSuggestedAmounts,
            RegionalBankTransferMethods)
    ];
}
