using System.Text.Json.Serialization;

namespace FortuneForge.Server.Arcade.Competition;

public sealed record ArcadeCompetitionPaidEntryRequest(
    string AttemptId,
    ArcadeCompetitionIdentity Competition,
    string AuthenticatedPlayerId,
    long EntryFeeCents,
    DateTimeOffset EnteredAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestBodySha256 = null);

public sealed class ArcadeCompetitionInsufficientCreditsException(long availableCents, long requiredCents) : Exception(
    "The authenticated player does not have enough slots credits for this arcade competition entry.")
{
    public long AvailableCents { get; } = availableCents;
    public long RequiredCents { get; } = requiredCents;
}

public sealed class ArcadeCompetitionSettlementCompletedException() : InvalidOperationException(
    "This arcade competition has completed settlement and is read-only.");
