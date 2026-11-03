using System.Text.Json;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class WukongPawPrototypeSessionTests
{
    private const string Owner = "synthetic-browser-owner";

    [Fact]
    public void Fixture_bank_and_distinct_earn_role_are_explicitly_nonproduction_and_unconfigured()
    {
        var session = Fixture(); var state = session.Get(Owner);
        Assert.Equal("synthetic-local", state.Mode);
        Assert.False(state.LiveEarningEnabled); Assert.False(state.LiveConsumptionEnabled);
        Assert.Equal("ready", state.Phase); Assert.Equal(new long[] { 50, 100, 200 }, state.WagerOptions);
        Assert.Equal(3, state.Grants.Count); Assert.All(state.Grants, grant => Assert.Equal(100, grant.WagerPoints));
        Assert.Equal(3, state.Grants.Select(grant => grant.Id).Distinct().Count());
        Assert.Equal("PAW_EARN_SCROLL", state.EarnRole.SymbolId);
        Assert.Null(state.EarnRole.Threshold); Assert.Null(state.EarnRole.Rate); Assert.Null(state.EarnRole.WagerBinding);
        Assert.Equal(1, state.ObservedEarnScrolls); Assert.True(state.FixtureEarnSymbolInjection);
        Assert.DoesNotContain(state.EarnRole.SymbolId, new[] { "3", "PAW", "BANANA", "SEAL_PAW", "SEAL_SYNC", "SEAL_ROWS", "SEAL_RAND" });
        Assert.Equal(state.SessionId, session.Get(Owner).SessionId);
        Assert.Equal(state.Grants, session.Get(Owner).Grants); // Reload reads the same bank; no automatic grants.
    }

    [Fact]
    public void Ownership_grant_wager_column_and_single_placement_are_authoritative()
    {
        var session = Fixture(); var grant = session.Get(Owner).Grants[0];
        Reject(() => session.Place("someone-else", new(grant.Id, 0, 100, "wrong-owner")), "wrong-owner");
        var otherGrant = Fixture().Get(Owner).Grants[0];
        Reject(() => session.Place(Owner, new(otherGrant.Id, 0, 100, "wrong-grant")), "unavailable-grant");
        Reject(() => session.Place(Owner, new(grant.Id, 0, 50, "wrong-wager")), "fixture-wager-mismatch");
        Reject(() => session.Place(Owner, new(grant.Id, -1, 100, "negative-column")), "invalid-column");
        Reject(() => session.Place(Owner, new(grant.Id, 5, 100, "past-column")), "invalid-column");
        var placed = session.Place(Owner, new(grant.Id, 2, 100, "place"));
        Assert.Equal("placed", placed.Phase); Assert.Equal(2, placed.Placement!.Column);
        Reject(() => session.Place(Owner, new(grant.Id, 3, 100, "another-placement")), "invalid-phase");
        Assert.Equal(placed.Placement, session.Get(Owner).Placement);
        var cleared = session.Clear(Owner, new(placed.Placement.Id, "clear"));
        Assert.Null(cleared.Placement); Assert.Equal(3, cleared.Grants.Count);
    }

    [Fact]
    public void Request_keys_bind_action_and_inputs_and_successful_responses_are_immutable_receipts()
    {
        var session = Fixture(); var grant = session.Get(Owner).Grants[0];
        var request = new WukongPawPrototypePlaceRequest(grant.Id, 1, 100, "same-key");
        var first = session.Place(Owner, request);
        Assert.Same(first, session.Place(Owner, request));
        Reject(() => session.Place(Owner, request with { Column = 2 }), "request-key-reused");
        Reject(() => session.ResetFixture(Owner, new("same-key")), "request-key-reused");
        Reject(() => session.Clear(Owner, new(first.Placement!.Id, " ")), "invalid-request-id");
        Assert.Same(first, session.Place(Owner, request));
    }

    [Fact]
    public void Only_deliberately_placed_fixture_paw_is_used_once_and_accepted_result_cannot_be_edited()
    {
        var calls = 0; WukongPawPrototypeAdmission? seen = null;
        var session = Fixture(admission => { calls++; seen = admission; return Outcome(admission.WagerPoints); });
        var initial = session.Get(Owner); var grant = initial.Grants[1];
        var placed = session.Place(Owner, new(grant.Id, 3, 100, "place"));
        Reject(() => session.Spin(Owner, new(placed.Placement!.Id, 50, "mismatch")), "fixture-wager-mismatch");
        var request = new WukongPawPrototypeSpinRequest(placed.Placement!.Id, 100, "spin");
        var result = session.Spin(Owner, request);
        Assert.Equal(1, calls); Assert.Equal("settled", result.Phase);
        Assert.Equal(grant.Id, seen!.GrantId); Assert.Equal(3, seen.ForcedPawColumn);
        Assert.Equal(initial.SessionId, seen.SessionId); Assert.False(string.IsNullOrEmpty(seen.AdmissionId));
        Assert.Equal(2, result.Grants.Count); Assert.DoesNotContain(result.Grants, value => value.Id == grant.Id);
        Assert.Same(result, session.Spin(Owner, request)); Assert.Equal(1, calls);
        Reject(() => session.Spin(Owner, request with { WagerPoints = 200 }), "request-key-reused");
        Reject(() => session.Clear(Owner, new(placed.Placement.Id, "late-clear")), "invalid-phase");
        Reject(() => session.Place(Owner, new(grant.Id, 4, 100, "late-edit")), "invalid-phase");
        var next = session.NextPreview(Owner, new("next"));
        Assert.Equal("ready", next.Phase); Assert.Null(next.Placement);
        Assert.Equal(result.Grants, next.Grants); Assert.Same(result.LastResult, next.LastResult); Assert.Same(result.Reels, next.Reels);
        Reject(() => session.Place(Owner, new(grant.Id, 0, 100, "spent-grant")), "unavailable-grant");
        session.Place(Owner, new(next.Grants[0].Id, 0, 100, "next-paw"));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Unplaced_low_and_high_preview_wagers_never_use_banked_paws(long wager)
    {
        WukongPawPrototypeAdmission? seen = null;
        var session = Fixture(admission => { seen = admission; return Outcome(admission.WagerPoints); });
        var before = session.Get(Owner);
        var after = session.Spin(Owner, new(null, wager, "unplaced"));
        Assert.Equal(before.Grants, after.Grants); Assert.Equal(wager, seen!.WagerPoints);
        Assert.Null(seen.ForcedPawColumn); Assert.Null(seen.GrantId); Assert.NotNull(seen.AdmissionId);
        Assert.Equal(0, after.LastResult!.TotalPoints); // A zero result is still a completed preview, never an earn.
        var ready = session.NextPreview(Owner, new("next"));
        Assert.Equal(before.Grants, ready.Grants); Assert.Equal(ready.Grants, session.Get(Owner).Grants);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(3)]
    [InlineData(2002)]
    public void Actual_definition_limits_reject_zero_negative_increment_and_above_maximum_before_engine(long wager)
    {
        var calls = 0; var session = Fixture(admission => { calls++; return Outcome(admission.WagerPoints); });
        Reject(() => session.Spin(Owner, new(null, wager, "invalid")), "invalid-wager");
        Assert.Equal(0, calls); Assert.Equal(3, session.Get(Owner).Grants.Count);
    }

    [Fact]
    public void Earn_symbol_illustration_is_toggleable_but_never_grants_and_explicit_reset_is_idempotent()
    {
        var session = Fixture(); var before = session.Get(Owner);
        var off = session.SetEarnFixture(Owner, new(false, "off"));
        Assert.False(off.FixtureEarnSymbolInjection); Assert.Equal(before.Grants, off.Grants);
        var result = session.Spin(Owner, new(null, 50, "spin"));
        Assert.Equal(before.Grants, result.Grants);
        var reset = session.ResetFixture(Owner, new("reset"));
        Assert.NotEqual(before.SessionId, reset.SessionId); Assert.Equal(3, reset.Grants.Count);
        Assert.DoesNotContain(reset.Grants, value => before.Grants.Any(old => old.Id == value.Id));
        Assert.Same(reset, session.ResetFixture(Owner, new("reset")));
        Assert.Same(result, session.Spin(Owner, new(null, 50, "spin"))); // Historical receipt, no new admission or bank mutation.
        Assert.Equal(reset.Grants, session.Get(Owner).Grants);
    }

    [Fact]
    public void Engine_failure_blocks_fixture_and_preserves_unused_bank_without_an_invented_refund()
    {
        var calls = 0; var session = Fixture(_ => { calls++; throw new InvalidOperationException("fixture engine failed"); });
        var grant = session.Get(Owner).Grants[0]; var placed = session.Place(Owner, new(grant.Id, 0, 100, "place"));
        var request = new WukongPawPrototypeSpinRequest(placed.Placement!.Id, 100, "spin");
        Reject(() => session.Spin(Owner, request), "fixture-engine-failed");
        Reject(() => session.Spin(Owner, request), "fixture-engine-failed");
        Assert.Equal(1, calls); Assert.Equal("blocked", session.Get(Owner).Phase); Assert.Equal(2, session.Get(Owner).Grants.Count);
        Reject(() => session.NextPreview(Owner, new("next")), "fixture-blocked");
        Assert.Equal(3, session.ResetFixture(Owner, new("explicit-reset")).Grants.Count);
    }

    [Fact]
    public async Task Inflight_admission_is_locked_and_concurrent_retry_cannot_run_engine_twice()
    {
        using var started = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        var calls = 0; var session = Fixture(admission =>
        {
            Interlocked.Increment(ref calls); started.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release fixture spin.");
            return Outcome(admission.WagerPoints);
        });
        var grant = session.Get(Owner).Grants[0]; var placed = session.Place(Owner, new(grant.Id, 0, 100, "place"));
        var request = new WukongPawPrototypeSpinRequest(placed.Placement!.Id, 100, "spin");
        var pending = Task.Run(() => session.Spin(Owner, request));
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5))); Assert.Equal("spinning", session.Get(Owner).Phase);
            Reject(() => session.Spin(Owner, request), "spin-in-progress");
            Reject(() => session.Spin(Owner, request with { WagerPoints = 200 }), "request-key-reused");
            Reject(() => session.Clear(Owner, new(placed.Placement.Id, "clear")), "spin-in-progress");
            Reject(() => session.ResetFixture(Owner, new("reset")), "spin-in-progress");
        }
        finally { finish.Set(); }
        var result = await pending;
        Assert.Same(result, session.Spin(Owner, request)); Assert.Equal(1, calls);
    }

    private static void Reject(Action action, string code) => Assert.Equal(code, Assert.Throws<WukongPawPrototypeException>(action).Code);
    private static WukongPawPrototypeSession Fixture(Func<WukongPawPrototypeAdmission, SpinResult>? callback = null)
    {
        using var configuration = JsonDocument.Parse(File.ReadAllText(ConfigurationPath()));
        var configured = configuration.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var definitions = new OptionsSlotsDefinitionProvider(Options.Create(configured));
        var random = new CryptoRandomIndexSource();
        var engine = new SpinService(definitions, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
        var game = definitions.GetGame("wukong-journey-v1")!;
        return new(Owner, game, wager => engine.ValidateRequest(game.Id, wager), callback ?? (admission => Outcome(admission.WagerPoints)));
    }
    private static SpinResult Outcome(long wager) => new(Guid.NewGuid(), "wukong-journey-v1", "fixture-reels", "fixture-symbols", "fixture-paytable", wager, 25,
        [0, 0, 0, 0, 0], Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)new[] { "3", "4", "5" }).ToArray(), new(0, []), 0, false, 0, 0, 0, false);
    private static string ConfigurationPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "FortuneForge.Server", "appsettings.json");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("Actual Wukong fixture configuration was not found.");
    }
}
