using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.PharaohsAscent;

public sealed record SealedTombAction(string Action, long ExpectedRevision, string? ChoiceId = null);
public sealed record SealedTombTicket(string Id, long FundedWagerPoints);
public sealed record SealedTombChoice(string Id, string Kind, string? Relic);
public sealed record SealedTombRoom(int Step, string Kind, string? Relic, long AwardPoints);
public sealed record SealedTombRelics(int Sun, int Moon, int Serpent);
public sealed record SealedTombFunding(int Sigils, int RequiredSigils, long AverageWagerPoints,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int MinimumScarabs = 0);

public sealed record SealedTombRun(string Id, long FundedWagerPoints,
    IReadOnlyList<SealedTombChoice> Choices, IReadOnlyList<SealedTombRoom> Path)
{
    public int TorchesRemaining => SealedTombRules.TorchCount - Path.Count;
    public int Keys => Path.Count(room => room.Kind == "key");
    public SealedTombRelics Relics => new(Path.Count(room => room.Relic == "sun"),
        Path.Count(room => room.Relic == "moon"), Path.Count(room => room.Relic == "serpent"));
    public int RelicMultiplier => 1 + Math.Min(Relics.Sun, Math.Min(Relics.Moon, Relics.Serpent));
    public long BasePoints => checked(FundedWagerPoints * SealedTombRules.BaseMultiplier);
    public long RoomPoints => Path.Sum(room => room.AwardPoints);
    public long BankedPoints => checked(BasePoints + RoomPoints * RelicMultiplier);
    public string Phase => Keys >= SealedTombRules.KeyRequirement ? "finale" : "explore";
}

public sealed record SealedTombResult(string Id, long FundedWagerPoints, int RoomsOpened, int Keys,
    SealedTombRelics Relics, int RelicMultiplier, long BasePoints, long RoomPoints, long FinalePoints,
    long PayoutPoints, string Reason);

public sealed record SealedTombView(int ContractVersion, long Revision, SealedTombFunding Funding,
    IReadOnlyList<SealedTombTicket> Tickets, SealedTombRun? ActiveRun, SealedTombResult? LastRun,
    SealedTombAction? PendingAction);

/// <summary>Exact funded totals remain private; the public view derives their average.</summary>
public sealed record SealedTombState(long Revision, int Sigils, long SigilWagerPointTotal,
    IReadOnlyList<SealedTombTicket> Tickets, SealedTombRun? ActiveRun, SealedTombResult? LastRun)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static SealedTombState Empty { get; } = new(0, 0, 0, [], null, null);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MinimumScarabs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PharaohsAscentSunfireFunding? Sunfire { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<long, int>? PityByWager { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PharaohsAscentFreeSpinBatch>? FreeSpinBatches { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PharaohsAscentBoostFunding? BoostFunding { get; init; }

    internal int FundingMinimumScarabs => MinimumScarabs != 0 ? MinimumScarabs :
        Sigils > 0 ? 2 : PharaohsAscentMathV3.Current.TombMinimumScarabs;

    public SealedTombView View(SealedTombAction? pendingAction = null, bool includeFundingRequirement = true) => new(SealedTombRules.ContractVersion,
        Revision, new(Sigils, SealedTombRules.SigilTarget, Sigils == 0 ? 0 :
            MinimumScarabs >= 3 ? SigilWagerPointTotal / Sigils : Average(SigilWagerPointTotal, Sigils),
            includeFundingRequirement ? FundingMinimumScarabs : 0),
        Tickets, ActiveRun, LastRun, pendingAction);

    public static SealedTombState Parse(string? json)
    {
        if (json is null) return Empty;
        var state = JsonSerializer.Deserialize<SealedTombState>(json, Json) ?? throw Invalid();
        Validate(state);
        return state;
    }

    public static string Serialize(SealedTombState state)
    {
        Validate(state);
        return JsonSerializer.Serialize(state, Json);
    }

    internal static long Average(long total, int count) =>
        checked((long)Math.Round(total / (decimal)count, MidpointRounding.AwayFromZero));

    public static void Validate(SealedTombState state)
    {
        PharaohsAscentSunfire.Validate(state.Sunfire);
        PharaohsAscentPity.Validate(state.PityByWager);
        PharaohsAscentFreeSpins.Validate(state.FreeSpinBatches);
        PharaohsAscentBoosts.Validate(state.BoostFunding);
        if (state.MinimumScarabs is not (0 or 2 or 3)) throw Invalid();
        if (state.Revision < 0 || state.Revision > 9_007_199_254_740_000L || state.Sigils is < 0 or >= SealedTombRules.SigilTarget ||
            state.SigilWagerPointTotal < 2L * state.Sigils || state.SigilWagerPointTotal > 2_000L * state.Sigils ||
            state.Tickets is null || state.Tickets.Any(ticket => ticket is null) ||
            state.Tickets.Select(ticket => ticket.Id).Distinct(StringComparer.Ordinal).Count() != state.Tickets.Count ||
            state.ActiveRun is not null && state.LastRun is not null)
            throw Invalid();
        foreach (var ticket in state.Tickets)
            if (!IsId(ticket.Id) || !IsStake(ticket.FundedWagerPoints)) throw Invalid();
        if (state.ActiveRun is { } run)
        {
            if (!IsId(run.Id) || !IsStake(run.FundedWagerPoints) || run.Path is null || run.Choices is null ||
                run.Path.Count > SealedTombRules.TorchCount || run.Keys > SealedTombRules.KeyRequirement ||
                run.Path.Take(Math.Max(0, run.Path.Count - 1)).Count(room => room.Kind == "key") >= SealedTombRules.KeyRequirement ||
                run.Keys < SealedTombRules.KeyRequirement && run.Path.Count == SealedTombRules.TorchCount ||
                state.Tickets.Any(ticket => ticket.Id == run.Id)) throw Invalid();
            for (var index = 0; index < run.Path.Count; index++)
            {
                var room = run.Path[index];
                if (room.Step != index + 1 || !ValidKind(room.Kind) ||
                    (room.Kind == "relic" ? !ValidRelic(room.Relic) : room.Relic is not null) ||
                    room.AwardPoints != checked(run.FundedWagerPoints * (room.Kind == "coin" ? room.AwardPoints / run.FundedWagerPoints : 1)) ||
                    (room.Kind == "coin" ? room.AwardPoints / run.FundedWagerPoints is < 2 or > 4 : room.AwardPoints != run.FundedWagerPoints))
                    throw Invalid();
            }
            if (run.Phase == "finale" ? run.Choices.Count != 0 : run.Choices.Count != 3 ||
                run.Choices.Select(choice => choice.Kind).Distinct(StringComparer.Ordinal).Count() != 3) throw Invalid();
            foreach (var choice in run.Choices)
                if (!ValidKind(choice.Kind) || choice.Id != SealedTombRules.ChoiceId(run.Id, run.Path.Count, choice.Kind) ||
                    (choice.Kind == "relic" ? !ValidRelic(choice.Relic) : choice.Relic is not null)) throw Invalid();
        }
        if (state.LastRun is { } result)
        {
            if (!IsId(result.Id) || !IsStake(result.FundedWagerPoints) || result.RoomsOpened is < 0 or > SealedTombRules.TorchCount ||
                result.Keys is < 0 or > SealedTombRules.KeyRequirement || result.RelicMultiplier is < 1 or > 3 ||
                result.Relics is null || result.Relics.Sun < 0 || result.Relics.Moon < 0 || result.Relics.Serpent < 0 ||
                result.RelicMultiplier != 1 + Math.Min(result.Relics.Sun, Math.Min(result.Relics.Moon, result.Relics.Serpent)) ||
                result.BasePoints != checked(result.FundedWagerPoints * SealedTombRules.BaseMultiplier) || result.RoomPoints < 0 ||
                result.FinalePoints < 0 || result.PayoutPoints != checked(result.BasePoints + result.RoomPoints * result.RelicMultiplier + result.FinalePoints) ||
                result.Relics.Sun + result.Relics.Moon + result.Relics.Serpent + result.Keys > result.RoomsOpened ||
                result.RoomPoints < result.RoomsOpened * result.FundedWagerPoints || result.RoomPoints > result.RoomsOpened * result.FundedWagerPoints * 4 ||
                result.Reason is not ("torches" or "sarcophagus" or "collect") ||
                result.Reason == "torches" && (result.RoomsOpened != SealedTombRules.TorchCount || result.Keys >= SealedTombRules.KeyRequirement) ||
                result.Reason == "collect" && result.Keys >= SealedTombRules.KeyRequirement ||
                result.Reason != "sarcophagus" && result.FinalePoints != 0 ||
                result.Reason == "sarcophagus" && (result.Keys != SealedTombRules.KeyRequirement ||
                    result.FinalePoints != result.FundedWagerPoints * 8 && result.FinalePoints != result.FundedWagerPoints * 12 &&
                    result.FinalePoints != result.FundedWagerPoints * 20)) throw Invalid();
        }
    }

    internal static bool ValidRelic(string? relic) => relic is "sun" or "moon" or "serpent";
    private static bool ValidKind(string kind) => kind is "coin" or "relic" or "key";
    private static bool IsId(string? id) => id is { Length: >= 16 and <= 100 } &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static bool IsStake(long points) => points is >= 2 and <= 2_000;
    private static InvalidOperationException Invalid() => new("The Sealed Tomb state is invalid.");
}
