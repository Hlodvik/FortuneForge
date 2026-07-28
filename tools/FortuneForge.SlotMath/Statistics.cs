using FortuneForge.Server.Slots.Models;

sealed class Statistics(IEnumerable<string> symbolIds)
{
    private readonly List<long> _cyclePayouts = [];

    public long BreakEvenCycles { get; private set; }
    public long FreeSpinsAwarded { get; private set; }
    public long EnergyAwarded { get; private set; }
    public long FullMatchSpins { get; private set; }
    public long HitPayout { get; private set; }
    public long HitSpins { get; private set; }
    public int LastFreeSpinsAwarded { get; private set; }
    public long MaxCyclePayout { get; private set; }
    public long MaxSpinPayout { get; private set; }
    public Dictionary<(string Symbol, int Length), long> MatchPayout { get; } = [];
    public long PaidSpinPayout { get; private set; }
    public long PaidSpins { get; private set; }
    public long PitySpins { get; private set; }
    public long PityPayout { get; private set; }
    public long ProfitCycles { get; private set; }
    public long SpecialBoostSpins { get; private set; }
    public long SpecialPointsAwarded { get; private set; }
    public Dictionary<string, long> SymbolPayout { get; } = symbolIds.ToDictionary(id => id, _ => 0L);
    public long TotalPaidWager { get; private set; }
    public long TotalPayout { get; private set; }
    public long TotalSpins { get; private set; }
    public long ZeroCycles { get; private set; }

    public void RecordSpin(
        SpinPayout payout,
        bool hasFullMatch,
        bool pityTriggered,
        bool isFreeSpin,
        int freeSpinsAwarded,
        int specialPointsAwarded,
        int energyAwarded,
        bool specialBoostApplied,
        long wager)
    {
        TotalSpins++;
        LastFreeSpinsAwarded = freeSpinsAwarded;
        FreeSpinsAwarded = checked(FreeSpinsAwarded + freeSpinsAwarded);
        SpecialPointsAwarded = checked(SpecialPointsAwarded + specialPointsAwarded);
        EnergyAwarded = checked(EnergyAwarded + energyAwarded);
        SpecialBoostSpins += specialBoostApplied ? 1 : 0;
        TotalPayout = checked(TotalPayout + payout.TotalPoints);
        MaxSpinPayout = Math.Max(MaxSpinPayout, payout.TotalPoints);
        if (!isFreeSpin)
        {
            PaidSpins++;
            TotalPaidWager = checked(TotalPaidWager + wager);
            PaidSpinPayout = checked(PaidSpinPayout + payout.TotalPoints);
        }
        if (payout.TotalPoints > 0)
        {
            HitSpins++;
            HitPayout = checked(HitPayout + payout.TotalPoints);
        }
        if (hasFullMatch)
        {
            FullMatchSpins++;
        }
        if (pityTriggered)
        {
            PitySpins++;
            PityPayout = checked(PityPayout + payout.TotalPoints);
        }

        foreach (var match in payout.Paylines.SelectMany(payline => payline.Matches))
        {
            SymbolPayout[match.Match.SymbolId] = checked(SymbolPayout[match.Match.SymbolId] + match.AmountPoints);
            var key = (match.Match.SymbolId, match.Match.MatchLength);
            MatchPayout[key] = checked(MatchPayout.GetValueOrDefault(key) + match.AmountPoints);
        }
    }

    public void RecordCycle(long payout, long wager)
    {
        _cyclePayouts.Add(payout);
        MaxCyclePayout = Math.Max(MaxCyclePayout, payout);
        if (payout == 0)
        {
            ZeroCycles++;
        }
        else if (payout == wager)
        {
            BreakEvenCycles++;
        }
        else if (payout > wager)
        {
            ProfitCycles++;
        }
    }

    public (decimal RuinRate, long MedianEndingBalance) EstimateBankrollDepletion(long wager, int seed)
    {
        const int trials = 10_000;
        const int maximumPaidSpins = 1_000;
        var random = new Random(seed);
        var endingBalances = new long[trials];
        var ruined = 0;

        for (var trial = 0; trial < trials; trial++)
        {
            var balance = checked(wager * 100);
            for (var spin = 0; spin < maximumPaidSpins; spin++)
            {
                if (balance < wager)
                {
                    ruined++;
                    break;
                }

                balance = checked(balance - wager + _cyclePayouts[random.Next(_cyclePayouts.Count)]);
            }
            endingBalances[trial] = balance;
        }

        Array.Sort(endingBalances);
        return (ruined / (decimal)trials, endingBalances[trials / 2]);
    }
}
