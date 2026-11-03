using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public async Task<SlotStateResponse> GetSlotStateAsync(
        string userId,
        string gameId,
        decimal pointValueInCents,
        CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, gameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, gameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, gameId);
        var isMidnight = gameId == SlotSpecialRoundProfiles.DragonHoardGameId;
        var isGoldFoundry = gameId == GoldFoundrySlotDefinitions.GameId;
        var isNeon = gameId == SlotSpecialRoundProfiles.NeonNightsGameId;
        if (isMidnight || isGoldFoundry || isNeon)
            return await database.RunTransactionAsync(async transaction =>
            {
                if (isGoldFoundry)
                {
                    var foundryState = await transaction.GetAllSnapshotsAsync([
                        guardReference, BalanceDocument(userId, energyCurrencyId)], cancellationToken);
                    return GoldFoundryCrucible.CreateState(ReadLong(foundryState[0], "crucibleWageredCents"),
                        ReadLong(foundryState[1], "available"));
                }
                var state = await transaction.GetAllSnapshotsAsync([
                    guardReference, BalanceDocument(userId, GameCurrencyId(FreeGamesCurrencyId, gameId)),
                    BalanceDocument(userId, specialPointsCurrencyId), BalanceDocument(userId, energyCurrencyId)], cancellationToken);
                if (isNeon)
                {
                    var remaining = checked((int)ReadLong(state[1], "available"));
                    var wager = remaining > 0 ? ReadNeonCarryWagerPoints(state[0], pointValueInCents) : (long?)null;
                    var mode = remaining > 0 ? ReadString(state[0], "freeSpinFeatureMode") : null;
                    return new SlotStateResponse(remaining, wager, 0, 0, [], mode);
                }
                var rounds = ReadMidnightBonusRounds(state[0], ReadLong(state[1], "available"), pointValueInCents);
                var active = rounds.FirstOrDefault();
                return new SlotStateResponse(active?.RemainingSpins ?? 0, active?.WagerPoints,
                    checked((int)ReadLong(state[2], "available")), ReadLong(state[3], "available"),
                    CreateSealCollections(state[0], pointValueInCents, gameId), active?.FeatureMode)
                { BonusRound = active, QueuedBonusRounds = rounds.Skip(1).ToArray() };
            }, cancellationToken: cancellationToken);
        var snapshots = await Task.WhenAll(
            guardReference.GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, specialPointsCurrencyId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, energyCurrencyId).GetSnapshotAsync(cancellationToken));
        var guardSnapshot = snapshots[0];
        return new SlotStateResponse(
            0,
            null,
            checked((int)ReadLong(snapshots[1], "available")),
            gameId == SlotSpecialRoundProfiles.RainbowRealmGameId ? 0 : ReadLong(snapshots[2], "available"),
            CreateSealCollections(guardSnapshot, pointValueInCents, gameId),
            null);
    }

    public async Task<IReadOnlyList<SlotSpinHistoryItem>> GetSlotSpinHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshots = await database
            .Collection("slotSpinResults")
            .WhereEqualTo("userId", userId)
            .GetSnapshotAsync(cancellationToken);

        return snapshots.Documents
            .Select(snapshot => new SlotSpinHistoryItem(
                snapshot.GetValue<string>("spinId"),
                snapshot.GetValue<string>("gameId"),
                ReadDecimal(snapshot, "wageredSlotsCredits"),
                ReadDecimal(snapshot, "wonSlotsCredits"),
                ReadDecimal(snapshot, "netSlotsCredits"),
                snapshot.GetValue<string>("result"),
                snapshot.GetValue<Timestamp>("createdAt").ToDateTime()))
            .OrderByDescending(spin => spin.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
    }

    private static long ReadNeonCarryWagerPoints(DocumentSnapshot guard, decimal pointValueInCents)
    {
        var cents = ReadLong(guard, "freeSpinWagerCents");
        if (cents <= 0)
        {
            // Point-only guards predate the migration from whole-rand units.
            cents = checked(ReadLong(guard, "freeSpinWagerPoints") * RandMoney.CentsPerRand);
        }
        var wager = RandMoney.CentsToPoints(cents, pointValueInCents);
        if (!NeonArcadeSlotDefinitions.IsValidCarryWager(wager))
            throw new InvalidOperationException("The saved Neon bonus wager is invalid.");
        return wager;
    }

    private static IReadOnlyList<MidnightVaultBonusRound> ReadMidnightBonusRounds(
        DocumentSnapshot guard, long available, decimal pointValueInCents)
    {
        if (available < 0) throw new InvalidOperationException("The Midnight Vault bonus balance is invalid.");
        IReadOnlyList<MidnightVaultBonusRound> rounds;
        if (guard.Exists && guard.ContainsField("midnightBonusRounds"))
            rounds = ReadMidnightRoundsField(guard, "midnightBonusRounds");
        else if (available == 0) rounds = [];
        else
        {
            var cents = ReadLong(guard, "freeSpinWagerCents");
            if (cents <= 0) cents = checked(ReadLong(guard, "freeSpinWagerPoints") * RandMoney.CentsPerRand);
            var wager = RandMoney.CentsToPoints(cents, pointValueInCents);
            MidnightVaultBonusPolicy.ValidateDerivedWager(wager);
            if (RandMoney.PointsToCents(wager, pointValueInCents) != cents)
                throw new InvalidOperationException("The saved Midnight Vault bonus wager is invalid.");
            rounds = [new(guard.Id + ":legacy", checked((int)available), checked((int)available), wager,
                ReadString(guard, "freeSpinFeatureMode"),
                MidnightVaultBonusPolicy.ResolveModel(ReadString(guard, "freeSpinMathModelId")))];
        }
        MidnightVaultBonusRounds.Validate(rounds);
        if (rounds.Sum(round => (long)round.RemainingSpins) != available)
            throw new InvalidOperationException("The Midnight Vault round queue differs from its bonus balance.");
        return rounds;
    }

    private static IReadOnlyList<MidnightVaultBonusRound> ReadMidnightRoundsField(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.Exists || !snapshot.ContainsField(field))
            throw new InvalidOperationException("The saved Midnight Vault round queue is missing.");
        var rounds = snapshot.GetValue<List<Dictionary<string, object>>>(field).Select(ReadMidnightRound).ToArray();
        MidnightVaultBonusRounds.Validate(rounds);
        return rounds;
    }

    private static MidnightVaultBonusRound ReadMidnightRound(Dictionary<string, object> data)
    {
        if (data.GetValueOrDefault("id") is not string id || data.GetValueOrDefault("mathModelId") is not string model ||
            string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("The saved Midnight Vault round is invalid.");
        if (data.TryGetValue("featureMode", out var rawMode) && rawMode is not null && rawMode is not string)
            throw new InvalidOperationException("The saved Midnight Vault round mode is invalid.");
        return new(id, checked((int)ReadMidnightInteger(data, "totalSpins")),
            checked((int)ReadMidnightInteger(data, "remainingSpins")), ReadMidnightInteger(data, "wagerPoints"),
            data.GetValueOrDefault("featureMode") is string mode && mode.Length > 0 ? mode : null, model);
    }

    private static long ReadMidnightInteger(Dictionary<string, object> data, string field) =>
        data.TryGetValue(field, out var value) && value is long integer
            ? integer
            : throw new InvalidOperationException("The saved Midnight Vault round requires integer " + field + ".");

    private static Dictionary<string, object> MidnightRoundData(MidnightVaultBonusRound round) => new()
    {
        ["id"] = round.Id, ["totalSpins"] = (long)round.TotalSpins, ["remainingSpins"] = (long)round.RemainingSpins,
        ["wagerPoints"] = round.WagerPoints, ["featureMode"] = round.FeatureMode ?? string.Empty,
        ["mathModelId"] = round.MathModelId
    };

    private static Dictionary<string, object>[] MidnightRoundsData(IReadOnlyList<MidnightVaultBonusRound> rounds) =>
        rounds.Select(MidnightRoundData).ToArray();

    private static Dictionary<string, object> MidnightBonusGuardData(
        IReadOnlyList<MidnightVaultBonusRound> rounds, decimal pointValueInCents)
    {
        var active = rounds.FirstOrDefault();
        return new()
        {
            ["midnightBonusRounds"] = MidnightRoundsData(rounds),
            ["freeSpinWagerPoints"] = active?.WagerPoints ?? 0,
            ["freeSpinWagerCents"] = active is null ? 0 : RandMoney.PointsToCents(active.WagerPoints, pointValueInCents),
            ["freeSpinFeatureMode"] = active?.FeatureMode ?? string.Empty,
            ["freeSpinMathModelId"] = active?.MathModelId ?? string.Empty
        };
    }
}
