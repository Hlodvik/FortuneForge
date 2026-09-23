using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.SicBo;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5189");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5179")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

const decimal StartingBalance = 1_000m;
const decimal MinimumStake = 1m;
const decimal MaximumStakePerBet = 100m;
const decimal StakeIncrement = 1m;
const int MaximumBetsPerRound = 20;
var transactionGate = new object();
var playerBalance = StartingBalance;

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Sic Bo",
    api = "/api/games/sic-bo",
    mode = "local-free-play",
    audit = "roundId is the local round audit correlation seam",
}));

var api = app.MapGroup("/api/games/sic-bo");
api.MapGet("/status", () => Results.Ok(new SicBoStatusResponse(
    true, MinimumStake, MaximumStakePerBet, StakeIncrement, MaximumBetsPerRound, playerBalance, "local-free-play")));

api.MapPost("/rounds", async (HttpRequest request) =>
{
    var parsed = await ParseRoundRequest(request);
    if (!parsed.IsSuccess) return Error(parsed.Code!, parsed.Message!);

    var bets = parsed.Bets!;
    lock (transactionGate)
    {
        var totalStake = bets.Sum(bet => bet.Stake);
        if (playerBalance < totalStake) return InsufficientBalance();

        playerBalance -= totalStake;
        var roll = new SicBoRoll(
            RandomNumberGenerator.GetInt32(1, 7),
            RandomNumberGenerator.GetInt32(1, 7),
            RandomNumberGenerator.GetInt32(1, 7));
        var settlements = bets.Select((bet, index) => ToSettlement(index, bet, SicBoPaytable.Settle(ToDomainBet(bet), roll))).ToArray();
        var totalReturn = settlements.Sum(settlement => settlement.TotalReturn);
        playerBalance += totalReturn;

        var roundId = Guid.NewGuid().ToString("N");
        return Results.Created($"/api/games/sic-bo/rounds/{roundId}", new SicBoRoundResponse(
            roundId,
            playerBalance,
            "settled",
            [.. roll.Values],
            roll.Total,
            roll.IsTriple,
            totalStake,
            totalReturn,
            totalReturn - totalStake,
            settlements));
    }
});

app.Run();

static async Task<ParsedRoundRequest> ParseRoundRequest(HttpRequest request)
{
    JsonDocument document;
    try
    {
        document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
    }
    catch (JsonException)
    {
        return ParsedRoundRequest.Failure("sic-bo-malformed-input", "The Sic Bo request body must be valid JSON.");
    }

    using (document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !HasExactlyProperties(root, "bets") || !root.TryGetProperty("bets", out var betsElement) || betsElement.ValueKind != JsonValueKind.Array)
            return ParsedRoundRequest.Failure("sic-bo-malformed-input", "The Sic Bo request must contain only a bets array.");
        if (betsElement.GetArrayLength() == 0 || betsElement.GetArrayLength() > MaximumBetsPerRound)
            return ParsedRoundRequest.Failure("sic-bo-invalid-slip", "Submit from one through 20 Sic Bo bets per round.");

        var bets = new List<ParsedBet>(betsElement.GetArrayLength());
        foreach (var element in betsElement.EnumerateArray())
        {
            var parsedBet = ParseBet(element);
            if (!parsedBet.IsSuccess) return ParsedRoundRequest.Failure(parsedBet.Code!, parsedBet.Message!);
            bets.Add(parsedBet.Bet!);
        }

        return ParsedRoundRequest.Success(bets);
    }
}

static ParsedBetResult ParseBet(JsonElement element)
{
    if (element.ValueKind != JsonValueKind.Object || !HasExactlyProperties(element, "kind", "stake", "face", "total", "firstFace", "secondFace"))
        return ParsedBetResult.Failure("sic-bo-invalid-bet", "Each Sic Bo bet must use the complete client contract shape.");

    if (!element.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String || !TryParseKind(kindElement.GetString(), out var kind))
        return ParsedBetResult.Failure("sic-bo-invalid-bet", "The Sic Bo bet kind is not supported.");
    if (!TryGetStake(element, out var stake) || stake is < MinimumStake or > MaximumStakePerBet || stake % StakeIncrement != 0m)
        return ParsedBetResult.Failure("sic-bo-invalid-stake", "Use whole-credit Sic Bo stakes from 1 through 100.");
    if (!TryGetNullableInt(element, "face", out var face) || !TryGetNullableInt(element, "total", out var total) ||
        !TryGetNullableInt(element, "firstFace", out var firstFace) || !TryGetNullableInt(element, "secondFace", out var secondFace))
        return ParsedBetResult.Failure("sic-bo-invalid-bet", "Sic Bo selections must be integers or null.");

    var bet = new ParsedBet(kind, stake, face, total, firstFace, secondFace);
    if (!HasValidSelection(bet))
        return ParsedBetResult.Failure("sic-bo-invalid-bet", "The Sic Bo selection does not match its bet kind.");

    return ParsedBetResult.Success(bet.Kind == SicBoBetKind.TwoNumberCombination
        ? bet with { FirstFace = Math.Min(bet.FirstFace!.Value, bet.SecondFace!.Value), SecondFace = Math.Max(bet.FirstFace!.Value, bet.SecondFace!.Value) }
        : bet);
}

static bool HasValidSelection(ParsedBet bet) => bet.Kind switch
{
    SicBoBetKind.Small or SicBoBetKind.Big or SicBoBetKind.Odd or SicBoBetKind.Even or SicBoBetKind.AnyTriple =>
        bet.Face is null && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null,
    SicBoBetKind.SingleNumber or SicBoBetKind.SpecificDouble or SicBoBetKind.SpecificTriple =>
        IsFace(bet.Face) && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null,
    SicBoBetKind.Total => bet.Face is null && bet.Total is >= 4 and <= 17 && bet.FirstFace is null && bet.SecondFace is null,
    SicBoBetKind.TwoNumberCombination => bet.Face is null && bet.Total is null && IsFace(bet.FirstFace) && IsFace(bet.SecondFace) && bet.FirstFace != bet.SecondFace,
    _ => false,
};

static SicBoBet ToDomainBet(ParsedBet bet) => bet.Kind switch
{
    SicBoBetKind.Small => SicBoBet.Small(bet.Stake),
    SicBoBetKind.Big => SicBoBet.Big(bet.Stake),
    SicBoBetKind.Odd => SicBoBet.Odd(bet.Stake),
    SicBoBetKind.Even => SicBoBet.Even(bet.Stake),
    SicBoBetKind.SingleNumber => SicBoBet.SingleNumber(bet.Stake, bet.Face!.Value),
    SicBoBetKind.Total => SicBoBet.ForTotal(bet.Stake, bet.Total!.Value),
    SicBoBetKind.TwoNumberCombination => SicBoBet.TwoNumberCombination(bet.Stake, bet.FirstFace!.Value, bet.SecondFace!.Value),
    SicBoBetKind.SpecificDouble => SicBoBet.SpecificDouble(bet.Stake, bet.Face!.Value),
    SicBoBetKind.AnyTriple => SicBoBet.AnyTriple(bet.Stake),
    SicBoBetKind.SpecificTriple => SicBoBet.SpecificTriple(bet.Stake, bet.Face!.Value),
    _ => throw new ArgumentOutOfRangeException(nameof(bet)),
};

static SicBoSettlementResponse ToSettlement(int betIndex, ParsedBet bet, SicBoBetSettlement settlement) => new(
    betIndex, KindName(bet.Kind), bet.Stake, bet.Face, bet.Total, bet.FirstFace, bet.SecondFace,
    settlement.Won, settlement.ProfitOdds, settlement.Profit, settlement.TotalReturn);

static IResult Error(string code, string message) => Results.BadRequest(new SicBoErrorResponse(code, message));
static IResult InsufficientBalance() => Error("sic-bo-insufficient-balance", "There are not enough local credits for this Sic Bo bet slip.");
static bool IsFace(int? value) => value is >= 1 and <= 6;
static bool HasExactlyProperties(JsonElement element, params string[] names)
{
    var properties = element.EnumerateObject().ToArray();
    return properties.Length == names.Length && properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() == names.Length &&
        names.All(name => properties.Any(property => property.NameEquals(name)));
}
static bool TryGetStake(JsonElement element, out decimal stake)
{
    stake = default;
    return element.TryGetProperty("stake", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out stake);
}
static bool TryGetNullableInt(JsonElement element, string name, out int? value)
{
    value = null;
    if (!element.TryGetProperty(name, out var property)) return false;
    if (property.ValueKind == JsonValueKind.Null) return true;
    if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var integer)) return false;
    value = integer;
    return true;
}
static bool TryParseKind(string? value, out SicBoBetKind kind)
{
    switch (value)
    {
        case "small": kind = SicBoBetKind.Small; return true;
        case "big": kind = SicBoBetKind.Big; return true;
        case "odd": kind = SicBoBetKind.Odd; return true;
        case "even": kind = SicBoBetKind.Even; return true;
        case "single-number": kind = SicBoBetKind.SingleNumber; return true;
        case "total": kind = SicBoBetKind.Total; return true;
        case "two-number-combination": kind = SicBoBetKind.TwoNumberCombination; return true;
        case "specific-double": kind = SicBoBetKind.SpecificDouble; return true;
        case "any-triple": kind = SicBoBetKind.AnyTriple; return true;
        case "specific-triple": kind = SicBoBetKind.SpecificTriple; return true;
        default: kind = default; return false;
    }
}
static string KindName(SicBoBetKind kind) => kind switch
{
    SicBoBetKind.Small => "small", SicBoBetKind.Big => "big", SicBoBetKind.Odd => "odd", SicBoBetKind.Even => "even",
    SicBoBetKind.SingleNumber => "single-number", SicBoBetKind.Total => "total", SicBoBetKind.TwoNumberCombination => "two-number-combination",
    SicBoBetKind.SpecificDouble => "specific-double", SicBoBetKind.AnyTriple => "any-triple", SicBoBetKind.SpecificTriple => "specific-triple",
    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
};

public sealed record ParsedBet(SicBoBetKind Kind, decimal Stake, int? Face, int? Total, int? FirstFace, int? SecondFace);
public sealed record ParsedRoundRequest(bool IsSuccess, IReadOnlyList<ParsedBet>? Bets, string? Code, string? Message)
{
    public static ParsedRoundRequest Success(IReadOnlyList<ParsedBet> bets) => new(true, bets, null, null);
    public static ParsedRoundRequest Failure(string code, string message) => new(false, null, code, message);
}
public sealed record ParsedBetResult(bool IsSuccess, ParsedBet? Bet, string? Code, string? Message)
{
    public static ParsedBetResult Success(ParsedBet bet) => new(true, bet, null, null);
    public static ParsedBetResult Failure(string code, string message) => new(false, null, code, message);
}
public sealed record SicBoStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStakePerBet, decimal StakeIncrement, int MaximumBetsPerRound, decimal Balance, string Mode);
public sealed record SicBoSettlementResponse(int BetIndex, string Kind, decimal Stake, int? Face, int? Total, int? FirstFace, int? SecondFace, bool Won, decimal ProfitOdds, decimal Profit, decimal TotalReturn);
public sealed record SicBoRoundResponse(string RoundId, decimal Balance, string Phase, int[] Dice, int Total, bool IsTriple, decimal TotalStaked, decimal TotalReturn, decimal Profit, IReadOnlyList<SicBoSettlementResponse> Settlements);
public sealed record SicBoErrorResponse(string Code, string Message);
