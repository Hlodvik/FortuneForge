namespace FortuneForge.Server.Games.Hearts;

public static class HeartsBackend
{
    public static string GameId => HeartsModule.Descriptor.Id;
    public static string ApiPath => HeartsModule.Descriptor.ApiBasePath ?? throw new InvalidOperationException("Hearts requires its API path.");
    public const string Owner = "hearts-package-v1";
    public const string FinancialProofContract = "hearts-protected-wallet-evidence-v1";
    internal const string FreeAdmissionSetting = "Features:HeartsEnabled";
}
