using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.NeonNights.Server;

internal sealed record NeonPrimaryFeatures(NeonAwardedPlayState Native, NeonAwardedPlayState Legacy,
    NeonDiscoRoundState Disco, NeonCarRoundState Car, int Pity)
{
    public NeonAwardedPlayState Awarded => CombineAwarded(Native, Legacy);

    internal static NeonAwardedPlayState ReadCombinedAwarded(DocumentSnapshot guard, long legacyRemaining) =>
        CombineAwarded(ReadNativeAwarded(guard), ReadLegacyAwarded(guard, legacyRemaining));

    private static NeonAwardedPlayState CombineAwarded(NeonAwardedPlayState native, NeonAwardedPlayState legacy)
    {
        if (legacy.Remaining == 0) return native;
        if (native.Remaining == 0) return legacy;
        if (native.LockedWagerPoints != legacy.LockedWagerPoints || native.FeatureMode != legacy.FeatureMode)
            throw new GameWalletUnavailableException();
        return legacy with { Remaining = checked(legacy.Remaining + native.Remaining) };
    }

    public static NeonPrimaryFeatures Read(DocumentSnapshot guard, long legacyRemaining) =>
        new(ReadNativeAwarded(guard), ReadLegacyAwarded(guard, legacyRemaining),
            NeonDiscoRoundState.Read(field => NeonPrimaryRoundStore.Number(guard, field)),
            NeonCarRoundState.ReadGuard(field => NeonPrimaryRoundStore.Text(guard, field),
                field => NeonPrimaryRoundStore.Number(guard, field) ?? 0) with { HasActiveAdmission = false },
            checked((int)(NeonPrimaryRoundStore.Number(guard, "consecutiveFiveMisses") ?? 0)));

    private const string NativeCount = "neonNativeCarSpinsRemaining";
    private const string NativeWager = "neonNativeCarWagerPoints";
    private const string NativeMode = "neonNativeCarFeatureMode";

    internal static NeonAwardedPlayState ReadNativeAwarded(DocumentSnapshot guard) =>
        ReadAwarded(guard, NeonPrimaryRoundStore.Number(guard, NativeCount) ?? 0,
            NeonPrimaryRoundStore.Number(guard, NativeWager) ?? 0, NativeMode);

    private static NeonAwardedPlayState ReadLegacyAwarded(DocumentSnapshot guard, long remaining)
    {
        var wager = NeonPrimaryRoundStore.Number(guard, "freeSpinWagerCents") ?? 0;
        if (remaining > 0 && wager <= 0)
            wager = checked((NeonPrimaryRoundStore.Number(guard, "freeSpinWagerPoints") ?? 0) * 100);
        return ReadAwarded(guard, remaining, wager, "freeSpinFeatureMode");
    }

    private static NeonAwardedPlayState ReadAwarded(DocumentSnapshot guard, long remaining, long wager, string modeField)
    {
        if (remaining < 0 || remaining > int.MaxValue)
            throw new GameWalletUnavailableException();
        if (remaining == 0) return new(0, null, null);
        var mode = NeonPrimaryRoundStore.Text(guard, modeField);
        if (!NeonArcadeSlotDefinitions.IsValidCarryWager(wager) ||
            mode is not null && !NeonPlayPolicy.IsFeatureMode(mode))
            throw new GameWalletUnavailableException();
        return new(remaining, wager, mode);
    }

    internal static void WriteNativeAwarded(IDictionary<string, object> fields, NeonAwardedPlayState awarded)
    {
        fields[NativeCount] = awarded.Remaining;
        fields[NativeWager] = awarded.LockedWagerPoints ?? 0;
        fields[NativeMode] = awarded.FeatureMode ?? string.Empty;
    }

    internal void Write(IDictionary<string, object> fields)
    {
        WriteNativeAwarded(fields, Native);
        if (Legacy.Remaining == 0)
        {
            fields["freeSpinWagerCents"] = 0L;
            fields["freeSpinWagerPoints"] = 0L;
            fields["freeSpinFeatureMode"] = string.Empty;
        }
        fields["consecutiveFiveMisses"] = Pity;
        Disco.WriteGuard(fields);
        Car.WriteGuard(fields);
    }

    internal NeonPrimaryFeatures Settle(NeonPrimaryPreparedRound round, long legacyRemaining)
    {
        var outcome = round.Outcome;
        var usedNative = round.Request.UseFreeSpin && !round.SpendsLegacy;
        var nextNative = NeonPlayPolicy.AdvanceAwardedPlays(Native.Remaining - (usedNative ? 1 : 0),
            round.Request.UseFreeSpin, Awarded.FeatureMode, outcome.FreeSpinsAwarded, outcome.WagerPoints, outcome.NeonArcade);
        var nextLegacy = legacyRemaining == 0 ? new NeonAwardedPlayState(0, null, null) :
            Legacy with { Remaining = legacyRemaining };
        var next = this with { Native = nextNative, Legacy = nextLegacy, Pity = outcome.ConsecutiveFiveMisses };
        var isCarPlay = NeonCarRoundState.IsCarPlay(round.Request.UseFreeSpin, Awarded.FeatureMode);
        return next with
        {
            Disco = Disco.Settle(round.IsDisco, outcome.WagerPoints, outcome.Payout.TotalPoints,
                outcome.NeonDiscoCascade, outcome.NeonArcade),
            Car = Car.Advance(outcome.SpinId, isCarPlay ? outcome : null, next.Awarded.FeatureMode, outcome.Payout.TotalPoints)
        };
    }
}

internal sealed record NeonPrimaryPreparedRound(Guid AdmissionId, string AccountId, NeonRoundRequest Request,
    NeonPrimaryFeatures Before, NeonSpinOutcome Outcome, string OutcomeSha256, long WagerCents,
    bool SpendsLegacy, bool IsDisco, int? ChosenReel, DateTime StartedAtUtc, GameRequestObservation? Observation,
    string? ReservationSha256 = null, GameWalletSettlementReceipt? Reservation = null,
    NeonSpinOutcome? ReservedOutcome = null)
{
    public long PayoutCents => Outcome.Payout.TotalPoints;
    public long ExpectedLegacy => Before.Legacy.Remaining;
    public int OfferedBalance => checked((int)Before.Awarded.Remaining - (Request.UseFreeSpin ? 1 : 0));
}

internal sealed record NeonPrimaryStoredRound(NeonPrimaryPreparedRound Round, NeonRoundResult? Final);
