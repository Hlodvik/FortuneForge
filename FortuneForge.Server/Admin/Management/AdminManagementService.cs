using System.Globalization;
using FortuneForge.Server.Admin.Operations;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Admin.Management;

internal sealed class AdminManagementService(IAdminManagementStore store, IOptions<AdminManagementOptions> options)
{
    public async Task<AdminManagementResponse> GetAsync(CancellationToken cancellationToken) => (await store.ReadAsync(cancellationToken)).Response();
    public async Task<CatalogVisibilityResponse> VisibilityAsync(CancellationToken cancellationToken)
    {
        var state = await store.ReadAsync(cancellationToken);
        return new(state.Version, AdminManagementCatalog.Games.Select(game => new CatalogVisibilityGame(game.Id, game.Route, state.IsVisible(game))).ToArray());
    }

    public Task<AdminManagementResponse> ChangeAsync(
        string actorId, string key, string operation, string? gameId, bool? enabled,
        long? expectedVersion, string? reason, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorId)) throw new AdminManagementInputException("An administrator account is required.");
        if (key.Length is < 16 or > 128 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
            throw new AdminManagementInputException("Send a unique Idempotency-Key containing 16 to 128 letters, digits, dots, colons, dashes or underscores.");
        if (enabled is null || expectedVersion is null or < 0) throw new AdminManagementInputException("A boolean switch value and current nonnegative expectedVersion are required.");
        reason = reason?.Trim();
        if (reason is null || reason.Length is < 10 or > 500 || reason.Any(char.IsControl))
            throw new AdminManagementInputException("Enter a reason of 10 to 500 characters without control characters.");
        if (operation != "bots-global")
        {
            var game = AdminManagementCatalog.Require(gameId ?? string.Empty);
            if (operation == "bots-game" && !game.BotsSupported) throw new AdminManagementInputException("This game has no managed-player adapter.");
            if (operation is not "bots-game" and not "catalog-visibility") throw new AdminManagementInputException("The management operation is not supported.");
        }
        else if (gameId is not null) throw new AdminManagementInputException("A global switch cannot name a game.");
        return store.MutateAsync(new(actorId, key, operation, gameId, enabled.Value, expectedVersion.Value, reason,
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)), cancellationToken);
    }

    public async Task<AdminManagementAuditPage> AuditAsync(int limit, string? cursor, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new AdminManagementInputException("limit must be from 1 through 100.");
        var codec = new AdminOperationsCursor(options.Value.CursorSigningKey!);
        long? version = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                var decoded = codec.Decode("management-audit", cursor);
                if (!long.TryParse(decoded.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                    throw new AdminManagementInputException("The audit cursor is invalid.");
                version = parsed;
            }
            catch (AdminOperationsQueryException) { throw new AdminManagementInputException("The audit cursor is invalid."); }
        }
        var rows = await store.AuditAsync(limit + 1, version, cancellationToken);
        var items = rows.Take(limit).ToArray();
        return new(items, rows.Count > limit ? codec.Encode("management-audit", items[^1].OccurredAtUtc,
            items[^1].Version.ToString(CultureInfo.InvariantCulture)) : null);
    }
}
