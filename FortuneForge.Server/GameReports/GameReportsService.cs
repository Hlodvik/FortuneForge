using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Admin.Operations;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.GameReports;

internal sealed class GameReportsService(IGameReportsStore store, IOptions<AdminOperationsOptions> options)
{
    internal static readonly string[] Categories = ["bug", "suggestion", "other"];
    internal static readonly string[] Statuses = ["open", "in-review", "resolved", "closed"];

    public async Task<GameReportReceipt> CreateAsync(string userId, string key, CreateGameReportRequest request, DateTime nowUtc, CancellationToken ct)
    {
        if (key.Length is < 16 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new GameReportInputException("A valid report request key is required.");
        if (!Categories.Contains(request.Category, StringComparer.Ordinal)) throw new GameReportInputException("Choose bug, suggestion, or other.");
        var message = Text(request.Message, 10, 3000, "Describe your report using 10 to 3,000 characters.", multiline: true);
        if (request.Path is null || request.Path.Length > 200 || request.Path.Any(char.IsControl) || request.Path.Contains('?') || request.Path.Contains('#'))
            throw new GameReportInputException("A recognised game page is required; do not include query strings.");
        var path = request.Path.TrimEnd('/');
        var game = AdminManagementCatalog.Games.SingleOrDefault(item => item.Route == path)
            ?? throw new GameReportInputException("Reports must be submitted from a recognised game page.");
        var fingerprint = Hash(JsonSerializer.Serialize(new { request.Category, message, path }));
        var record = await store.CreateAsync(new(Hash($"game-report-v1\n{userId}\n{key}"), userId, game.Id, game.DisplayName,
            path, request.Category!, message, fingerprint, nowUtc), ct);
        // Players receive only a receipt, never internal administrator notes or status audit data.
        return new(record.Id, record.Status, record.CreatedAtUtc);
    }

    public async Task<GameReportsPage> ListAsync(int limit, string? cursor, CancellationToken ct)
    {
        if (limit is < 1 or > 50) throw new GameReportInputException("Choose a page size from 1 through 50.");
        var codec = new AdminOperationsCursor(options.Value.CursorSigningKey);
        (DateTime OccurredAtUtc, string Id)? before = null;
        if (cursor is not null)
        {
            before = codec.Decode("game-reports", cursor);
            ValidateId(before.Value.Id);
        }
        var rows = await store.ListAsync(limit + 1, before?.OccurredAtUtc, before?.Id, ct);
        var items = rows.Take(limit).ToArray();
        var next = rows.Count > limit ? codec.Encode("game-reports", items[^1].CreatedAtUtc, items[^1].Id) : null;
        return new(items, next);
    }

    public Task<GameReportRecord?> GetAsync(string id, CancellationToken ct) { ValidateId(id); return store.GetAsync(id, ct); }
    public Task<GameReportRecord?> UpdateAsync(string id, string actorUserId, UpdateGameReportRequest request, DateTime nowUtc, CancellationToken ct)
    {
        ValidateId(id);
        if (!Statuses.Contains(request.Status, StringComparer.Ordinal) || request.ExpectedVersion is null or < 0)
            throw new GameReportInputException("Choose a valid report status and current revision.");
        var note = Text(request.Note ?? "", 0, 1000, "Internal notes may contain at most 1,000 characters.", multiline: true);
        return store.UpdateAsync(new(id, request.Status!, request.ExpectedVersion.Value, note, Hash($"report-admin\n{actorUserId}"), nowUtc), ct);
    }
    internal static void ValidateId(string id)
    {
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new GameReportInputException("The report reference is invalid.");
    }
    private static string Text(string? value, int minimum, int maximum, string error, bool multiline)
    {
        var text = value?.Trim();
        if (text is null || text.Length < minimum || text.Length > maximum || text.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')))
            throw new GameReportInputException(error);
        return text;
    }
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
