// Edited: 2026-11-18T18:00:51-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:45:56-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:29:41-06:00 | Computer: Vinnabok
using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.ArcaneArchives;

public record ArcaneTomeState(
    string SelectedPage,
    string? ActivePage,
    int BoostedSpinsRemaining,
    bool GrimoireReady,
    IReadOnlyDictionary<string, long> Ingredients)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RecipeVersion { get; init; }
}

public sealed record ArcaneTomePage(string Id, string Label, IReadOnlyList<string> Ingredients);

public sealed record ArcaneTomeView(
    string SelectedPage,
    string? ActivePage,
    int BoostedSpinsRemaining,
    bool GrimoireReady,
    IReadOnlyDictionary<string, long> Ingredients,
    IReadOnlyList<ArcaneTomePage> Pages)
    : ArcaneTomeState(SelectedPage, ActivePage, BoostedSpinsRemaining, GrimoireReady, Ingredients);

public sealed record ArcaneTomeProgress(
    ArcaneTomeState State,
    IReadOnlyDictionary<string, long> IngredientsAwarded);

public sealed class ArcaneTomeConflictException(string message) : Exception(message);
public sealed class ArcaneTomeActivationRequiredException()
    : Exception("Open the Tome and activate a page before spinning again.");

public static class ArcaneTome
{
    public const int BoostedSpinCount = 5;
    public const string GrimoireSymbolId = "ACE";
    public const string MandrakeRoot = "MANDRAKE_ROOT";
    internal const string NightshadeBerries = "NIGHTSHADE_BERRIES";
    internal const string MooncapMushroom = "MOONCAP_MUSHROOM";
    internal const string PhoenixFeather = "PHOENIX_FEATHER";
    internal const string WolfsbaneWine = "WOLFSBANE_WINE";
    internal const string Quicksilver = "QUICKSILVER";
    private const string Herbalism = "herbalism";
    private const string MoonAndFlame = "moon-and-flame";
    private const string Distillation = "distillation";
    private const int CurrentRecipeVersion = 1;

    private static readonly IReadOnlyList<ArcaneTomePage> LegacyPages = Array.AsReadOnly<ArcaneTomePage>([
        new(Herbalism, "Herbalism", Array.AsReadOnly<string>([MandrakeRoot, NightshadeBerries])),
        new(MoonAndFlame, "Moon & Flame", Array.AsReadOnly<string>([MooncapMushroom, PhoenixFeather])),
        new(Distillation, "Distillation", Array.AsReadOnly<string>([WolfsbaneWine, Quicksilver])),
    ]);

    private static readonly IReadOnlyList<ArcaneTomePage> Pages = Array.AsReadOnly<ArcaneTomePage>([
        LegacyPages[0],
        new(MoonAndFlame, "Transmutation", Array.AsReadOnly<string>([Quicksilver, PhoenixFeather])),
        new(Distillation, "Tinctures", Array.AsReadOnly<string>([MooncapMushroom, WolfsbaneWine])),
    ]);

    // Keep ingredient strip positions stable when Tome recipes change.
    public static IReadOnlyList<string> IngredientIds { get; } =
        Array.AsReadOnly(LegacyPages.SelectMany(page => page.Ingredients).ToArray());

    private static readonly FrozenSet<string> Ingredients = IngredientIds.ToFrozenSet(StringComparer.Ordinal);

    public static ArcaneTomeState Empty { get; } = new(Herbalism, null, 0, false,
        IngredientIds.ToFrozenDictionary(id => id, _ => 0L, StringComparer.Ordinal));

    public static ArcaneTomeView? ToView(ArcaneTomeState? state) => state is null ? null :
        new(state.SelectedPage, state.ActivePage, state.BoostedSpinsRemaining, state.GrimoireReady,
            state.Ingredients, PagesFor(state)) { RecipeVersion = state.RecipeVersion };

    public static void EnsureSpinAllowed(ArcaneTomeState state)
    {
        if (state.GrimoireReady)
            throw new ArcaneTomeActivationRequiredException();
    }

    public static ArcaneTomeState SelectPage(ArcaneTomeState before, string page, bool activate)
    {
        ValidateState(before);
        if (!IsPage(page))
            throw new ArgumentException("Choose one of the three Arcane Tome pages.", nameof(page));
        if (before.ActivePage is { } active)
        {
            if (page != active)
                throw new ArcaneTomeConflictException("The active Tome page stays locked until the boost ends.");
            return before;
        }
        if (!activate)
            return before with { SelectedPage = page };
        if (!before.GrimoireReady)
            throw new ArcaneTomeConflictException("Land a Grand Grimoire to awaken a Tome page.");
        return before with
        {
            SelectedPage = page,
            ActivePage = page,
            BoostedSpinsRemaining = BoostedSpinCount,
            GrimoireReady = false,
            RecipeVersion = CurrentRecipeVersion
        };
    }

    public static ArcaneTomeProgress CompleteSpin(
        ArcaneTomeState before,
        IReadOnlyList<IReadOnlyList<string>> reels,
        bool isFreeSpin,
        bool requireIngredient = false)
    {
        ValidateState(before);
        var active = before.ActivePage;
        var boosted = active is not null && !isFreeSpin;
        var allSymbols = reels.SelectMany(reel => reel).ToArray();
        var grimoireLanded = allSymbols.Contains(GrimoireSymbolId, StringComparer.Ordinal);
        if (active is not null && grimoireLanded)
            throw new InvalidOperationException("A boosted Tome reel contains a Grand Grimoire.");

        var awards = IngredientUnits(before, allSymbols, isFreeSpin, requireIngredient)
            .GroupBy(symbol => symbol, StringComparer.Ordinal).ToFrozenDictionary(
                group => group.Key, group => group.LongCount(), StringComparer.Ordinal);
        var ingredients = IngredientIds.ToFrozenDictionary(symbol => symbol,
            symbol => checked(before.Ingredients[symbol] + awards.GetValueOrDefault(symbol)),
            StringComparer.Ordinal);
        var remaining = before.BoostedSpinsRemaining - (boosted ? 1 : 0);
        return new(before with
        {
            ActivePage = remaining > 0 ? active : null,
            BoostedSpinsRemaining = remaining,
            GrimoireReady = before.GrimoireReady || grimoireLanded,
            RecipeVersion = remaining > 0 ? before.RecipeVersion : 0,
            Ingredients = ingredients
        }, awards);
    }

    // Consecutive copies from one boosted landing stay adjacent in the pot.
    public static IReadOnlyList<string> OrderedIngredientUnits(
        ArcaneTomeState before, IReadOnlyList<IReadOnlyList<string>> reels, bool isFreeSpin)
    {
        ValidateState(before);
        return IngredientUnits(before, reels.SelectMany(reel => reel), isFreeSpin, false);
    }

    private static string[] IngredientUnits(
        ArcaneTomeState before, IEnumerable<string> symbols, bool isFreeSpin, bool requireIngredient)
    {
        var landings = symbols.Where(Ingredients.Contains).ToArray();
        if (landings.Length > 3 || landings.Length == 0 && requireIngredient)
            throw new InvalidOperationException("The Arcane spin violates its ingredient landing rules.");
        var boosted = before.ActivePage is { } page && !isFreeSpin
            ? PagesFor(before).Single(candidate => candidate.Id == page).Ingredients : null;
        return landings.SelectMany(id =>
            Enumerable.Repeat(id, boosted?.Contains(id, StringComparer.Ordinal) == true ? 2 : 1)).ToArray();
    }

    public static void ValidateState(ArcaneTomeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsPage(state.SelectedPage) || state.BoostedSpinsRemaining is < 0 or > BoostedSpinCount ||
            state.RecipeVersion is < 0 or > CurrentRecipeVersion ||
            (state.BoostedSpinsRemaining == 0
                ? state.ActivePage is not null || state.RecipeVersion != 0
                : state.ActivePage != state.SelectedPage || state.GrimoireReady) ||
            state.Ingredients is null || state.Ingredients.Count != IngredientIds.Count ||
            IngredientIds.Any(id => !state.Ingredients.TryGetValue(id, out var count) || count < 0))
            throw new InvalidOperationException("The saved Arcane Tome state is invalid.");
    }

    public static bool SameState(ArcaneTomeState first, ArcaneTomeState second) =>
        first.SelectedPage == second.SelectedPage && first.ActivePage == second.ActivePage &&
        first.BoostedSpinsRemaining == second.BoostedSpinsRemaining && first.GrimoireReady == second.GrimoireReady &&
        first.RecipeVersion == second.RecipeVersion &&
        IngredientIds.All(id => first.Ingredients[id] == second.Ingredients[id]);

    private static IReadOnlyList<ArcaneTomePage> PagesFor(ArcaneTomeState state) =>
        state.ActivePage is not null && state.RecipeVersion == 0 ? LegacyPages : Pages;

    private static bool IsPage(string? page) => page is Herbalism or MoonAndFlame or Distillation;
}
