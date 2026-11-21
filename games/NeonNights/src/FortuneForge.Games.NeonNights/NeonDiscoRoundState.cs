namespace FortuneForge.Games.NeonNights;

public readonly record struct NeonDiscoRoundState(int SpinsRemaining, long? LockedWagerPoints, int Multiplier, long WinPoints)
{
    private const string SpinsField = "neonDiscoSpinsRemaining";
    private const string LockedWagerField = "neonDiscoWagerPoints";
    private const string MultiplierField = "neonDiscoMultiplier";
    private const string RoundWinField = "neonDiscoRoundWinPoints";
    private const string AdmittedPlayField = "isNeonDiscoPlay";
    private const string AdmittedSpinsField = "neonDiscoSpinsBefore";
    private const string AdmittedWagerField = "neonDiscoWagerPointsBefore";
    private const string AdmittedMultiplierField = "neonDiscoMultiplierBefore";

    public static NeonDiscoRoundState Inactive { get; } = new(0, null, NeonDiscoCascade.InitialMultiplier, 0);

    public static NeonDiscoRoundState Grant(long wagerPoints)
    {
        NeonArcadeSlotDefinitions.ValidateCarryWager(wagerPoints);
        return new(NeonArcadeSlotDefinitions.DiscoPlaySpins, wagerPoints, NeonDiscoCascade.InitialMultiplier, 0);
    }

    // The caller supplies primitive document fields from its current transaction.
    public static NeonDiscoRoundState Read(Func<string, long?> read)
    {
        var remaining = ReadSpinsRemaining(read);
        if (remaining == 0) return Inactive;

        var wager = read(LockedWagerField).GetValueOrDefault();
        return new(remaining, NeonPlayPolicy.DiscoLockedWager(remaining, wager == 0 ? null : wager),
            ReadMultiplier(read(MultiplierField)), read(RoundWinField).GetValueOrDefault());
    }

    public static int ReadSpinsRemaining(Func<string, long?> read)
    {
        var remaining = read(SpinsField).GetValueOrDefault();
        if (remaining < 0 || remaining > NeonArcadeSlotDefinitions.DiscoPlaySpins)
            throw new InvalidOperationException("The saved Neon Disco play balance is invalid.");
        return (int)remaining;
    }

    public static bool ReadAdmittedPlay(Func<string, bool> read) => read(AdmittedPlayField);

    public static int ReadAdmittedMultiplier(Func<string, long?> read) => ReadMultiplier(read(AdmittedMultiplierField));

    public (bool IsDiscoPlay, NeonDiscoRoundState Round) Admit(bool usesAwardedPlay, long requestedWager)
    {
        if (!IsPaidDiscoPlay(usesAwardedPlay)) return (false, this);

        NeonPlayPolicy.ValidateDiscoWager(SpinsRemaining, LockedWagerPoints, requestedWager);
        return (true, this with { LockedWagerPoints = LockedWagerPoints ?? requestedWager });
    }

    public void ValidateSettlement(Func<string, long?> readAdmission, int admittedMultiplier, bool admittedDiscoPlay,
        bool awardedPlayPending, long wagerPoints)
    {
        if (readAdmission(AdmittedSpinsField).GetValueOrDefault() != SpinsRemaining || admittedMultiplier != Multiplier ||
            admittedDiscoPlay != IsPaidDiscoPlay(awardedPlayPending))
            throw new InvalidOperationException("Neon's Disco play state differs from its admitted counter.");
        if (admittedDiscoPlay)
            NeonPlayPolicy.ValidateDiscoWager(SpinsRemaining, LockedWagerPoints, wagerPoints);
    }

    public NeonDiscoRoundState Settle(bool consumedDiscoPlay, long wagerPoints, long payoutPoints,
        NeonDiscoCascadeOutcome? cascade, NeonArcadeEvidence? evidence)
    {
        var remaining = NeonPlayPolicy.NextDiscoSpins(SpinsRemaining, consumedDiscoPlay, evidence);
        return new(remaining,
            NeonPlayPolicy.NextDiscoWager(SpinsRemaining, LockedWagerPoints, consumedDiscoPlay, wagerPoints, evidence),
            NeonPlayPolicy.NextDiscoMultiplier(Multiplier, remaining, cascade, evidence),
            consumedDiscoPlay ? checked(WinPoints + payoutPoints) : WinPoints);
    }

    public long? ReceiptWinPoints(bool consumedDiscoPlay = false) => consumedDiscoPlay || SpinsRemaining > 0 ? WinPoints : null;

    public void WriteGuard(IDictionary<string, object> fields, bool includeRoundWin = true)
    {
        fields[SpinsField] = SpinsRemaining;
        fields[LockedWagerField] = LockedWagerPoints ?? 0L;
        fields[MultiplierField] = Multiplier;
        if (includeRoundWin)
            fields[RoundWinField] = WinPoints;
    }

    public void WriteAdmission(IDictionary<string, object> fields, bool isDiscoPlay, bool includeNeonDetails)
    {
        fields[AdmittedPlayField] = isDiscoPlay;
        fields[AdmittedSpinsField] = SpinsRemaining;
        if (includeNeonDetails)
        {
            fields[AdmittedMultiplierField] = Multiplier;
            fields[AdmittedWagerField] = LockedWagerPoints ?? 0L;
        }
    }

    private bool IsPaidDiscoPlay(bool usesAwardedPlay) => !usesAwardedPlay && SpinsRemaining > 0;

    private static int ReadMultiplier(long? stored)
    {
        var multiplier = stored ?? NeonDiscoCascade.InitialMultiplier;
        if (!NeonDiscoCascade.IsValidMultiplier(multiplier))
            throw new InvalidOperationException("The saved Neon Disco multiplier is invalid.");
        return (int)multiplier;
    }
}
