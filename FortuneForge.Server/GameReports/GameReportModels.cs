using System.Text.Json.Serialization;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.GameReports;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateGameReportRequest(string? Category, string? Message, string? Path);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateGameReportRequest(string? Status, long? ExpectedVersion, string? Note);
public sealed record GameReportReceipt(string Id, string Status, DateTime CreatedAtUtc);
public sealed record GameReportRecord(string Id, string UserId, string GameId, string GameName, string Path,
    string Category, string Message, string Status, long Version, DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc, string AdminNote, string? UpdatedBy);
public sealed record GameReportsPage(IReadOnlyList<GameReportRecord> Items, string? NextCursor);
internal sealed record GameReportDraft(string Id, string UserId, string GameId, string GameName,
    string Path, string Category, string Message, string Fingerprint, DateTime NowUtc);
internal sealed record GameReportChange(string Id, string Status, long ExpectedVersion, string Note, string ActorId, DateTime NowUtc);
internal sealed class GameReportInputException(string message) : Exception(message);
internal sealed class GameReportConflictException(string message) : Exception(message);
internal interface IGameReportAccountResolver
{
    Task<AccountAccessContext?> ResolveAsync(HttpRequest request, CancellationToken cancellationToken);
}
internal interface IGameReportsStore
{
    Task<GameReportRecord> CreateAsync(GameReportDraft draft, CancellationToken cancellationToken);
    Task<IReadOnlyList<GameReportRecord>> ListAsync(int limit, DateTime? beforeUtc, string? beforeId, CancellationToken cancellationToken);
    Task<GameReportRecord?> GetAsync(string id, CancellationToken cancellationToken);
    Task<GameReportRecord?> UpdateAsync(GameReportChange change, CancellationToken cancellationToken);
}
