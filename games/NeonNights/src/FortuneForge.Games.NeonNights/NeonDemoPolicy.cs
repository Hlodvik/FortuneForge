namespace FortuneForge.Games.NeonNights;

public sealed record NeonDemoPlayRequest(long WagerPoints, bool UseFreeSpin, int FreeSpinsRemaining,
    long? FreeSpinWagerPoints, string? FreeSpinFeatureMode, int NeonDiscoSpinsRemaining,
    long? NeonDiscoWagerPoints, int? NeonDiscoMultiplier, long EnergyBalance);

public sealed record NeonDemoProgress(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int NeonDiscoSpinsRemaining, long? NeonDiscoWagerPoints, int NeonDiscoMultiplier)
{
    public long EnergyBalance => 0;
    public bool EnergyMultiplierApplied => false;
    public decimal PayoutMultiplier => 1m;
}

/// <summary>Admits caller-provided practice counters and progresses only server-generated outcomes.</summary>
public sealed class NeonDemoPolicy
{
    private readonly NeonDemoPlayRequest request;
    private readonly int startingDiscoMultiplier;

    public NeonDemoEngineRequest Engine { get; }
    public bool ChargesWager => NeonPlayPolicy.ChargesWager(request.UseFreeSpin, request.FreeSpinFeatureMode);

    public NeonDemoPolicy(NeonDemoPlayRequest request, long legacyEnergyCapacity)
    {
        this.request = request;
        if (request.NeonDiscoSpinsRemaining < 0 || request.NeonDiscoSpinsRemaining > NeonArcadeSlotDefinitions.DiscoPlaySpins)
            throw new ArgumentOutOfRangeException(nameof(request.NeonDiscoSpinsRemaining),
                "The demo Disco play balance is outside the allowed range.");
        if (request.FreeSpinsRemaining < 0)
            throw new ArgumentOutOfRangeException(nameof(request.FreeSpinsRemaining),
                "The demo free-spin balance is outside the allowed range.");
        if (request.EnergyBalance < 0 || request.EnergyBalance > legacyEnergyCapacity)
            throw new ArgumentOutOfRangeException(nameof(request.EnergyBalance),
                "The demo energy balance is outside the allowed range.");
        if (request.UseFreeSpin && request.FreeSpinsRemaining == 0)
            throw new ArgumentException("No demo free spins are available.", nameof(request));
        if (!request.UseFreeSpin && request.FreeSpinsRemaining > 0)
            throw new ArgumentException("Finish your awarded Neon spins before placing another bet.", nameof(request));
        if (request.UseFreeSpin && request.WagerPoints != request.FreeSpinWagerPoints)
            throw new ArgumentException("Special Plays use the wager from the triggering spin.", nameof(request));

        var disco = !request.UseFreeSpin && request.NeonDiscoSpinsRemaining > 0;
        if (disco)
            NeonPlayPolicy.ValidateDiscoWager(request.NeonDiscoSpinsRemaining, request.NeonDiscoWagerPoints, request.WagerPoints);
        startingDiscoMultiplier = request.NeonDiscoMultiplier ?? NeonDiscoCascade.InitialMultiplier;
        if (!NeonDiscoCascade.IsValidMultiplier(startingDiscoMultiplier) ||
            request.NeonDiscoSpinsRemaining == 0 && startingDiscoMultiplier != NeonDiscoCascade.InitialMultiplier)
            throw new ArgumentOutOfRangeException(nameof(request.NeonDiscoMultiplier));

        var featureMode = request.UseFreeSpin && NeonPlayPolicy.IsFeatureMode(request.FreeSpinFeatureMode)
            ? request.FreeSpinFeatureMode : null;
        Engine = new(request.WagerPoints, null, request.UseFreeSpin, featureMode, disco,
            disco ? startingDiscoMultiplier : NeonDiscoCascade.InitialMultiplier);
    }

    public NeonDemoProgress Complete(long wagerPoints, int awardedSpins, NeonArcadeEvidence? evidence,
        NeonDiscoCascadeOutcome? cascade)
    {
        var awarded = NeonPlayPolicy.AdvanceAwardedPlays(
            request.FreeSpinsRemaining - (request.UseFreeSpin ? 1 : 0), request.UseFreeSpin,
            request.FreeSpinFeatureMode, awardedSpins, wagerPoints, evidence);
        if (awarded.Remaining > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(request.FreeSpinsRemaining),
                "The next demo free-spin balance exceeds the supported range.");

        var remaining = NeonPlayPolicy.NextDiscoSpins(request.NeonDiscoSpinsRemaining, Engine.IsDiscoPlay, evidence);
        return new(checked((int)awarded.Remaining), awarded.LockedWagerPoints, awarded.FeatureMode, remaining,
            NeonPlayPolicy.NextDiscoWager(request.NeonDiscoSpinsRemaining, request.NeonDiscoWagerPoints,
                Engine.IsDiscoPlay, wagerPoints, evidence),
            NeonPlayPolicy.NextDiscoMultiplier(startingDiscoMultiplier, remaining, cascade, evidence));
    }
}
