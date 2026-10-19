using System.Globalization;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Server.Bots.Solitaire;

/// <summary>
/// Advances one managed player's real Klondike game on that player's own clock.
/// No result or score is sampled: every point comes from an engine-legal action.
/// </summary>
internal static class SolitaireManagedPlayerProgression
{
    private const string NextActionTicksKey = "solitaire-bot-next-action-ticks";
    private const string MoveLimitKey = "solitaire-bot-move-limit";
    private const string ProgressEventsKey = "solitaire-bot-progress-events";
    private const string ProgressAtRecycleKey = "solitaire-bot-progress-at-recycle";
    private const string RecentVisibleStatesKey = "solitaire-bot-recent-visible-states";
    private const int RecentVisibleStateLimit = 24;
    private const int NormalCatchUpLimit = 8;

    internal static SolitairePlayerState Initialize(SolitaireMatch match, SolitairePlayerState player)
    {
        RequireManaged(player);
        if (player.HostMetadata.ContainsKey(NextActionTicksKey) &&
            player.HostMetadata.ContainsKey(MoveLimitKey)) return player;

        var metadata = new Dictionary<string, string>(player.HostMetadata, StringComparer.Ordinal);
        metadata[NextActionTicksKey] = StartedAt(match, player)
            .AddMilliseconds(ActionDelayMilliseconds(match, player, 0))
            .Ticks.ToString(CultureInfo.InvariantCulture);
        metadata[MoveLimitKey] = MoveLimit(match, player).ToString(CultureInfo.InvariantCulture);
        metadata[ProgressEventsKey] = "0";
        metadata[ProgressAtRecycleKey] = "0";
        metadata[RecentVisibleStatesKey] = SolitaireManagedPlayerPolicy.VisibleStateFingerprint(player.Game);
        return player with { HostMetadata = metadata };
    }

    /// <summary>
    /// Applies at most eight due actions in a normal poll. At the deadline the
    /// remaining scheduled actions are caught up so a quiet match can settle.
    /// </summary>
    internal static SolitairePlayerState Advance(
        SolitaireMatch match,
        SolitairePlayerState player,
        DateTime nowUtc)
    {
        RequireManaged(player);
        if (player.Status != SolitairePlayerStatuses.Playing) return player;

        var current = Initialize(match, player);
        var startedAt = StartedAt(match, current);
        var deadlineAt = DeadlineAt(match, current);
        var deadlineReached = nowUtc >= deadlineAt;
        var throughUtc = deadlineReached ? deadlineAt : nowUtc;
        if (SolitaireEngine.IsWon(current.Game))
            return Finish(match, current, throughUtc < startedAt ? startedAt : throughUtc);
        var actionLimit = deadlineReached ? int.MaxValue : NormalCatchUpLimit;
        var movesRemaining = ReadMoveLimit(current) - current.Game.Moves;
        var actionCount = 0;

        while (movesRemaining > 0 && actionCount < actionLimit &&
               NextActionAt(current) <= throughUtc &&
               !SolitaireEngine.IsWon(current.Game))
        {
            var dueAt = NextActionAt(current);
            SolitaireCommandRequest command;
            try
            {
                command = SolitaireManagedPlayerPolicy.Choose(
                    current.Game, current.Version, DecisionSkill(match, current),
                    DecisionSeed(match, current), ReadRecentVisibleStates(current));
            }
            catch (InvalidOperationException exception)
                when (exception.Message == "No legal Solitaire command is available.")
            {
                return Finish(match, current, dueAt);
            }

            var isRecycle = command.Type == SolitaireCommandTypes.Draw && current.Game.Stock.Count == 0;
            var progressEvents = ReadCounter(current, ProgressEventsKey);
            if (isRecycle && (progressEvents <= ReadCounter(current, ProgressAtRecycleKey) ||
                              current.Game.Score <= 100))
                return Finish(match, current, dueAt);

            var nextGame = SolitaireEngine.Apply(current.Game, command);
            var metadata = new Dictionary<string, string>(current.HostMetadata, StringComparer.Ordinal);
            if (MadeProgress(current.Game, nextGame, command))
                metadata[ProgressEventsKey] = checked(progressEvents + 1).ToString(CultureInfo.InvariantCulture);
            if (isRecycle)
                metadata[ProgressAtRecycleKey] = progressEvents.ToString(CultureInfo.InvariantCulture);
            metadata[RecentVisibleStatesKey] = AppendRecentVisibleState(
                current, SolitaireManagedPlayerPolicy.VisibleStateFingerprint(nextGame));
            metadata[NextActionTicksKey] = dueAt
                .AddMilliseconds(ActionDelayMilliseconds(match, current, nextGame.Moves))
                .Ticks.ToString(CultureInfo.InvariantCulture);
            current = current with
            {
                Game = nextGame,
                Version = checked(current.Version + 1),
                HostMetadata = metadata
            };
            movesRemaining--;
            actionCount++;

            if (SolitaireEngine.IsWon(nextGame))
                return Finish(match, current, dueAt);
        }

        if (movesRemaining == 0)
            return Finish(match, current, LastActionAt(match, current));
        if (deadlineReached)
            return Finish(match, current, deadlineAt);
        return current;
    }

    internal static DateTime NextActionAt(SolitairePlayerState player)
    {
        if (!player.HostMetadata.TryGetValue(NextActionTicksKey, out var value) ||
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            throw new InvalidOperationException("The managed Solitaire action clock has not been initialized.");
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static SolitairePlayerState Finish(
        SolitaireMatch match, SolitairePlayerState player, DateTime completedAtUtc)
    {
        var elapsedMilliseconds = Math.Max(0,
            (completedAtUtc.Ticks - StartedAt(match, player).Ticks) / TimeSpan.TicksPerMillisecond);
        var won = SolitaireEngine.IsWon(player.Game);
        var finalGame = player.Game with
        {
            Score = SolitaireScoreRules.FinalScore(player.Game, elapsedMilliseconds),
            Message = won ? "Game complete" : "Run complete"
        };
        return player with
        {
            Status = SolitairePlayerStatuses.Finished,
            Game = finalGame,
            Version = checked(player.Version + 1),
            ElapsedMilliseconds = elapsedMilliseconds,
            CompletedAtUtc = completedAtUtc
        };
    }

    private static DateTime LastActionAt(SolitaireMatch match, SolitairePlayerState player)
    {
        var nextOrdinal = player.Game.Moves;
        var lastDelay = ActionDelayMilliseconds(match, player, nextOrdinal);
        return NextActionAt(player).AddMilliseconds(-lastDelay);
    }

    private static int ReadMoveLimit(SolitairePlayerState player) =>
        player.HostMetadata.TryGetValue(MoveLimitKey, out var stored) &&
        int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) &&
        limit is >= 50 and <= 200
            ? limit
            : throw new InvalidOperationException("The managed Solitaire move limit is invalid.");

    private static int ReadCounter(SolitairePlayerState player, string key) =>
        player.HostMetadata.TryGetValue(key, out var stored) &&
        int.TryParse(stored, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : 0;

    private static IReadOnlySet<string> ReadRecentVisibleStates(SolitairePlayerState player) =>
        player.HostMetadata.TryGetValue(RecentVisibleStatesKey, out var stored)
            ? stored.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    private static string AppendRecentVisibleState(SolitairePlayerState player, string fingerprint)
    {
        var recent = player.HostMetadata.TryGetValue(RecentVisibleStatesKey, out var stored)
            ? stored.Split(',', StringSplitOptions.RemoveEmptyEntries)
            : [];
        return string.Join(',', recent.Append(fingerprint).TakeLast(RecentVisibleStateLimit));
    }

    private static bool MadeProgress(
        SolitaireGameState before, SolitaireGameState after, SolitaireCommandRequest command) =>
        after.Foundations.Sum(pile => pile.Count) > before.Foundations.Sum(pile => pile.Count) ||
        after.Tableau.Sum(pile => pile.Count(card => !card.FaceUp)) <
            before.Tableau.Sum(pile => pile.Count(card => !card.FaceUp)) ||
        command.From?.Zone == "waste" && command.To?.Zone == "tableau";

    private static int MoveLimit(SolitaireMatch match, SolitairePlayerState player)
    {
        var random = Random(match, player, "move-limit");
        return SolitaireManagedPlayerPolicy.Skill(player) switch
        {
            ManagedPlayerSkillLevels.Poor => 70 + random.Next(36),
            ManagedPlayerSkillLevels.Average => 105 + random.Next(41),
            _ => 135 + random.Next(46)
        };
    }

    private static int ActionDelayMilliseconds(
        SolitaireMatch match, SolitairePlayerState player, int ordinal)
    {
        var random = Random(match, player, $"action-delay:{ordinal}");
        var (minimum, spread) = SolitaireManagedPlayerPolicy.Skill(player) switch
        {
            ManagedPlayerSkillLevels.Poor => (2_800, 4_600),
            ManagedPlayerSkillLevels.Average => (1_900, 3_900),
            _ => (1_400, 3_500)
        };
        var tempo = 80 + Random(match, player, "tempo").Next(61);
        var delay = (minimum + random.Next(spread)) * tempo / 100;
        if (random.Next(100) < 8) delay += 3_000 + random.Next(8_000);
        return ordinal == 0 ? Math.Min(5_000, delay) : delay;
    }

    private static int DecisionSkill(SolitaireMatch match, SolitairePlayerState player)
    {
        var skill = SolitaireManagedPlayerPolicy.Skill(player);
        var random = Random(match, player, $"decision-skill:{player.Game.Moves}");
        var carelessness = Random(match, player, "carelessness").Next(10);
        return skill switch
        {
            ManagedPlayerSkillLevels.Strong when random.Next(100) < carelessness =>
                ManagedPlayerSkillLevels.Average,
            ManagedPlayerSkillLevels.Average when random.Next(100) < carelessness * 2 =>
                ManagedPlayerSkillLevels.Poor,
            _ => skill
        };
    }

    private static ulong DecisionSeed(SolitaireMatch match, SolitairePlayerState player)
    {
        var random = Random(match, player, $"decision:{player.Game.Moves}");
        return ((ulong)(uint)random.Next(int.MaxValue) << 32) |
            (uint)random.Next(int.MaxValue);
    }

    private static DeterministicManagedPlayerRandom Random(
        SolitaireMatch match, SolitairePlayerState player, string stream) =>
        new(((ulong)match.DealSeed << 32) | (uint)player.Seat,
            $"solitaire-progress:{player.UserId}:{stream}");

    private static void RequireManaged(SolitairePlayerState player)
    {
        if (player.IsAccountBacked)
            throw new ArgumentException("A managed player policy cannot advance a real account.", nameof(player));
    }

    private static DateTime StartedAt(SolitaireMatch match, SolitairePlayerState player) =>
        player.StartedAtUtc > DateTime.UnixEpoch ? player.StartedAtUtc : match.StartedAtUtc;

    private static DateTime DeadlineAt(SolitaireMatch match, SolitairePlayerState player) =>
        player.DeadlineAtUtc > DateTime.UnixEpoch ? player.DeadlineAtUtc : match.DeadlineAtUtc;
}
