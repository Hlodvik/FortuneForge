using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Slots;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

internal sealed record SlotPreparedOperationRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost)
{
    internal static bool IsValidId(string? operationId) => operationId is { Length: >= 16 and <= 128 } &&
        operationId.All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_');
}

internal sealed class SlotPreparedOperationConflictException(string message, SlotPreparedOperationRequest? pendingOperation = null)
    : InvalidOperationException(message)
{
    public SlotPreparedOperationRequest? PendingOperation { get; } = pendingOperation;
}

internal sealed class SlotPreparedStartPausedException() : InvalidOperationException(
    "Starting new prepared Arcane Archives spins is paused.");

public sealed partial class FirestoreAccountStore
{
    private const string PreparedOperationIdField = "preparedOperationId";
    private const string PreparedOutcomeSha256Field = "preparedOutcomeSha256";
    private const string PreparedRoundJsonField = "preparedRoundJson";
    private const long PreparedRoundSchemaVersion = 1;

    private sealed record PreparedSlotOperation(DocumentReference Reference, string OperationId, string RequestSha256);

    private sealed record PreparedSlotRound(SlotSpinAdmission Admission, SpinResult Outcome, SlotSpinEngineAudit EngineAudit,
        bool TracksIngredientWagers, GameRequestObservation? RequestObservation);

    private PreparedSlotOperation PrepareOperation(string userId, string gameId, string operationId,
        long wagerPoints, decimal pointValueInCents, bool useFreeSpin, bool useSpecialBoost, int specialBoostCost)
    {
        if (!SlotPreparedOperationRequest.IsValidId(operationId))
            throw new ArgumentException("Spin operation IDs must contain 16 through 128 ASCII letters, digits, hyphens, or underscores.", nameof(operationId));

        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{gameId}\n{userId}\n{operationId}")));
        var terms = JsonSerializer.SerializeToUtf8Bytes(new
        {
            wagerPoints,
            pointValueInCents = pointValueInCents.ToString("G29", CultureInfo.InvariantCulture),
            useFreeSpin,
            useSpecialBoost,
            specialBoostCost,
        });
        return new(database.Collection("slotPreparedOperations").Document(key), operationId,
            Convert.ToHexStringLower(SHA256.HashData(terms)));
    }

    private async Task<SlotSpinAdmission> ReadPreparedOperationAsync(Transaction transaction, PreparedSlotOperation operation,
        DocumentSnapshot index, DocumentSnapshot guard, string userId, string gameId, CancellationToken cancellationToken)
    {
        if (ReadLong(index, "schemaVersion") != PreparedRoundSchemaVersion ||
            !Guid.TryParseExact(ReadString(index, "admissionId"), "N", out var admissionId))
            throw PreparedRoundUnavailable();
        if (ReadString(index, "requestSha256") != operation.RequestSha256)
            throw new SlotPreparedOperationConflictException("This spin operation ID was already used for different spin options.");

        var record = await transaction.GetSnapshotAsync(SlotSpinAdmissionDocument(admissionId), cancellationToken);
        if (!MatchesPreparedOperation(record, admissionId, userId, gameId, operation.OperationId) ||
            ReadString(record, PreparedRoundJsonField) is not { } json ||
            ReadString(record, PreparedOutcomeSha256Field) is not { } expectedOutcome)
            throw PreparedRoundUnavailable();

        PreparedSlotRound prepared;
        try
        {
            prepared = JsonSerializer.Deserialize<PreparedSlotRound>(json) ?? throw PreparedRoundUnavailable();
        }
        catch (JsonException)
        {
            throw PreparedRoundUnavailable();
        }

        if (prepared.Admission is not { } admission || prepared.Outcome is not { } savedOutcome || prepared.EngineAudit is not { } audit)
            throw PreparedRoundUnavailable();
        var outcome = savedOutcome with { EngineAudit = audit };
        if (admission.AdmissionId != admissionId || outcome.GameId != gameId || outcome.SpinId == Guid.Empty ||
            admission.WagerPoints != ReadLong(record, "effectiveWagerPoints") ||
            admission.ChargedWagerCents != ReadLong(record, "chargedWagerCents") ||
            outcome.ChargedWagerCents != admission.ChargedWagerCents ||
            admission.RequestId != ReadString(record, "requestId") ||
            admission.RequestId != prepared.RequestObservation?.RequestId ||
            EngineOutcomeSha256(outcome) != expectedOutcome)
            throw PreparedRoundUnavailable();

        var status = ReadString(record, "status");
        if (status == "admitted" ? ReadActivePreparedAdmission(guard) != admissionId.ToString("N") :
            status != "settled" || ReadString(record, "spinId") != outcome.SpinId.ToString("N"))
            throw PreparedRoundUnavailable();

        return admission with
        {
            ArcanePotionWagerTracked = prepared.TracksIngredientWagers,
            ResolvedOutcome = outcome,
            PreparedRequestObservation = prepared.RequestObservation,
        };
    }

    private async Task<SlotPreparedOperationConflictException> PendingPreparedOperationAsync(Transaction transaction,
        string activeAdmissionId, string userId, string gameId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(activeAdmissionId, "N", out var id))
            return PreparedRoundUnavailable();

        var record = await transaction.GetSnapshotAsync(SlotSpinAdmissionDocument(id), cancellationToken);
        var operationId = ReadString(record, PreparedOperationIdField);
        if (!SlotPreparedOperationRequest.IsValidId(operationId) ||
            !MatchesPreparedOperation(record, id, userId, gameId, operationId!) || ReadString(record, "status") != "admitted")
            return PreparedRoundUnavailable();

        var pending = new SlotPreparedOperationRequest(operationId!, record.GetValue<long>("requestedWagerPoints"),
            record.GetValue<bool>("isFreeSpin"), record.GetValue<bool>("specialBoostApplied"));
        return new("Finish the prepared Arcane Archives spin before starting a new spin.", pending);
    }

    private static void WritePreparedOperation(Transaction transaction, PreparedSlotOperation operation,
        SlotSpinAdmission admission, Dictionary<string, object> admissionData)
    {
        if (admission.ResolvedOutcome is not { EngineAudit: { } audit } outcome || outcome.SpinId == Guid.Empty ||
            outcome.WagerPoints != admission.WagerPoints || outcome.IsFreeSpin != admission.IsFreeSpin ||
            outcome.ChargedWagerCents != admission.ChargedWagerCents ||
            outcome.SpecialBoostApplied != admission.SpecialBoostApplied || audit.GeneratedAtUtc < admission.StartedAtUtc ||
            audit.PityMissesBefore != admission.PityMissesBefore || audit.EnergyBalanceBefore != admission.EnergyBalance ||
            outcome.GameId != admissionData["gameId"] as string)
            throw new InvalidOperationException("The prepared outcome does not match the Arcane Archives admission.");

        admissionData[PreparedOperationIdField] = operation.OperationId;
        admissionData[PreparedOutcomeSha256Field] = EngineOutcomeSha256(outcome);
        admissionData[PreparedRoundJsonField] = JsonSerializer.Serialize(new PreparedSlotRound(admission, outcome, audit,
            admission.ArcanePotionWagerTracked, admission.PreparedRequestObservation));

        // Retain this small pointer after raw-admission retention so a consumed key cannot start a second spin.
        transaction.Create(operation.Reference, new Dictionary<string, object>
        {
            ["admissionId"] = admission.AdmissionId.ToString("N"),
            ["requestSha256"] = operation.RequestSha256,
            ["schemaVersion"] = PreparedRoundSchemaVersion,
        });
    }

    private static string? ReadActivePreparedAdmission(DocumentSnapshot guard) =>
        ReadString(guard, SlotSpinGuard.ActiveAdmissionField) is { } active &&
        active == ReadString(guard, SlotSpinGuard.PreparedAdmissionField) ? active : null;

    private static bool MatchesPreparedOperation(DocumentSnapshot record, Guid admissionId, string userId,
        string gameId, string operationId) => record.Exists && ReadString(record, "admissionId") == admissionId.ToString("N") &&
        ReadString(record, "userId") == userId && ReadString(record, "gameId") == gameId &&
        ReadString(record, PreparedOperationIdField) == operationId;

    private static SlotPreparedOperationConflictException PreparedRoundUnavailable() =>
        new("The saved Arcane Archives spin is unavailable. Its operation ID cannot be reused for a new spin.");
}
