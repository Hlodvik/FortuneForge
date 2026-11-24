using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.FrontierFortune.Server;

public sealed record FrontierCommand(string OperationId, string Kind, long WagerPoints = 0,
    string? BonusId = null, int? TargetIndex = null, long? ExpectedRevision = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MathVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RoundId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool[]? Held { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int?[]? SelectedRows { get; init; }
    public void Validate()
    {
        if (OperationId is not { Length: >= 16 and <= 128 } ||
            OperationId.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_')))
            throw new ArgumentException("Include an operation ID with 16 to 128 letters, digits, hyphens or underscores.");
        if (MathVersion is not null) { OutlawRoundPolicy.ValidateCommand(this); return; }
        if (RoundId is not null || Held is not null || SelectedRows is not null) throw new ArgumentException("Foreign command fields.");
        if (Kind == "spin")
        {
            FrontierRules.ValidateWager(WagerPoints);
            if (BonusId is not null || TargetIndex is not null || ExpectedRevision is not null)
                throw new ArgumentException("A spin cannot include bounty action fields.");
        }
        else if (Kind is "shoot" or "bank")
        {
            if (WagerPoints != 0 || !Guid.TryParseExact(BonusId, "N", out var id) || id == Guid.Empty ||
                ExpectedRevision is null or < 0 || Kind == "shoot" && TargetIndex is not (>= 0 and < 15) ||
                Kind == "bank" && TargetIndex is not null)
                throw new ArgumentException("Include the active bounty ID, current revision and a valid action.");
        }
        else throw new ArgumentException("Unknown Frontier Fortune command.");
    }
}

public sealed record FrontierAccountState(long Revision, string[][] Reels, FrontierLineWin[] Wins,
    long LastWinPoints, FrontierBonus? Bonus)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OutlawAccountState? Outlaw { get; init; }
    public static FrontierAccountState Empty() => new(0,
        [["J", "Q", "K"], ["A", "SPUR", "HAT"], ["POUCH", "REVOLVER", "BAR"],
         ["BADGE", "WILD", "A"], ["K", "Q", "J"]], [], 0, null);

    public void Validate()
    {
        FrontierFortuneEngine.ValidateReels(Reels);
        if (Revision < 0 || LastWinPoints < 0 || Wins is null) throw new InvalidOperationException("The saved Frontier state is invalid.");
        if (Bonus is not null) FrontierBounties.Validate(Bonus);
        if (Outlaw is not null) { if (Bonus is not null) throw new InvalidOperationException("Mixed feature versions."); Outlaw.Validate(); }
    }
}

public sealed record FrontierWagering(int PointValueInCents, long MinWagerPoints, long MaxWagerPoints, long StepPoints);
public sealed record FrontierReply(string GameId, string EngineVersion, long BalanceCents, long Revision,
    string[][] Reels, FrontierLineWin[] Wins, long LastWinPoints, FrontierBonus? Bonus,
    FrontierCommand? PendingOperation, FrontierWagering Wagering, bool Accepted = true,
    string? Code = null, string? Error = null, FrontierShotPreview? LastAction = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OutlawAnteView? Ante { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OutlawHandRound? HandRound { get; init; }
    public static FrontierReply Create(FrontierAccountState state, long balance, FrontierCommand? pending = null,
        bool accepted = true, string? code = null, string? error = null, FrontierShotPreview? action = null)
    {
        if (state.Outlaw is not null || pending?.MathVersion == OutlawRules.EngineVersion)
            return CreateOutlaw(state, balance, pending, accepted, code, error);
        var rules = FrontierRules.Definition;
        return new(FrontierFortuneEngine.GameId, FrontierFortuneEngine.EngineVersion, balance, state.Revision,
            state.Reels, state.Wins, state.LastWinPoints, state.Bonus is null ? null : FrontierBounties.WithPreviews(state.Bonus),
            pending, new(rules.PointValueInCents, rules.MinWagerPoints, rules.MaxWagerPoints, rules.StepPoints),
            accepted, code, error, action);
    }

    public static FrontierReply CreateOutlaw(FrontierAccountState state, long balance, FrontierCommand? pending = null,
        bool accepted = true, string? code = null, string? error = null)
    {
        var native = OutlawAccountState.From(state);
        var rules = FrontierRules.Definition;
        return new(FrontierFortuneEngine.GameId, OutlawRules.EngineVersion, balance, state.Revision, state.Reels, state.Wins,
            state.LastWinPoints, null, pending, new(rules.PointValueInCents, rules.MinWagerPoints, rules.MaxWagerPoints, rules.StepPoints),
            accepted, code, error) { Ante = native.View(), HandRound = native.HandRound };
    }
}

public sealed record FrontierPreparedRound(string AdmissionId, string AccountId, FrontierCommand Command,
    FrontierAccountState Before, FrontierAccountState After, long WagerCents, long PayoutCents,
    DateTime StartedAtUtc, FrontierSpin? Spin, FrontierShotPreview? Shot, string OutcomeSha256)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OutlawPreparedPayload? Outlaw { get; init; }
}
public sealed record FrontierStoredRound(FrontierPreparedRound Prepared, FrontierReply? Final = null);
public sealed record FrontierReadState(FrontierAccountState State, FrontierCommand? PendingOperation);

public sealed class FrontierConflictException(string message, string code = "frontier-operation-conflict") : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public static class FrontierRoundIdentity
{
    public static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, FrontierRules.Json)));
    public static string Key(string account, string operation) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{FrontierFortuneEngine.GameId}\n{account}\n{operation}")));
    public static GameWalletSettlementRequest WalletRequest(FrontierPreparedRound round) => new(round.AdmissionId,
        round.OutcomeSha256, round.WagerCents, round.PayoutCents, ExpectedLegacyFreeGamesRemaining: 0);
}
