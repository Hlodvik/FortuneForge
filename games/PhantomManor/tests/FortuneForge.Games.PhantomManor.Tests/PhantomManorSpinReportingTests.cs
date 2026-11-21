using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PhantomManor.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.PhantomManor.Tests;

public sealed class PhantomManorSpinReportingTests
{
    private const string Account = "phantom-manor-report-compatibility";

    [Fact]
    public void AuthoredPawFeatureAddsTwoWildsWithoutDirectValueTokensOrCollectors()
    {
        var engine = new PhantomManorEngine(new ScriptedDraws(new int[5]));
        var spin = engine.Spin(3, Account, featureMode: "paw", isFreeSpin: true);
        var baseWilds = spin.Audit.ReelsAfterFrequency.Sum(reel => reel.Count(symbol => symbol == "ACE"));

        Assert.Equal("paw", PhantomManorEngine.Profile.ScatterFeatureMode);
        Assert.Equal(1.139m, spin.Audit.EffectiveGame.Math.PayoutMultiplier);
        Assert.Equal(0.32m, spin.Audit.EffectiveGame.Math.Targets.HitRate);
        Assert.Equal(baseWilds + 2, spin.Reels.Sum(reel => reel.Count(symbol => symbol == "ACE")));
        Assert.DoesNotContain(spin.Reels.SelectMany(reel => reel), symbol => symbol == "PAW" || symbol.StartsWith("RAND_", StringComparison.Ordinal));
        Assert.Equal(0, spin.CollectorPoints);
        Assert.Empty(spin.CollectionsAwarded);
        Assert.Equal(0, spin.EnergyAwarded);
    }

    [Fact]
    public void SignedOutcomeAdapterAcceptsHistoricalOddFreeWagerAndRejectsForeignRules()
    {
        var outcomes = new PhantomManorPaidSlotOutcomes(new ScriptedDraws(new int[5]));
        var request = new PaidSlotOutcomeRequest(Guid.NewGuid(), PhantomManorEngine.GameId, Account,
            3, true, false, 96, "paw", 0, null);
        var resolved = outcomes.Resolve(request);
        var spin = resolved.Outcome.Deserialize<ClassicSlotSpinOutcome>(RoundAuditCapture.Json)!;

        Assert.Equal(request.AdmissionId, resolved.AdmissionId);
        Assert.Equal(PaidSlotOutcomeProtocol.ClassicSlotKind, resolved.OutcomeKind);
        Assert.Equal(3, spin.WagerPoints);
        Assert.Equal("paw", spin.Audit.FeatureMode);
        Assert.Equal(96, spin.Audit.EnergyBalanceBefore);
        Assert.Throws<ArgumentException>(() => outcomes.Resolve(request with { GameId = "another-game-v1" }));
        Assert.Throws<ArgumentException>(() => outcomes.Resolve(request with { MathModelId = "foreign-math" }));
        Assert.Throws<ArgumentException>(() => outcomes.Resolve(request with { IsFreeSpin = false }));
        Assert.Throws<ArgumentException>(() => outcomes.Resolve(request with
        {
            GameOptions = new Dictionary<string, JsonElement> { ["foreignFeature"] = JsonSerializer.SerializeToElement(true) }
        }));
    }

    [Fact]
    public void AllTwentyThreeClassicsRetainTheirPositionalPayoutIds()
    {
        var game = PhantomManorEngine.CreateGame();
        var patterns = ClassicSlotDefinitionFactory.CreatePaylines();
        Assert.Equal(23, game.Layout.PaylineCount);
        Assert.Equal(23, game.Paylines.Count);
        for (var index = 0; index < patterns.Count; index++)
        {
            var path = patterns[index];
            Assert.Equal(path, game.Paylines[index]);
            var board = path.Select((winningRow, reel) => (IReadOnlyList<string>)Enumerable.Range(0, 4)
                .Select(row => row == winningRow ? "7" : ((row + reel) % 5 + 2).ToString()).ToArray()).ToArray();
            var evaluated = ClassicSlotEngine.Evaluate(board, game, PhantomManorEngine.CreateSymbolSet());
            var paid = ClassicSlotEngine.CalculatePayout(evaluated, game, PhantomManorEngine.CreatePaytable(), 100);
            var line = Assert.Single(paid.Paylines, candidate => candidate.PaylineId == index + 1);
            var match = Assert.Single(line.Matches).Match;
            Assert.Equal("7", match.SymbolId);
            Assert.Equal(path.Select((row, reel) => new GridPosition(reel, row)), match.Positions);
        }
    }

    [Fact]
    public void AuthoredBasePayoutExcludesCollectorsAndDormantMeters()
    {
        var before = new ClassicSlotRoundState(0, 0, null, 0, 100, 0,
            new Dictionary<string, long> { ["sync"] = 4 }, new Dictionary<string, long> { ["sync"] = 400 });
        var request = new PhantomManorRoundRequest("phantom-manor-cash-collector", 100, false);
        var round = PhantomManorRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null,
            new ScriptedDraws(new int[5]));
        var settled = Settle(round);
        var response = PhantomManorSpinResponse.From(settled);
        var report = PhantomManorSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.Equal(0, response.MonkeyPawCount);
        Assert.Equal(0, response.MoneyGrabPoints);
        Assert.DoesNotContain(response.Payout.Paylines, line => line.PaylineId == 901);
        Assert.Equal(round.Outcome.Payout.TotalPoints, response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(1m, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyAwarded);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Empty(response.SealsAwarded);
        Assert.Empty(response.SealCollections);
        Assert.Equal(4, round.Progress.After.SealCounts["sync"]);
        Assert.Equal(400, round.Progress.After.SealWagerPoints["sync"]);
        Assert.Equal("phantom-manor-v1", report.Result["gameId"]);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.Contains("-energy-", StringComparison.Ordinal));
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var summary = report.Round.Audit.Summary;
        Assert.Equal("phantom-manor", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(2_500, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var audit = data.GetProperty("engineAudit");
        Assert.Equal(100, audit.GetProperty("energyBalanceBefore").GetInt64());
        Assert.Equal(23, audit.GetProperty("baseGame").GetProperty("layout").GetProperty("paylineCount").GetInt32());
        Assert.Empty(PhantomManorSpinEvidence.InspectNative(summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["phantomManorProgress"]!["after"]!["sealCounts"]!["sync"] = 5;
        AssertMismatch(summary, changed);
        changed["phantomManorProgress"]!["after"]!["sealCounts"]!["sync"] = 4;
        changed["result"]!["moneyGrabPoints"] = 1;
        AssertMismatch(summary, changed);

        var drawsChanged = JsonNode.Parse(data.GetRawText())!.AsObject();
        var draws = Convert.FromBase64String(drawsChanged["phantomManorDraws"]!.GetValue<string>());
        draws[0] ^= 1;
        drawsChanged["phantomManorDraws"] = Convert.ToBase64String(draws);
        AssertMismatch(summary, drawsChanged);

        var receiptChanged = JsonNode.Parse(data.GetRawText())!.AsObject();
        receiptChanged["walletSettlementId"] = "another-wallet-receipt";
        AssertMismatch(summary, receiptChanged);

        var rulesChanged = JsonNode.Parse(data.GetRawText())!.AsObject();
        rulesChanged["engineAudit"]!["effectiveGame"]!["math"]!["payoutMultiplier"] = 1.140m;
        AssertMismatch(summary, rulesChanged);
    }

    [Fact]
    public void ImportedOddSyncWagerRetriggersFiveAndWritesOnlyTheAwardWitness()
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, "sync"), 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var stops = PhantomManorEngine.CreateReelSet().Reels.Select(reel => reel.IndexOf("FREE")).ToArray();
        var round = PhantomManorRoundPolicy.Prepare(Account, new("phantom-manor-sync-retrigger", 3, true), before, DateTime.UtcNow,
            null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = PhantomManorSpinResponse.From(settled);
        var report = PhantomManorSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);

        Assert.True(response.IsFreeSpin);
        Assert.Equal(3, response.WagerPoints);
        Assert.Equal(3, response.FreeSpinWagerPoints);
        Assert.Equal("paw", response.FreeSpinFeatureMode);
        Assert.Equal(5, response.FreeSpinsAwarded);
        Assert.Equal(6, response.FreeSpinsRemaining);
        Assert.Equal("paw", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal(0, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        var award = report.GameplayWitnesses[$"{round.Outcome.SpinId:N}-free-games-award"];
        Assert.Equal("freeGames:phantom-manor-v1", award["currencyId"]);
        Assert.Equal(5L, award["amount"]);
        Assert.Equal(6L, award["balanceAfter"]);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(PhantomManorSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("phantomManorDraws");
        old.Remove("phantomManorProgress");
        old.Remove("engineOutcomeSha256");
        old.Remove("walletSettlementId");
        Assert.False(PhantomManorSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
    }

    private static PhantomManorRoundResult Settle(PhantomManorPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(PhantomManorEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(PhantomManorRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-phantom-manor-native-proof-mismatch", Assert.Single(PhantomManorSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private sealed class ScriptedDraws(int[] stops) : IGameRandomSource
    {
        private int position;
        private bool directValueRolled;

        public int Next(int maximumExclusive)
        {
            if (position < stops.Length) return stops[position++];
            if (maximumExclusive != 100 || directValueRolled) return 0;
            directValueRolled = true;
            return 79;
        }
    }
}
