namespace FortuneForge.Server.Admin.Operations;

public sealed class AdminOperationsOptions
{
    public const string SectionName = "AdminOperations";
    public int MaximumRangeDays { get; set; } = 31;
    public int MaximumDocumentsPerCollection { get; set; } = 5_000;
    public string CursorSigningKey { get; set; } = string.Empty;
}

internal static class AdminOperationsFeature
{
    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue("Features:AdminOperationsEnabled", false);
}
