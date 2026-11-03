using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Spins;

namespace FortuneForge.Server.Slots.Bonuses;

// In-memory fixture bank, not production inventory, entitlement or persistence.
public sealed class WukongPawPrototypeSession
{
    public const string EarnSymbolId = "PAW_EARN_SCROLL";
    private readonly object gate = new();
    private readonly string owner;
    private readonly GameDefinition game;
    private readonly Action<long> validateWager;
    private readonly Func<WukongPawPrototypeAdmission, SpinResult> spin;
    private readonly Dictionary<string, Receipt> receipts = new(StringComparer.Ordinal);
    private readonly List<WukongPawPrototypeGrant> grants = [];
    private string sessionId = "";
    private long wagerPoints = 100;
    private string phase = "ready";
    private WukongPawPrototypePlacement? placement;
    private IReadOnlyList<IReadOnlyList<string>> reels = [];
    private WukongPawPrototypeResult? lastResult;
    private string? error;
    private bool injectEarnSymbol = true;

    public WukongPawPrototypeSession(string owner, GameDefinition game, SpinService engine)
        : this(owner, game, wager => engine.ValidateRequest(game.Id, wager), engine.SpinWukongPawPrototype) { }

    internal WukongPawPrototypeSession(string owner, GameDefinition game, Action<long> validateWager,
        Func<WukongPawPrototypeAdmission, SpinResult> spin)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("A synthetic owner is required.", nameof(owner));
        if (game.Id != "wukong-journey-v1" || game.Layout.ReelCount < 1 || game.Layout.VisibleRows < 1)
            throw new ArgumentException("The actual Wukong definition is required.", nameof(game));
        this.owner = owner; this.game = game; this.validateWager = validateWager; this.spin = spin;
        foreach (var wager in new long[] { 50, 100, 200 }) validateWager(wager);
        ResetCore();
    }

    public WukongPawPrototypeState Get(string requestingOwner)
    {
        lock (gate) { CheckOwner(requestingOwner); return Snapshot(); }
    }

    public WukongPawPrototypeState Place(string requestingOwner, WukongPawPrototypePlaceRequest request) =>
        Change(requestingOwner, request.RequestId, "place", request, () =>
        {
            RequirePhase("ready"); ValidateWager(request.WagerPoints);
            if (request.Column < 0 || request.Column >= game.Layout.ReelCount) Fail("invalid-column", "Choose one existing reel column.", 400);
            var grant = grants.SingleOrDefault(value => value.Id == request.GrantId)
                ?? throw new WukongPawPrototypeException("unavailable-grant", "This server fixture paw is unavailable.");
            if (grant.WagerPoints != request.WagerPoints) Fail("fixture-wager-mismatch", "This illustrative fixture paw is bound to a different wager; production binding is not selected.");
            placement = new(NewId(), grant.Id, request.Column, request.WagerPoints);
            wagerPoints = request.WagerPoints; phase = "placed";
        });

    public WukongPawPrototypeState Clear(string requestingOwner, WukongPawPrototypeClearRequest request) =>
        Change(requestingOwner, request.RequestId, "clear", request, () =>
        {
            RequirePhase("placed");
            if (placement?.Id != request.PlacementId) Fail("placement-mismatch", "Only the current unspun placement can be cleared.");
            placement = null; phase = "ready";
        });

    public WukongPawPrototypeState NextPreview(string requestingOwner, WukongPawPrototypeFixtureRequest request) =>
        Change(requestingOwner, request.RequestId, "next-preview", request, () =>
        {
            RequirePhase("settled"); placement = null; phase = "ready";
        });

    public WukongPawPrototypeState ResetFixture(string requestingOwner, WukongPawPrototypeFixtureRequest request) =>
        Change(requestingOwner, request.RequestId, "reset-fixture", request, ResetCore);

    public WukongPawPrototypeState SetEarnFixture(string requestingOwner, WukongPawPrototypeEarnFixtureRequest request) =>
        Change(requestingOwner, request.RequestId, "fixture-earn-symbol", request, () => injectEarnSymbol = request.Enabled);

    public WukongPawPrototypeState Spin(string requestingOwner, WukongPawPrototypeSpinRequest request)
    {
        var fingerprint = Fingerprint("spin", request);
        WukongPawPrototypeAdmission admission;
        lock (gate)
        {
            CheckOwner(requestingOwner); CheckRequestId(request.RequestId);
            if (Replay(request.RequestId, fingerprint) is { } replay) return replay;
            if (phase is not ("ready" or "placed")) Fail("invalid-phase", "Start the next preview before another spin.");
            ValidateWager(request.WagerPoints);
            if (phase == "placed")
            {
                if (placement is null || request.PlacementId != placement.Id) Fail("placement-mismatch", "The staged placement identity is required.");
                if (placement!.WagerPoints != request.WagerPoints) Fail("fixture-wager-mismatch", "A staged fixture paw cannot move to another wager.");
                if (!grants.Any(value => value.Id == placement.GrantId)) Fail("unavailable-grant", "The selected fixture paw is unavailable.");
            }
            else if (request.PlacementId is not null) Fail("placement-mismatch", "There is no staged fixture paw.");
            admission = new(sessionId, request.WagerPoints, placement?.Column, injectEarnSymbol,
                NewId(), placement?.GrantId);
            // Reserve before invoking the engine. A failed fixture never silently
            // refunds/regrants; production failure/entitlement rules are undecided.
            if (placement is not null) grants.RemoveAll(value => value.Id == placement.GrantId);
            wagerPoints = request.WagerPoints; phase = "spinning";
            receipts.Add(request.RequestId, new(fingerprint, null));
        }
        SpinResult result;
        try { result = spin(admission); }
        catch (Exception)
        {
            lock (gate)
            {
                phase = "blocked";
                error = "Synthetic engine failed. The placed fixture paw remains unavailable; reset the fixture explicitly.";
                receipts[request.RequestId] = new(fingerprint, Snapshot(), "fixture-engine-failed");
            }
            throw new WukongPawPrototypeException("fixture-engine-failed", error, 500);
        }
        lock (gate)
        {
            reels = FreezeReels(result.Reels);
            lastResult = new(result.SpinId, result.MonkeyPawCount, result.MoneyGrabPoints, result.Payout.TotalPoints);
            phase = "settled"; error = null;
            var snapshot = Snapshot();
            receipts[request.RequestId] = new(fingerprint, snapshot);
            return snapshot;
        }
    }

    private WukongPawPrototypeState Change(string requestingOwner, string requestId, string kind, object request, Action change)
    {
        lock (gate)
        {
            CheckOwner(requestingOwner); CheckRequestId(requestId);
            var fingerprint = Fingerprint(kind, request);
            if (Replay(requestId, fingerprint) is { } replay) return replay;
            if (phase == "spinning") Fail("spin-in-progress", "The accepted synthetic spin is locked.");
            if (phase == "blocked" && kind != "reset-fixture") Fail("fixture-blocked", "Reset the failed synthetic fixture explicitly.");
            change();
            var snapshot = Snapshot(); receipts.Add(requestId, new(fingerprint, snapshot)); return snapshot;
        }
    }

    private WukongPawPrototypeState? Replay(string requestId, string fingerprint)
    {
        if (!receipts.TryGetValue(requestId, out var receipt)) return null;
        if (receipt.Fingerprint != fingerprint) Fail("request-key-reused", "A request key cannot be reused with changed inputs or another action.");
        if (receipt.State is null) Fail("spin-in-progress", "The accepted synthetic spin is still running; retry the same request.");
        if (receipt.ErrorCode is not null) throw new WukongPawPrototypeException(receipt.ErrorCode, receipt.State!.Error!, 500);
        return receipt.State;
    }

    private void ValidateWager(long wager)
    {
        try { validateWager(wager); }
        catch (ArgumentException) { Fail("invalid-wager", "Wager must satisfy the actual Wukong definition.", 400); }
    }
    private void CheckOwner(string value) { if (value != owner) Fail("wrong-owner", "This is another synthetic fixture session.", 403); }
    private void RequirePhase(string expected) { if (phase != expected) Fail("invalid-phase", "The current placement/result is locked; finish this preview first."); }
    private static void CheckRequestId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) Fail("invalid-request-id", "A bounded nonempty request ID is required.", 400);
    }
    [DoesNotReturn]
    private static void Fail(string code, string message, int status = 409) => throw new WukongPawPrototypeException(code, message, status);
    private static string NewId() => Guid.NewGuid().ToString("N");
    private static string Fingerprint(string kind, object request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + JsonSerializer.Serialize(request))));
    private void ResetCore()
    {
        sessionId = NewId(); grants.Clear();
        for (var index = 0; index < 3; index++) grants.Add(new("fixture-" + NewId(), 100));
        wagerPoints = 100; placement = null; lastResult = null; phase = "ready"; error = null; injectEarnSymbol = true;
        var board = Enumerable.Range(0, game.Layout.ReelCount).Select(column =>
            Enumerable.Range(0, game.Layout.VisibleRows).Select(row => column == 0 && row == 0 ? EarnSymbolId : "3").ToArray()).ToArray();
        reels = FreezeReels(board);
    }
    private WukongPawPrototypeState Snapshot() => new(sessionId, game.Id, game.Wagering.PointValueInCents,
        wagerPoints, Array.AsReadOnly(new long[] { 50, 100, 200 }), Array.AsReadOnly(grants.ToArray()), placement,
        phase, reels, lastResult, injectEarnSymbol, reels.SelectMany(reel => reel).Count(symbol => symbol == EarnSymbolId), new(EarnSymbolId), error);
    private static IReadOnlyList<IReadOnlyList<string>> FreezeReels(IEnumerable<IEnumerable<string>> source) =>
        Array.AsReadOnly(source.Select(reel => (IReadOnlyList<string>)Array.AsReadOnly(reel.ToArray())).ToArray());
    private sealed record Receipt(string Fingerprint, WukongPawPrototypeState? State, string? ErrorCode = null);
}
