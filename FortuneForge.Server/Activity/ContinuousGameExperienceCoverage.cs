using System.Text.RegularExpressions;
using FortuneForge.Games.Abstractions.Analytics;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Activity;

internal static class GameExperienceOrigin
{
    internal const string Contract = "fresh-firebase-account-transaction-v1";

    // Called only while creating the fresh Firebase user's account document.
    // The timestamp is resolved by that same Firestore commit, not account age.
    internal static Dictionary<string, object> Create(string? apiRevision, string? sourceRevision) => new()
    {
        ["schemaVersion"] = 1L, ["originContract"] = Contract,
        ["originId"] = Guid.NewGuid().ToString("N"),
        ["apiRevision"] = apiRevision ?? "", ["sourceRevision"] = sourceRevision ?? "",
        ["captureVersion"] = AcceptedGameParticipationDocuments.CaptureVersion,
        ["committedAtUtc"] = FieldValue.ServerTimestamp,
    };
}

internal sealed record ContinuousGameExperienceEligibility(string EpochId, string OriginId,
    DateTime OriginAtUtc, DateTime VerifiedAtUtc, DateTime ValidUntilUtc, string ApiRevision,
    string SourceRevision, IReadOnlyList<string> GameIds)
{
    internal IReadOnlyList<GameExperienceCoverageProof> Proofs(DateTime asOf, DateTime now) =>
        asOf < OriginAtUtc || asOf < VerifiedAtUtc || asOf > now || ValidUntilUtc <= now ? [] :
            GameIds.Select(game => new GameExperienceCoverageProof(game, EpochId, asOf,
                ApiRevision, SourceRevision, ValidUntilUtc, ContinuousGameExperienceCoverage.SourceVersion,
                ContinuousGameExperienceCoverage.SourceProofSha256, "canonicalCapture", OriginId)).ToArray();
}

// This ledger is operator-owned. Merely deploying this reader does not enable
// negative claims, and a retired/gapped epoch can never validate an old origin.
internal sealed class ContinuousGameExperienceCoverage(FirestoreDb database, TimeProvider clock, IConfiguration? configuration)
{
    internal const string PointerCollection = "catalogGameExperienceCoverage";
    internal const string PointerDocument = "canonical-capture-v1";
    internal const string EpochCollection = "catalogGameExperienceCoverageEpochs";
    internal const string SourceVersion = "canonical-capture-epoch-v1";
    internal static readonly string SourceProofSha256 = AcceptedGameActivityReader.Hash(
        SourceVersion + "\n" + GameExperienceOrigin.Contract + "\n" + AcceptedGameParticipationDocuments.CaptureVersion +
        "\nfresh-account-create-server-commit;continuous-epoch;all-serving-writers;no-retired-or-gap\n" +
        string.Join("\n", PlayerGameExperienceProjection.GameIds.Order(StringComparer.Ordinal)));

    internal async Task<ContinuousGameExperienceEligibility?> ReadAsync(string userId, CancellationToken ct)
    {
        var revision = configuration?["K_REVISION"];
        var commit = configuration?["FF_RELEASE_SHA"];
        if (!Identifier(revision) || !Commit(commit)) return null;
        var userTask = database.Collection("users").Document(userId).GetSnapshotAsync(ct);
        var pointerTask = database.Collection(PointerCollection).Document(PointerDocument).GetSnapshotAsync(ct);
        await Task.WhenAll(userTask, pointerTask);
        if (!userTask.Result.Exists || !pointerTask.Result.Exists) return null;
        var pointer = pointerTask.Result.ToDictionary();
        var epochId = Text(pointer, "epochId");
        if (!Schema(pointer) || Text(pointer, "status") != "active" || !Identifier(epochId, 48)) return null;
        var epoch = await database.Collection(EpochCollection).Document(epochId!).GetSnapshotAsync(ct);
        return epoch.Exists ? Validate(Map(userTask.Result.ToDictionary(), "gameExperienceOrigin"), pointer,
            epoch.ToDictionary(), revision!, commit!, clock.GetUtcNow().UtcDateTime) : null;
    }

    internal static ContinuousGameExperienceEligibility? Validate(IReadOnlyDictionary<string, object> origin,
        IReadOnlyDictionary<string, object> pointer, IReadOnlyDictionary<string, object> epoch,
        string revision, string commit, DateTime now)
    {
        if (!Identifier(revision) || !Commit(commit) || !Schema(origin) || !Schema(pointer) || !Schema(epoch) ||
            Text(pointer, "status") != "active" || Text(epoch, "status") != "active" ||
            Text(pointer, "epochId") is not { } epochId || !Identifier(epochId, 48) || Text(epoch, "epochId") != epochId ||
            epoch.ContainsKey("retiredAtUtc") || epoch.ContainsKey("gapAtUtc") || epoch.ContainsKey("revokedAtUtc") ||
            Text(epoch, "sourceVersion") != SourceVersion || Text(epoch, "sourceProofSha256") != SourceProofSha256 ||
            Text(epoch, "captureVersion") != AcceptedGameParticipationDocuments.CaptureVersion ||
            Text(epoch, "originContract") != GameExperienceOrigin.Contract ||
            Text(origin, "originContract") != GameExperienceOrigin.Contract ||
            Text(origin, "captureVersion") != AcceptedGameParticipationDocuments.CaptureVersion ||
            Text(origin, "originId") is not { } originId || !Guid.TryParseExact(originId, "N", out _) ||
            Text(origin, "apiRevision") is not { } originRevision || !Identifier(originRevision) ||
            Text(origin, "sourceRevision") is not { } originCommit || !Commit(originCommit) ||
            !Time(origin, "committedAtUtc", out var created) || !Time(epoch, "continuousFromUtc", out var start) ||
            !Time(epoch, "verifiedAtUtc", out var verified) || !Time(epoch, "validUntilUtc", out var until) ||
            created < start || created > now || verified < start || verified > now || until <= now || until > verified.AddHours(24) ||
            !Strings(epoch, "canonicalGameIds", out var inventory) ||
            !inventory.ToHashSet(StringComparer.Ordinal).SetEquals(PlayerGameExperienceProjection.GameIds) ||
            !Strings(epoch, "servingApiRevisions", out var serving) || serving.Length == 0 ||
            !serving.Contains(revision, StringComparer.Ordinal) ||
            !epoch.TryGetValue("writerRevisions", out var raw) || raw is not IEnumerable<object> entries) return null;
        var all = entries.ToArray(); var writers = all.OfType<Dictionary<string, object>>().ToArray();
        if (writers.Length == 0 || writers.Length != all.Length ||
            writers.Any(writer => !Identifier(Text(writer, "apiRevision")) || !Commit(Text(writer, "sourceRevision"))) ||
            writers.Select(writer => Text(writer, "apiRevision")).Distinct(StringComparer.Ordinal).Count() != writers.Length ||
            serving.Any(active => !writers.Any(writer => Text(writer, "apiRevision") == active))) return null;
        var current = writers.SingleOrDefault(writer => Text(writer, "apiRevision") == revision);
        var original = writers.SingleOrDefault(writer => Text(writer, "apiRevision") == originRevision);
        if (current is null || Text(current, "sourceRevision") != commit || original is null ||
            Text(original, "sourceRevision") != originCommit || Text(original, "originContract") != GameExperienceOrigin.Contract) return null;
        var closure = Map(epoch, "gameCaptureVersions");
        if (closure.Keys.Any(game => !PlayerGameExperienceProjection.GameIds.Contains(game))) return null;
        var games = PlayerGameExperienceProjection.GameIds.Where(game =>
            Text(closure, game) == AcceptedGameParticipationDocuments.CaptureVersion &&
            writers.All(writer => Text(Map(writer, "gameCaptureVersions"), game) == AcceptedGameParticipationDocuments.CaptureVersion))
            .Order(StringComparer.Ordinal).ToArray();
        return games.Length == 0 ? null : new(epochId, originId, created, verified, until, revision, commit, games);
    }

    private static bool Schema(IReadOnlyDictionary<string, object> value) =>
        value.TryGetValue("schemaVersion", out var version) && version is 1L;
    private static bool Identifier(string? value, int max = 128) => value is { Length: > 0 } && value.Length <= max &&
        Regex.IsMatch(value, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);
    private static bool Commit(string? value) => value is not null && Regex.IsMatch(value, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static string? Text(IReadOnlyDictionary<string, object> value, string key) =>
        value.TryGetValue(key, out var field) ? field as string : null;
    private static Dictionary<string, object> Map(IReadOnlyDictionary<string, object> value, string key) =>
        value.TryGetValue(key, out var field) && field is Dictionary<string, object> map ? map : new();
    private static bool Strings(IReadOnlyDictionary<string, object> value, string key, out string[] strings)
    {
        strings = [];
        if (!value.TryGetValue(key, out var raw) || raw is not IEnumerable<object> values) return false;
        var all = values.ToArray(); strings = all.OfType<string>().ToArray();
        return strings.Length == all.Length && strings.All(item => Identifier(item)) &&
            strings.Distinct(StringComparer.Ordinal).Count() == strings.Length;
    }
    private static bool Time(IReadOnlyDictionary<string, object> value, string key, out DateTime at)
    {
        at = default;
        if (!value.TryGetValue(key, out var raw) || raw is not Timestamp timestamp) return false;
        at = timestamp.ToDateTime(); return true;
    }
}
