namespace FortuneForge.Games.Abstractions;

public sealed class LegacySlotAdmissionPendingException() : Exception("A host-owned slot admission is still open.")
{
    public const string Code = "legacy-slot-admission-pending";
}
