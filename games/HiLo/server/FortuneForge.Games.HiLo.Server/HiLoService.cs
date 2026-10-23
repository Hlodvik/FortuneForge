using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.HiLo.Server;

public sealed class HiLoService(IHiLoStore store, TimeProvider? timeProvider = null, Func<PlayingCard>? draw = null, bool allowNewMutations = true)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Func<PlayingCard> draw = draw ?? SecureDraw;
    public Task<HiLoSessionResponse> GetSessionAsync(string userId, CancellationToken cancellationToken = default)
    { ValidateUser(userId); return store.GetSessionAsync(userId, cancellationToken); }
    public Task<HiLoSessionResponse> StartAsync(string userId, HiLoStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute(userId, new(request.OperationId, "start", WagerCents: request.WagerCents), cancellationToken);
    }
    public Task<HiLoSessionResponse> ActAsync(string userId, string roundId, HiLoActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute(userId, new(request.OperationId, "action", RoundId: roundId, ExpectedRevision: request.ExpectedRevision, Choice: request.Choice), cancellationToken);
    }
    private Task<HiLoSessionResponse> Execute(string userId, HiLoCommand command, CancellationToken cancellationToken)
    {
        ValidateUser(userId); ValidateCommand(command);
        return store.ExecuteAsync(userId, command, draw, clock.GetUtcNow(), cancellationToken, allowNewMutations);
    }
    public static void ValidateCommand(HiLoCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.OperationId) || command.OperationId.Length is < 16 or > 128 ||
            command.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new HiLoException("invalid_request", "operationId requires 16–128 letters, digits, hyphens or underscores.", 400);
        if (command.Kind == "start")
        {
            if (command.WagerCents is < 10 or > 100_000)
                throw new HiLoException("invalid_request", "Wagers must be from 10 through 100000 whole cents.", 400);
        }
        else if (command.Kind == "action")
        {
            if (!Guid.TryParseExact(command.RoundId, "N", out _) || command.ExpectedRevision < 0 ||
                command.Choice is not ("higher" or "lower" or "skip" or "cashout"))
                throw new HiLoException("invalid_request", "A valid roundId, expectedRevision and choice are required.", 400);
        }
        else throw new HiLoException("invalid_request", "Unknown command.", 400);
    }
    private static void ValidateUser(string userId)
    { if (string.IsNullOrWhiteSpace(userId)) throw new HiLoException("authentication_required", "Sign in to play Hi-Lo.", 401); }
    public static PlayingCard SecureDraw() => new((CardRank)(RandomNumberGenerator.GetInt32(13) + 1), (CardSuit)RandomNumberGenerator.GetInt32(4));
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Fingerprint(HiLoCommand command) => Hash(JsonSerializer.Serialize(command));
    public static long Cents(decimal credits) => checked((long)(credits * 100m));
}

internal static class HiLoMutations
{
    internal static HiLoMutation Apply(HiLoStoredState state, HiLoCommand command, Func<PlayingCard> draw, bool practice, bool allowNewMutations = true)
    {
        HiLoService.ValidateCommand(command);
        if (!allowNewMutations && (command.Kind == "start" || command.Choice != "cashout"))
            throw new HiLoException("hi_lo_disabled", "Hi-Lo is paused. Existing rounds can still be resumed and cashed out.", 503);
        if (state.BalanceCents < 0) throw new InvalidOperationException("The persisted Hi-Lo wallet is corrupt.");
        HiLoRound round;
        long delta;
        string? ledger;
        if (command.Kind == "start")
        {
            if (state.Round?.Status == HiLoRoundStatus.Active) throw new HiLoException("round_active", "Finish or cash out your current round before placing another wager.");
            if (state.BalanceCents < command.WagerCents) throw new HiLoException("insufficient_funds", "The wallet has insufficient credits for this wager.");
            round = HiLoEngine.Start(Guid.NewGuid(), command.WagerCents / 100m, draw());
            delta = -command.WagerCents; ledger = "hi-lo-wager";
        }
        else
        {
            round = state.Round ?? throw new HiLoException("round_not_found", "The Hi-Lo round could not be found.", 404);
            if (round.Id.ToString("N") != command.RoundId) throw new HiLoException("round_not_found", "The Hi-Lo round could not be found.", 404);
            if (round.Version != command.ExpectedRevision) throw new HiLoException("stale_revision", "This round changed. Refresh the session before choosing again.");
            if (round.Status != HiLoRoundStatus.Active) throw new HiLoException("round_finished", "This round is already finished.");
            try
            {
                switch (command.Choice)
                {
                    case "higher": HiLoEngine.EnsureCanGuess(round, HiLoDirection.Higher); round = HiLoEngine.Guess(round, HiLoDirection.Higher, draw()); break;
                    case "lower": HiLoEngine.EnsureCanGuess(round, HiLoDirection.Lower); round = HiLoEngine.Guess(round, HiLoDirection.Lower, draw()); break;
                    case "skip": HiLoEngine.EnsureCanSkip(round); round = HiLoEngine.Skip(round, draw()); break;
                    case "cashout": round = HiLoEngine.CashOut(round); break;
                    default: throw new HiLoException("invalid_request", "Unknown choice.", 400);
                }
            }
            catch (InvalidOperationException exception) { throw new HiLoException("invalid_choice", exception.Message); }
            delta = round.Status is HiLoRoundStatus.CashedOut or HiLoRoundStatus.AutoCashedOut ? HiLoService.Cents(round.Payout) : 0;
            ledger = delta > 0 ? "hi-lo-payout" : null;
        }
        var next = new HiLoStoredState(checked(state.BalanceCents + delta), round);
        return new(next, Snapshot(next, practice) with { Enabled = allowNewMutations }, delta, ledger);
    }
    internal static HiLoSessionResponse Snapshot(HiLoStoredState state, bool practice) => new(practice, state.BalanceCents, new(), state.Round is null ? null : PublicRound(state.Round));
    private static HiLoRoundResponse PublicRound(HiLoRound round) => new(round.Id.ToString("N"), round.Version,
        round.Status switch { HiLoRoundStatus.Active => "active", HiLoRoundStatus.Lost => "lost", HiLoRoundStatus.CashedOut => "cashedOut", _ => "maximumReturn" },
        HiLoService.Cents(round.Wager), Card(round.CurrentCard), History(round), HiLoService.Cents(round.CurrentReturn),
        round.Multiplier, round.Streak, round.Skips, Offer(round, HiLoDirection.Higher), Offer(round, HiLoDirection.Lower),
        round.CanCashOut, round.Status == HiLoRoundStatus.Active && round.Skips < 52);
    private static HiLoCardResponse Card(PlayingCard card) => new(card.Rank.ToString().ToLowerInvariant(), card.Suit.ToString().ToLowerInvariant());
    private static IReadOnlyList<HiLoHistoryResponse> History(HiLoRound round) => round.History.Select(entry =>
        new HiLoHistoryResponse(Card(entry.Card), entry.Kind switch { HiLoHistoryKind.Initial => "start", HiLoHistoryKind.Skip => "skip", _ => entry.Direction!.Value.ToString().ToLowerInvariant() },
            entry.Kind switch { HiLoHistoryKind.Win => true, HiLoHistoryKind.Loss => false, _ => null })).ToArray();
    private static HiLoOfferResponse Offer(HiLoRound round, HiLoDirection direction)
    {
        var odds = HiLoRules.GetOdds(round.CurrentCard.Rank, direction);
        if (round.Status != HiLoRoundStatus.Active || !odds.Available) return new(odds.Probability, 0, 0, false);
        var offer = HiLoRules.Quote(round, direction);
        return new(odds.Probability, offer.Multiplier, HiLoService.Cents(offer.Return), true);
    }
}
