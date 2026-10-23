namespace FortuneForge.Server.Admin.Management;

public sealed record AdminManagementGame(
    string Id, string DisplayName, string Category, string Route,
    bool Visible, bool BotsSupported, bool BotsEnabled, bool EffectiveBotsEnabled);
public sealed record AdminManagementResponse(
    long Version, bool BotsEnabled, IReadOnlyList<AdminManagementGame> Games, DateTime? UpdatedAtUtc);
public sealed record CatalogVisibilityGame(string Id, string Route, bool Visible);
public sealed record CatalogVisibilityResponse(long Version, IReadOnlyList<CatalogVisibilityGame> Games);
public sealed record AdminBotSwitchRequest(bool? Enabled, long? ExpectedVersion, string? Reason);
public sealed record AdminVisibilityRequest(bool? Visible, long? ExpectedVersion, string? Reason);
public sealed record AdminManagementAuditItem(
    string Id, long Version, string Operation, string? GameId, string ActorId,
    DateTime OccurredAtUtc, string Reason, bool Before, bool After);
public sealed record AdminManagementAuditPage(IReadOnlyList<AdminManagementAuditItem> Items, string? NextCursor);

internal sealed record AdminRuntimeState(
    long Version, bool BotsEnabled, IReadOnlyDictionary<string, bool> GameBots,
    IReadOnlyDictionary<string, bool> Visibility, DateTime? UpdatedAtUtc)
{
    public static AdminRuntimeState Default => new(0, true,
        new Dictionary<string, bool>(StringComparer.Ordinal),
        new Dictionary<string, bool>(StringComparer.Ordinal), null);

    public bool AllowsBots(string gameId) => BotsEnabled && GameBots.GetValueOrDefault(gameId, true);
    public bool IsVisible(AdminManagedGame game) => Visibility.GetValueOrDefault(game.Id, game.DefaultVisible);
    public AdminManagementResponse Response() => new(Version, BotsEnabled,
        AdminManagementCatalog.Games.Select(game => new AdminManagementGame(
            game.Id, game.DisplayName, game.Category, game.Route, IsVisible(game),
            game.BotsSupported, game.BotsSupported && GameBots.GetValueOrDefault(game.Id, true),
            game.BotsSupported && AllowsBots(game.Id))).ToArray(), UpdatedAtUtc);
}

internal sealed record AdminManagementMutation(
    string ActorUserId, string IdempotencyKey, string Operation, string? GameId,
    bool Value, long ExpectedVersion, string Reason, DateTime NowUtc);
internal sealed class AdminManagementInputException(string message) : Exception(message);
internal sealed class AdminManagementConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal interface IAdminManagementStore
{
    Task<AdminRuntimeState> ReadAsync(CancellationToken cancellationToken);
    Task<AdminManagementResponse> MutateAsync(AdminManagementMutation mutation, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminManagementAuditItem>> AuditAsync(int limit, long? beforeVersion, CancellationToken cancellationToken);
}

/// <summary>Only new assignments consult this policy; renewals and active turns do not.</summary>
internal interface IManagedPlayerAdmissionPolicy
{
    Task<bool> AllowsNewAssignmentsAsync(string gameId, CancellationToken cancellationToken);
}
