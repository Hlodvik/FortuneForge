namespace FortuneForge.Server.Accounts.Models;

public sealed class NeonNitroChoiceConflictException(string message) : Exception(message);

public sealed record NeonNitroChoiceFinalization(
    FortuneForge.Server.Slots.Models.SpinResult Result,
    SlotSpinSettlement Settlement);
