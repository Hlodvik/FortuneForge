using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonDiscoRoundStateTests
{
    [Fact]
    public void LegacySavedRoundAdoptsOnlyItsFirstDiscoWagerAndKeepsItsAccumulatedWin()
    {
        var guard = new Dictionary<string, object>
        {
            ["neonDiscoSpinsRemaining"] = 10L,
            ["neonDiscoRoundWinPoints"] = 400L
        };
        var saved = NeonDiscoRoundState.Read(Reader(guard));
        Assert.Equal(new(10, null, NeonDiscoCascade.InitialMultiplier, 400), saved);

        Assert.Equal((false, saved), saved.Admit(usesAwardedPlay: true, requestedWager: 500));
        var (isDisco, admitted) = saved.Admit(usesAwardedPlay: false, requestedWager: 300);
        Assert.True(isDisco);
        admitted.WriteGuard(guard, includeRoundWin: false);
        Assert.Equal(400L, guard["neonDiscoRoundWinPoints"]);
        Assert.Equal(300L, guard["neonDiscoWagerPoints"]);
        Assert.Equal(admitted, NeonDiscoRoundState.Read(Reader(guard)));
        Assert.Throws<NeonDiscoWagerLockedException>(() => admitted.Admit(usesAwardedPlay: false, requestedWager: 500));
    }

    [Fact]
    public void SavedAdmissionFieldsMatchTheLegacySchemaAndRejectChangedRoundState()
    {
        var round = NeonDiscoRoundState.Grant(300);
        var admission = new Dictionary<string, object>();
        round.WriteAdmission(admission, isDiscoPlay: true, includeNeonDetails: true);
        Assert.Equal(4, admission.Count);
        Assert.Equal(true, admission["isNeonDiscoPlay"]);
        Assert.Equal(12, admission["neonDiscoSpinsBefore"]);
        Assert.Equal(300L, admission["neonDiscoWagerPointsBefore"]);
        Assert.Equal(NeonDiscoCascade.InitialMultiplier, admission["neonDiscoMultiplierBefore"]);
        Assert.True(NeonDiscoRoundState.ReadAdmittedPlay(field => (bool)admission[field]));
        Assert.Equal(round.Multiplier, NeonDiscoRoundState.ReadAdmittedMultiplier(Reader(admission)));
        round.ValidateSettlement(Reader(admission), round.Multiplier, true, false, 300);
        Assert.Throws<InvalidOperationException>(() =>
            (round with { SpinsRemaining = 11 }).ValidateSettlement(Reader(admission), round.Multiplier, true, false, 300));
        Assert.Throws<NeonDiscoWagerLockedException>(() =>
            round.ValidateSettlement(Reader(admission), round.Multiplier, true, false, 500));
    }

    [Fact]
    public void LastSettledPlayKeepsTheFinalWinInItsReceiptAndClearsTheSavedWager()
    {
        var round = new NeonDiscoRoundState(1, 300, 3, 550);
        var completed = round.Settle(consumedDiscoPlay: true, wagerPoints: 300, payoutPoints: 350,
            cascade: null, evidence: null);
        Assert.Equal(new(0, null, NeonDiscoCascade.InitialMultiplier, 900), completed);
        Assert.Equal(900, completed.ReceiptWinPoints(consumedDiscoPlay: true));

        var guard = new Dictionary<string, object>();
        completed.WriteGuard(guard);
        Assert.Equal(0L, guard["neonDiscoWagerPoints"]);
        Assert.Equal(900L, guard["neonDiscoRoundWinPoints"]);
        Assert.Equal(NeonDiscoRoundState.Inactive, NeonDiscoRoundState.Read(Reader(guard)));
    }

    private static Func<string, long?> Reader(IReadOnlyDictionary<string, object> data) =>
        field => data.TryGetValue(field, out var value) ? Convert.ToInt64(value) : null;
}
