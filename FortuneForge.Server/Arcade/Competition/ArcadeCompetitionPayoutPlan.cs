using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Arcade.Competition;

internal enum ArcadeCompetitionPayoutKind
{
    Prize,
    Refund,
}

internal sealed record ArcadeCompetitionCreditInstruction
{
    public ArcadeCompetitionCreditInstruction(
        ArcadeCompetitionPayoutKind kind,
        string playerId,
        int? placement,
        long amountCents,
        string idempotencyKey)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("A payout instruction requires a player id.", nameof(playerId));
        if (kind == ArcadeCompetitionPayoutKind.Prize && placement is null or <= 0)
            throw new ArgumentOutOfRangeException(nameof(placement), "A prize instruction requires a positive placement.");
        if (kind == ArcadeCompetitionPayoutKind.Refund && placement is not null)
            throw new ArgumentException("A refund instruction cannot have a placement.", nameof(placement));
        if (amountCents <= 0) throw new ArgumentOutOfRangeException(nameof(amountCents), "Payout instructions must be positive.");
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("A payout instruction requires an idempotency key.", nameof(idempotencyKey));

        Kind = kind;
        PlayerId = playerId;
        Placement = placement;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
    }

    public ArcadeCompetitionPayoutKind Kind { get; }
    public string PlayerId { get; }
    public int? Placement { get; }
    public long AmountCents { get; }
    public string IdempotencyKey { get; }
}

internal sealed record ArcadeCompetitionPayoutPlan
{
    private ArcadeCompetitionPayoutPlan(
        ArcadeCompetitionIdentity competition,
        ImmutableArray<ArcadeCompetitionCreditInstruction> instructions,
        long visibleJackpotCents,
        long houseCutCents)
    {
        Competition = competition;
        Instructions = instructions;
        VisibleJackpotCents = visibleJackpotCents;
        HouseCutCents = houseCutCents;
    }

    public ArcadeCompetitionIdentity Competition { get; }
    public ImmutableArray<ArcadeCompetitionCreditInstruction> Instructions { get; }
    public long VisibleJackpotCents { get; }
    public long HouseCutCents { get; }
    public long TotalCreditsCents => Instructions.Sum(instruction => instruction.AmountCents);

    internal static ArcadeCompetitionPayoutPlan Restore(
        ArcadeCompetitionIdentity competition,
        ImmutableArray<ArcadeCompetitionCreditInstruction> instructions,
        long visibleJackpotCents,
        long houseCutCents)
    {
        ArgumentNullException.ThrowIfNull(competition);
        if (visibleJackpotCents < 0) throw new ArgumentOutOfRangeException(nameof(visibleJackpotCents));
        if (houseCutCents < 0) throw new ArgumentOutOfRangeException(nameof(houseCutCents));
        if (instructions.IsDefault) throw new ArgumentException("Stored payout instructions are required.", nameof(instructions));
        if (instructions.Any(instruction => instruction is null))
            throw new ArgumentException("Stored payout instructions cannot contain null values.", nameof(instructions));
        if (instructions.Any(instruction => instruction.IdempotencyKey != CreateIdempotencyKey(
                competition,
                instruction.Kind,
                instruction.PlayerId,
                instruction.Placement)))
        {
            throw new ArgumentException("A stored payout instruction has an invalid idempotency key.", nameof(instructions));
        }

        ValidateInstructionSet(instructions, visibleJackpotCents, houseCutCents, nameof(instructions));
        return new ArcadeCompetitionPayoutPlan(competition, instructions, visibleJackpotCents, houseCutCents);
    }

    internal bool HasSameValueAs(ArcadeCompetitionPayoutPlan other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Competition == other.Competition &&
            VisibleJackpotCents == other.VisibleJackpotCents &&
            HouseCutCents == other.HouseCutCents &&
            Instructions.SequenceEqual(other.Instructions);
    }

    internal static bool HasValidIdempotencyKey(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(competition);
        ArgumentNullException.ThrowIfNull(instruction);
        return instruction.IdempotencyKey == CreateIdempotencyKey(
            competition,
            instruction.Kind,
            instruction.PlayerId,
            instruction.Placement);
    }

    public static ArcadeCompetitionPayoutPlan Create(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionRulesResult result)
    {
        ArgumentNullException.ThrowIfNull(competition);
        ArgumentNullException.ThrowIfNull(result);
        ValidateCompetition(competition, result.Window);

        if (result.TotalEntryFeesCents < 0 || result.VisibleJackpotCents < 0 || result.HouseCutCents < 0)
            throw new ArgumentException("Competition payout totals cannot be negative.", nameof(result));
        if (checked(result.VisibleJackpotCents + result.HouseCutCents) != result.TotalEntryFeesCents)
            throw new ArgumentException("The visible jackpot plus house cut must equal total entry fees.", nameof(result));

        var instructions = result.PrizeAllocations
            .Select(allocation => CreateInstruction(
                competition,
                ArcadeCompetitionPayoutKind.Prize,
                allocation.PlayerId,
                allocation.Position,
                allocation.AmountCents))
            .Concat(result.Refunds.Select(refund => CreateInstruction(
                competition,
                ArcadeCompetitionPayoutKind.Refund,
                refund.PlayerId,
                placement: null,
                refund.AmountCents)))
            .OrderBy(instruction => instruction.Kind)
            .ThenBy(instruction => instruction.Placement ?? int.MaxValue)
            .ThenBy(instruction => instruction.PlayerId, StringComparer.Ordinal)
            .ToImmutableArray();

        if (instructions.Select(instruction => instruction.PlayerId).Distinct(StringComparer.Ordinal).Count() != instructions.Length)
            throw new ArgumentException("A player cannot receive more than one competition payout instruction.", nameof(result));

        if (result.UniquePlayerCount == 1)
        {
            var refund = instructions.Length == 1 && instructions[0].Kind == ArcadeCompetitionPayoutKind.Refund
                ? instructions[0]
                : throw new ArgumentException("A sole-player competition must produce exactly one refund.", nameof(result));
            if (refund.AmountCents != result.TotalEntryFeesCents || result.HouseCutCents != 0)
                throw new ArgumentException("A sole-player competition must return every entry fee and retain no house cut.", nameof(result));
        }
        else if (result.UniquePlayerCount > 1)
        {
            if (instructions.Any(instruction => instruction.Kind != ArcadeCompetitionPayoutKind.Prize))
                throw new ArgumentException("A multiplayer competition can only produce prize instructions.", nameof(result));
            if (instructions.Sum(instruction => instruction.AmountCents) != result.VisibleJackpotCents)
                throw new ArgumentException("Multiplayer prize instructions must pay the entire visible jackpot.", nameof(result));
        }
        else if (!instructions.IsEmpty || result.TotalEntryFeesCents != 0)
        {
            throw new ArgumentException("An empty competition cannot produce payouts or entry fees.", nameof(result));
        }

        return new ArcadeCompetitionPayoutPlan(
            competition,
            instructions,
            result.VisibleJackpotCents,
            result.HouseCutCents);
    }

    private static ArcadeCompetitionCreditInstruction CreateInstruction(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionPayoutKind kind,
        string playerId,
        int? placement,
        long amountCents) => new(
            kind,
            playerId,
            placement,
            amountCents,
            CreateIdempotencyKey(competition, kind, playerId, placement));

    private static string CreateIdempotencyKey(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionPayoutKind kind,
        string playerId,
        int? placement)
    {
        var canonicalValue = string.Join('\n',
            "arcade-competition-credit-v1",
            competition.GameId,
            competition.WindowKind.ToString(),
            competition.StartsAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            competition.EndsAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            kind.ToString(),
            playerId,
            placement?.ToString(CultureInfo.InvariantCulture) ?? "none");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalValue));
        return $"arcade-competition-credit-v1-{Convert.ToHexStringLower(digest)}";
    }

    private static void ValidateCompetition(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionWindow window)
    {
        if (competition.WindowKind != window.Kind ||
            competition.StartsAtUtc != window.StartsAtUtc ||
            competition.EndsAtUtc != window.EndsAtUtc)
        {
            throw new ArgumentException("The payout result must belong to the supplied competition.", nameof(window));
        }
    }

    private static void ValidateInstructionSet(
        ImmutableArray<ArcadeCompetitionCreditInstruction> instructions,
        long visibleJackpotCents,
        long houseCutCents,
        string parameterName)
    {
        if (instructions.Select(instruction => instruction.PlayerId).Distinct(StringComparer.Ordinal).Count() != instructions.Length)
            throw new ArgumentException("A player cannot receive more than one competition payout instruction.", parameterName);
        if (instructions.Sum(instruction => instruction.AmountCents) != visibleJackpotCents)
            throw new ArgumentException("Stored payout instructions must equal the visible jackpot.", parameterName);

        var refundCount = instructions.Count(instruction => instruction.Kind == ArcadeCompetitionPayoutKind.Refund);
        if (refundCount > 0 && (refundCount != 1 || instructions.Length != 1 || houseCutCents != 0))
            throw new ArgumentException("A refund plan must contain one instruction and retain no house cut.", parameterName);
    }
}
