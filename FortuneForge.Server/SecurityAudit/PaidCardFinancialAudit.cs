using System.Collections;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.AndarBahar;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.DragonTiger;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.HouseHoldem;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Checks deterministic paid-card wager and return ledgers against a settled round receipt.</summary>
internal static class PaidCardFinancialAudit
{
    private sealed record ExpectedLedger(string Id, string Type, long Cents);
    private sealed record LinkedLedger(string Id, string Type, long Cents, string Key, Timestamp CreatedAt,
        long? BalanceAfterCents = null, string? RoundId = null);

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(
        FirestoreDb database, RoundAuditSummary summary, string payloadJson, CancellationToken ct)
    {
        if (PaidCardEvidenceReplayAudit.Supports(summary) && summary.GameId == "hi-lo")
            return await HiLoAsync(database, summary, payloadJson, ct);
        if (PaidCardEvidenceReplayAudit.Supports(summary) && summary.GameId == "house-holdem")
            return await HouseHoldemAsync(database, summary, payloadJson, ct);
        if (summary.GameId is not ("casino-war" or "video-poker" or "baccarat" or
            "dragon-tiger" or "andar-bahar")) return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var wager = root.GetProperty("wagerCents").GetInt64();
            var payout = root.GetProperty("payoutCents").GetInt64();
            if (wager < 0 || payout < 0 || summary.Participants.Count != 1 ||
                summary.Participants[0].WagerCents != wager || summary.Participants[0].PayoutCents != payout)
                return [new("paid-card-financial-receipt-mismatch", "error",
                    "The saved financial receipt differs from its audit summary.")];
            var expected = Ledgers(summary.GameId, summary.RoundId, root.GetProperty("data"), wager, payout);
            var collection = database.Collection("balanceTransactions");
            var reads = await Task.WhenAll(expected.Select(item =>
                collection.Document(item.Id).GetSnapshotAsync(ct)));
            var findings = new List<RoundAuditFinding>();
            for (var index = 0; index < expected.Count; index++)
            {
                var item = expected[index];
                var read = reads[index];
                if (!read.Exists && item.Cents != 0)
                    findings.Add(new("paid-card-ledger-missing", "error",
                        $"Expected wallet ledger {item.Id} is absent."));
                else if (read.Exists && (item.Cents == 0 ||
                    Text(read, "transactionId") != item.Id ||
                    Text(read, "userId") != summary.Participants[0].ActorId ||
                    Text(read, "currencyId") != "slotsCredits" ||
                    Text(read, "type") != item.Type ||
                    LedgerCents(read) != item.Cents))
                    findings.Add(new("paid-card-ledger-mismatch", "error",
                        $"Wallet ledger {item.Id} differs from the settled round."));
            }
            var prefix = $"{summary.GameId}-{summary.RoundId}-";
            var allLedgers = (await collection.OrderBy(FieldPath.DocumentId).StartAt(prefix)
                .EndAt(prefix + "\uf8ff").Limit(100).GetSnapshotAsync(ct)).Documents;
            var expectedIds = expected.Where(item => item.Cents != 0)
                .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            if (allLedgers.Any(item => !expectedIds.Contains(item.Id)))
                findings.Add(new("paid-card-ledger-extra", "error",
                    "A round-linked wallet ledger has no accepted wager or payout."));
            if (allLedgers.Count != expectedIds.Count)
                findings.Add(new("paid-card-ledger-set-mismatch", "error",
                    "The round-linked wallet ledger set differs from the settled wager and return."));
            if (PaidCardEvidenceReplayAudit.Supports(summary))
                findings.AddRange(await InspectSourceAsync(database, summary, root, wager, payout, ct));
            return findings;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            ArgumentException or OverflowException or InvalidCastException or FormatException)
        {
            return [new("paid-card-financial-evidence-invalid", "error",
                "The saved financial receipt cannot be reconciled with wallet ledgers.")];
        }
    }

    private static IReadOnlyList<ExpectedLedger> Ledgers(string game, string id, JsonElement data,
        long wager, long payout) => game switch
    {
        "video-poker" =>
        [
            new($"video-poker-{id}-wager", "video-poker-wager", -wager),
            new($"video-poker-{id}-payout", "video-poker-payout", payout)
        ],
        "baccarat" =>
        [
            new($"baccarat-{id}-wager", "baccarat-wager", -wager),
            new($"baccarat-{id}-payout", "baccarat-payout", payout)
        ],
        "dragon-tiger" =>
        [
            new($"dragon-tiger-{id}-wager", "dragon-tiger-wager", -wager),
            new($"dragon-tiger-{id}-payout", "dragon-tiger-payout", payout)
        ],
        "andar-bahar" =>
        [
            new($"andar-bahar-{id}-wager", "andar-bahar-wager", -wager),
            new($"andar-bahar-{id}-payout", "andar-bahar-payout", payout)
        ],
        "casino-war" => CasinoWar(id, data, wager, payout),
        _ => []
    };

    private static IReadOnlyList<ExpectedLedger> CasinoWar(string id, JsonElement data, long wager, long payout)
    {
        var round = data.GetProperty("round");
        var primary = Cents(round.GetProperty("primaryStake").GetDecimal());
        var tie = data.GetProperty("tieSettlement");
        var tieStake = tie.ValueKind == JsonValueKind.Null ? 0 : Cents(tie.GetProperty("stake").GetDecimal());
        var tieReturn = tie.ValueKind == JsonValueKind.Null ? 0 : Cents(tie.GetProperty("totalReturn").GetDecimal());
        var decision = round.GetProperty("tieDecision");
        var decided = decision.ValueKind != JsonValueKind.Null;
        var extra = decided && decision.GetInt32() == 1 ? primary : 0;
        var primaryReturn = Cents(round.GetProperty("settlement").GetProperty("totalReturn").GetDecimal());
        if (wager != checked(primary + tieStake + extra) || payout != checked(tieReturn + primaryReturn))
            throw new InvalidOperationException("Casino War settlement amounts do not balance.");
        return
        [
            new($"casino-war-{id}-opening-wager", "casino-war-opening-wager", -checked(primary + tieStake)),
            new($"casino-war-{id}-tie-payout", "casino-war-tie-payout", tieReturn),
            new($"casino-war-{id}-opening-primary-payout", "casino-war-primary-payout", decided ? 0 : primaryReturn),
            new($"casino-war-{id}-war-wager", "casino-war-war-wager", -extra),
            new($"casino-war-{id}-decision-primary-payout", "casino-war-primary-payout", decided ? primaryReturn : 0)
        ];
    }

    private static async Task<IReadOnlyList<RoundAuditFinding>> InspectSourceAsync(
        FirestoreDb database, RoundAuditSummary summary, JsonElement root, long wager, long payout,
        CancellationToken ct)
    {
        var collection = summary.GameId switch
        {
            "casino-war" => "casinoWarRounds", "video-poker" => "videoPokerRounds",
            "baccarat" => "baccaratRounds", "dragon-tiger" => "dragonTigerRounds",
            "andar-bahar" => "andarBaharRounds", _ => throw new InvalidOperationException("Unknown paid card source.")
        };
        if (root.GetProperty("sourceCollection").GetString() != collection ||
            root.GetProperty("sourceDocumentId").GetString() != summary.RoundId)
            return [Failure("paid-card-source-round-mismatch", "The immutable receipt points to a different source round.")];
        var source = await database.Collection(collection).Document(summary.RoundId).GetSnapshotAsync(ct);
        if (!source.Exists || Text(source, "roundId") != summary.RoundId ||
            Text(source, "userId") != summary.Participants[0].ActorId ||
            !source.TryGetValue<Timestamp>("createdAt", out var createdAt) ||
            createdAt.ToDateTime() != summary.StartedAtUtc)
            return [Failure("paid-card-source-round-mismatch", "The source round is missing or has different identity or timing.")];
        var data = root.GetProperty("data");
        var matches = summary.GameId switch
        {
            "casino-war" => CasinoWarSource(source, summary, data, wager, payout),
            "video-poker" => VideoPokerSource(source, summary, data, wager, payout),
            "baccarat" => BaccaratSource(source, summary, data, wager, payout),
            "dragon-tiger" => DragonTigerSource(source, summary, data, wager, payout),
            "andar-bahar" => AndarBaharSource(source, summary, data, wager, payout),
            _ => false
        };
        if (!matches)
            return [Failure("paid-card-source-round-mismatch", "The mutable source round differs from the private settled evidence.")];
        if (summary.GameId != "andar-bahar") return [];
        var result = await database.Collection("cardGameResults")
            .Document($"andar-bahar-{summary.RoundId}").GetSnapshotAsync(ct);
        var savedRound = data.GetProperty("round");
        var winner = savedRound.GetProperty("winner").Deserialize<AndarBaharSide>(RoundAuditCapture.Json);
        var side = data.GetProperty("side").Deserialize<AndarBaharSide>(RoundAuditCapture.Json);
        var outcome = side == winner ? "win" : "loss";
        return SourceResultMatches(result, summary, summary.Participants[0].ActorId, wager, payout,
                   outcome, true) && Text(result, "side") == side.ToString().ToLowerInvariant() &&
               Text(result, "winner") == winner.ToString().ToLowerInvariant()
            ? [] : [Failure("paid-card-source-result-mismatch", "The Andar Bahar shared result differs from the private settlement.")];
    }

    private static bool CasinoWarSource(DocumentSnapshot source, RoundAuditSummary summary,
        JsonElement data, long wager, long payout)
    {
        var round = data.GetProperty("round");
        var tie = data.GetProperty("tieSettlement");
        var primary = Cents(round.GetProperty("primaryStake").GetDecimal());
        var tieStake = tie.ValueKind == JsonValueKind.Null ? 0 : Cents(tie.GetProperty("stake").GetDecimal());
        var cards = CardCodes(data.GetProperty("deck"));
        var decision = round.GetProperty("tieDecision");
        var openingKey = Text(source, "startIdempotencyKey");
        if (Number(source, "schemaVersion") != 1 || Number(source, "primaryStakeCents") != primary ||
            Number(source, "tieStakeCents") != tieStake || Text(source, "phase") != "completed" ||
            !Strings(source, "deck").SequenceEqual(cards) ||
            CasinoWarFirestoreStore.Hash($"{summary.Participants[0].ActorId}\n{openingKey}") != summary.RoundId)
            return false;
        if (decision.ValueKind == JsonValueKind.Null)
            return !source.ContainsField("decision") && wager == primary + tieStake;
        var selected = decision.Deserialize<CasinoWarTieDecision>(RoundAuditCapture.Json);
        var settlement = round.GetProperty("settlement");
        var outcome = settlement.GetProperty("outcome")
            .Deserialize<CasinoWarRoundOutcome>(RoundAuditCapture.Json);
        return Text(source, "decision") == CasinoWarService.DecisionName(selected) &&
            !string.IsNullOrWhiteSpace(Text(source, "decisionIdempotencyKey")) &&
            Number(source, "primaryReturnCents") == Cents(settlement.GetProperty("totalReturn").GetDecimal()) &&
            Text(source, "primaryOutcome") == CasinoWarService.RoundOutcomeName(outcome) &&
            source.TryGetValue<Timestamp>("updatedAt", out var updatedAt) &&
            updatedAt.ToDateTime() == summary.CompletedAtUtc;
    }

    private static bool VideoPokerSource(DocumentSnapshot source, RoundAuditSummary summary,
        JsonElement data, long wager, long payout)
    {
        var initial = CardCodes(data.GetProperty("initialDeal").GetProperty("cards"));
        var positions = data.GetProperty("heldCardPositions").GetProperty("positions")
            .EnumerateArray().Select(item => item.GetInt64()).ToArray();
        var held = positions.ToHashSet();
        var remaining = data.GetProperty("remainingDecks").EnumerateArray().ToArray();
        var draws = data.GetProperty("draws").EnumerateArray().ToArray();
        if (remaining.Length != draws.Length) return false;
        var decks = Enumerable.Range(0, remaining.Length).SelectMany(hand => initial
            .Concat(CardCodes(draws[hand].GetProperty("finalHand").GetProperty("cards"))
                .Where((_, position) => !held.Contains(position)))
            .Concat(CardCodes(remaining[hand]))).ToArray();
        var results = data.GetProperty("results").EnumerateArray().ToArray();
        var ranks = results.Select(result => VideoPokerService.HandRankName(
            result.GetProperty("handRank").Deserialize<VideoPokerHandRank>(RoundAuditCapture.Json))).ToArray();
        var payouts = results.Select(result => checked((long)result.GetProperty("paytableOutcome")
            .GetProperty("creditsWon").GetInt32() * VideoPokerMoney.CoinValueCents)).ToArray();
        var openingKey = Text(source, "startIdempotencyKey");
        return Number(source, "schemaVersion") == 2 && Text(source, "status") == "completed" &&
            Number(source, "coinsWagered") == data.GetProperty("coinsWagered").GetInt32() &&
            Number(source, "handCount") == data.GetProperty("handCount").GetInt32() &&
            Number(source, "payoutCents") == payout &&
            !string.IsNullOrWhiteSpace(Text(source, "drawIdempotencyKey")) &&
            VideoPokerFirestoreStore.CreateLookupKey($"{summary.Participants[0].ActorId}\n{openingKey}") == summary.RoundId &&
            Strings(source, "decks").SequenceEqual(decks) &&
            Longs(source, "heldPositions").SequenceEqual(positions) &&
            Text(source, "handRank") == ranks.FirstOrDefault() &&
            Strings(source, "handRanks").SequenceEqual(ranks) &&
            Longs(source, "handPayoutCents").SequenceEqual(payouts) &&
            source.TryGetValue<Timestamp>("updatedAt", out var updatedAt) &&
            updatedAt.ToDateTime() == summary.CompletedAtUtc && wager > 0;
    }

    private static bool BaccaratSource(DocumentSnapshot source, RoundAuditSummary summary,
        JsonElement data, long wager, long payout)
    {
        var round = data.GetProperty("round");
        var settlement = data.GetProperty("settlement");
        var playerPair = data.GetProperty("playerPairSettlement");
        var bankerPair = data.GetProperty("bankerPairSettlement");
        var side = data.GetProperty("betSide").Deserialize<BaccaratBetSide>(RoundAuditCapture.Json);
        var outcome = round.GetProperty("outcome").Deserialize<BaccaratRoundOutcome>(RoundAuditCapture.Json);
        var openingKey = Text(source, "startIdempotencyKey");
        if (Number(source, "schemaVersion") is not (1 or 2) ||
            BaccaratFirestoreStore.CreateLookupKey($"{summary.Participants[0].ActorId}\n{openingKey}") != summary.RoundId ||
            Text(source, "betSide") != BaccaratService.BetSideName(side) ||
            Number(source, "stakeCents") != Cents(settlement.GetProperty("stake").GetDecimal()) ||
            !Strings(source, "consumedCards").SequenceEqual(CardCodes(round.GetProperty("consumedCards"))) ||
            Text(source, "outcome") != BaccaratService.OutcomeName(outcome) ||
            !source.TryGetValue<bool>("endedOnNatural", out var natural) ||
            natural != round.GetProperty("endedOnNatural").GetBoolean() ||
            Text(source, "disposition") != BaccaratService.DispositionName(
                settlement.GetProperty("disposition").Deserialize<BaccaratBetDisposition>(RoundAuditCapture.Json)) ||
            Number(source, "profitCents") != Cents(settlement.GetProperty("profit").GetDecimal()) ||
            Number(source, "totalReturnCents") != Cents(settlement.GetProperty("totalReturn").GetDecimal()) ||
            Number(source, "shoeCardsUsed") != data.GetProperty("shoeCardsUsed").GetInt32() ||
            Number(source, "shoeCardsRemaining") != data.GetProperty("shoeCardsRemaining").GetInt32())
            return false;
        if (Number(source, "schemaVersion") == 1)
            return wager == Cents(settlement.GetProperty("stake").GetDecimal()) &&
                payout == Cents(settlement.GetProperty("totalReturn").GetDecimal());
        return Number(source, "playerPairStakeCents") == Cents(playerPair.GetProperty("stake").GetDecimal()) &&
            Number(source, "bankerPairStakeCents") == Cents(bankerPair.GetProperty("stake").GetDecimal()) &&
            Number(source, "playerPairTotalReturnCents") == Cents(playerPair.GetProperty("totalReturn").GetDecimal()) &&
            Number(source, "bankerPairTotalReturnCents") == Cents(bankerPair.GetProperty("totalReturn").GetDecimal()) &&
            Number(source, "totalStakeCents") == wager &&
            Number(source, "combinedTotalReturnCents") == payout;
    }

    private static bool DragonTigerSource(DocumentSnapshot source, RoundAuditSummary summary,
        JsonElement data, long wager, long payout)
    {
        var stored = data.Deserialize<DragonTigerStoredRound>(RoundAuditCapture.Json)
            ?? throw new JsonException("Missing Dragon Tiger round.");
        var openingKey = Text(source, "startIdempotencyKey");
        return Number(source, "schemaVersion") == 1 &&
            DragonTigerService.LookupKey(summary.Participants[0].ActorId, openingKey) == summary.RoundId &&
            Number(source, "dragonStakeCents") == stored.Wagers.DragonCents &&
            Number(source, "tigerStakeCents") == stored.Wagers.TigerCents &&
            Number(source, "tieStakeCents") == stored.Wagers.TieCents &&
            Number(source, "totalStakeCents") == wager &&
            Text(source, "dragonCard") == CardCode.Format(stored.Round.DragonCard) &&
            Text(source, "tigerCard") == CardCode.Format(stored.Round.TigerCard) &&
            Text(source, "outcome") == stored.Round.Outcome.ToString().ToLowerInvariant() &&
            Number(source, "totalReturnCents") == payout &&
            Number(source, "profitCents") == checked(payout - wager) &&
            Number(source, "shoeCardsUsed") == stored.ShoeCardsUsed &&
            Number(source, "shoeCardsRemaining") == stored.ShoeCardsRemaining;
    }

    private static bool AndarBaharSource(DocumentSnapshot source, RoundAuditSummary summary,
        JsonElement data, long wager, long payout)
    {
        if (Number(source, "schemaVersion") != 2) return false;
        using var record = JsonDocument.Parse(Text(source, "recordJson"));
        var saved = record.RootElement;
        var round = data.GetProperty("round");
        var prefix = new[] { round.GetProperty("joker").Deserialize<PlayingCard>(RoundAuditCapture.Json) }
            .Concat(round.GetProperty("deals").EnumerateArray().Select(entry =>
                entry.GetProperty("card").Deserialize<PlayingCard>(RoundAuditCapture.Json)))
            .Select(CardCode.Format).ToArray();
        var side = data.GetProperty("side").Deserialize<AndarBaharSide>(RoundAuditCapture.Json);
        var winner = round.GetProperty("winner").Deserialize<AndarBaharSide>(RoundAuditCapture.Json);
        return saved.GetProperty("SchemaVersion").GetInt32() == 1 &&
            saved.GetProperty("RoundId").GetString() == summary.RoundId &&
            saved.GetProperty("UserId").GetString() == summary.Participants[0].ActorId &&
            AndarBaharService.LookupKey(summary.Participants[0].ActorId,
                saved.GetProperty("IdempotencyKey").GetString() ?? "") == summary.RoundId &&
            saved.GetProperty("IdempotencyKey").GetString() == data.GetProperty("idempotencyKey").GetString() &&
            saved.GetProperty("Side").GetString() == side.ToString().ToLowerInvariant() &&
            saved.GetProperty("Winner").GetString() == winner.ToString().ToLowerInvariant() &&
            saved.GetProperty("StakeCents").GetInt64() == wager &&
            saved.GetProperty("TotalReturnCents").GetInt64() == payout &&
            saved.GetProperty("ProfitCents").GetInt64() == checked(payout - wager) &&
            saved.GetProperty("RevealedCards").EnumerateArray().Select(item => item.GetString())
                .SequenceEqual(prefix) &&
            saved.GetProperty("BalanceAfterWagerCents").GetInt64() ==
                data.GetProperty("balanceAfterWagerCents").GetInt64() &&
            saved.GetProperty("BalanceAfterSettlementCents").GetInt64() ==
                data.GetProperty("balanceAfterSettlementCents").GetInt64();
    }

    private static string[] CardCodes(JsonElement cards) =>
        (cards.Deserialize<PlayingCard[]>(RoundAuditCapture.Json) ?? throw new JsonException("Missing card list."))
        .Select(CardCode.Format).ToArray();

    private static async Task<IReadOnlyList<RoundAuditFinding>> HiLoAsync(
        FirestoreDb database, RoundAuditSummary summary, string payloadJson, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var user = SingleActor(summary, root);
            if (root.GetProperty("sourceCollection").GetString() != "cardGameResults" ||
                root.GetProperty("sourceDocumentId").GetString() != $"hi-lo-{summary.RoundId}")
                return [Failure("paid-card-source-result-mismatch", "The Hi-Lo receipt points to a different source result.")];
            var saved = root.GetProperty("data");
            var round = saved.Deserialize<HiLoRound>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing Hi-Lo round.");
            var wager = root.GetProperty("wagerCents").GetInt64();
            var payout = root.GetProperty("payoutCents").GetInt64();
            if (round.Id.ToString("N") != summary.RoundId || round.History.IsDefaultOrEmpty ||
                round.Status == HiLoRoundStatus.Active || HiLoService.Cents(round.Wager) != wager ||
                HiLoService.Cents(round.Payout) != payout || round.History.Length > 153)
                return [Failure("paid-card-financial-receipt-mismatch", "The Hi-Lo financial receipt is invalid.")];

            var states = new List<HiLoRound>();
            var choices = new List<string> { "start" };
            var replay = HiLoEngine.Start(round.Id, round.Wager, round.History[0].Card);
            states.Add(replay);
            foreach (var entry in round.History.Skip(1))
            {
                if (entry.Kind == HiLoHistoryKind.Skip)
                {
                    choices.Add("skip");
                    replay = HiLoEngine.Skip(replay, entry.Card);
                }
                else if (entry.Kind is HiLoHistoryKind.Win or HiLoHistoryKind.Loss &&
                    entry.Direction is { } direction)
                {
                    choices.Add(direction == HiLoDirection.Higher ? "higher" : "lower");
                    replay = HiLoEngine.Guess(replay, direction, entry.Card);
                }
                else throw new InvalidOperationException("Invalid Hi-Lo history action.");
                states.Add(replay);
            }
            if (round.Status == HiLoRoundStatus.CashedOut)
            {
                choices.Add("cashout");
                replay = HiLoEngine.CashOut(replay);
                states.Add(replay);
            }
            if (!JsonElement.DeepEquals(saved, JsonSerializer.SerializeToElement(replay, RoundAuditCapture.Json)) ||
                states.Count != round.Version + 1)
                return [Failure("paid-card-financial-receipt-mismatch", "The Hi-Lo history does not explain the final wager and return.")];

            var result = await database.Collection("cardGameResults").Document($"hi-lo-{summary.RoundId}")
                .GetSnapshotAsync(ct);
            if (!SourceResultMatches(result, summary, user, wager, payout, HiLoStatus(round.Status)))
                return [Failure("paid-card-source-result-mismatch", "The Hi-Lo source result differs from its final receipt.")];
            var events = (await database.Collection("hiLoEvents").WhereEqualTo("roundId", summary.RoundId)
                .Limit(200).GetSnapshotAsync(ct)).Documents;
            if (events.Count != states.Count || events.Select(item => Number(item, "revision")).Distinct().Count() != states.Count)
                return [Failure("paid-card-event-mismatch", "The Hi-Lo accepted operation events do not cover the saved history.")];
            var ordered = events.OrderBy(item => Number(item, "revision")).ToArray();
            var linked = new List<LinkedLedger>(ordered.Length);
            var operationIds = new string[ordered.Length];
            var hashes = new string[ordered.Length];
            var previousTime = Timestamp.FromDateTime(summary.StartedAtUtc);
            for (var revision = 0; revision < ordered.Length; revision++)
            {
                var entry = ordered[revision];
                var operationId = Text(entry, "operationId");
                var hash = HiLoService.Hash($"{user}\n{operationId}");
                if (Number(entry, "revision") != revision || Text(entry, "roundId") != summary.RoundId ||
                    Text(entry, "userId") != user || Number(entry, "schemaVersion") != 1 ||
                    Text(entry, "choice") != choices[revision] || entry.Id != hash ||
                    operationId.Length is < 16 or > 128 || !entry.TryGetValue<Timestamp>("createdAt", out var createdAt) ||
                    createdAt < previousTime || createdAt.ToDateTime() > summary.CompletedAtUtc)
                    return [Failure("paid-card-event-mismatch", "A Hi-Lo operation event differs from the accepted action history.")];
                previousTime = createdAt;
                operationIds[revision] = operationId;
                hashes[revision] = hash;
            }
            if (ordered[0].GetValue<Timestamp>("createdAt").ToDateTime() != summary.StartedAtUtc ||
                ordered[^1].GetValue<Timestamp>("createdAt").ToDateTime() != summary.CompletedAtUtc)
                return [Failure("paid-card-event-mismatch", "The Hi-Lo operation timestamps differ from the settled receipt.")];
            var operations = await Task.WhenAll(hashes.Select(hash => database.Collection("hiLoOperations")
                .Document(hash).GetSnapshotAsync(ct)));
            for (var revision = 0; revision < ordered.Length; revision++)
            {
                var eventEntry = ordered[revision];
                var operation = operations[revision];
                var state = states[revision];
                var command = revision == 0
                    ? new HiLoCommand(operationIds[revision], "start", WagerCents: wager)
                    : new HiLoCommand(operationIds[revision], "action", RoundId: summary.RoundId,
                        ExpectedRevision: revision - 1, Choice: choices[revision]);
                var fingerprint = HiLoService.Fingerprint(command);
                if (!operation.Exists || operation.Id != hashes[revision] ||
                    Text(operation, "userId") != user || Text(operation, "operationId") != operationIds[revision] ||
                    Number(operation, "schemaVersion") != 1 ||
                    Text(operation, "fingerprint") != fingerprint || Text(eventEntry, "fingerprint") != fingerprint ||
                    !operation.TryGetValue<Timestamp>("createdAt", out var operationTime) ||
                    operationTime != eventEntry.GetValue<Timestamp>("createdAt"))
                    return [Failure("paid-card-operation-mismatch", "A Hi-Lo operation record is absent or differs from its accepted event.")];
                var response = JsonSerializer.Deserialize<HiLoSessionResponse>(Text(operation, "responseJson"));
                if (response?.Round is not { } responseRound || responseRound.RoundId != summary.RoundId ||
                    responseRound.Revision != revision || responseRound.WagerCents != wager ||
                    responseRound.CurrentReturnCents != HiLoService.Cents(state.CurrentReturn) ||
                    responseRound.Status != HiLoStatus(state.Status) ||
                    responseRound.History.Count != state.History.Length)
                    return [Failure("paid-card-operation-mismatch", "A Hi-Lo operation response differs from the saved action state.")];
                var delta = revision == 0 ? -wager : state.Status is HiLoRoundStatus.CashedOut or
                    HiLoRoundStatus.AutoCashedOut ? HiLoService.Cents(state.Payout) : 0L;
                if (Number(operation, "walletDeltaCents") != delta)
                    return [Failure("paid-card-operation-mismatch", "A Hi-Lo operation wallet delta differs from its accepted outcome.")];
                linked.Add(new LinkedLedger($"hi-lo-{hashes[revision]}", delta < 0 ? "hi-lo-wager" :
                    delta > 0 ? "hi-lo-payout" : "", delta, operationIds[revision],
                    eventEntry.GetValue<Timestamp>("createdAt"), response.WalletBalanceCents, summary.RoundId));
            }
            return await CheckLinkedLedgersAsync(database, summary, linked, true, ct);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            ArgumentException or OverflowException or InvalidCastException or FormatException)
        {
            return [Failure("paid-card-financial-evidence-invalid", "The saved Hi-Lo operations cannot be reconciled with the wallet ledger.")];
        }
    }

    private static string HiLoStatus(HiLoRoundStatus status) => status switch
    {
        HiLoRoundStatus.Active => "active", HiLoRoundStatus.Lost => "lost",
        HiLoRoundStatus.CashedOut => "cashedOut", HiLoRoundStatus.AutoCashedOut => "maximumReturn",
        _ => throw new InvalidOperationException("Unknown Hi-Lo round status.")
    };

    private static async Task<IReadOnlyList<RoundAuditFinding>> HouseHoldemAsync(
        FirestoreDb database, RoundAuditSummary summary, string payloadJson, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var user = SingleActor(summary, root);
            if (root.GetProperty("sourceCollection").GetString() != "houseHoldemRounds" ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId)
                return [Failure("paid-card-source-round-mismatch", "The House Hold'em receipt points to a different source round.")];
            var data = root.GetProperty("data");
            var round = data.GetProperty("round").Deserialize<HouseHoldemRound>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing House Hold'em round.");
            var steps = data.GetProperty("steps").Deserialize<string[]>(RoundAuditCapture.Json)
                ?? throw new JsonException("Missing House Hold'em actions.");
            var wager = root.GetProperty("wagerCents").GetInt64();
            var payout = root.GetProperty("payoutCents").GetInt64();
            if (round.Settlement is null || round.Phase != HouseHoldemPhases.Settled ||
                round.TotalWagerCents != wager || round.PayoutCents != payout ||
                steps.Length is < 1 or > 5)
                return [Failure("paid-card-financial-receipt-mismatch", "The House Hold'em financial receipt is invalid.")];

            var source = await database.Collection("houseHoldemRounds").Document(summary.RoundId)
                .GetSnapshotAsync(ct);
            var result = await database.Collection("cardGameResults").Document($"house-holdem-{summary.RoundId}")
                .GetSnapshotAsync(ct);
            if (!SourceResultMatches(result, summary, user, wager, payout, round.Settlement.Outcome))
                return [Failure("paid-card-source-result-mismatch", "The House Hold'em result differs from its final receipt.")];
            if (!source.Exists || Text(source, "roundId") != summary.RoundId || Text(source, "userId") != user ||
                Number(source, "schemaVersion") != 1 || Number(source, "anteCents") != round.AnteCents ||
                Number(source, "tripsCents") != round.TripsCents ||
                Number(source, "totalWagerCents") != wager || Number(source, "payoutCents") != payout ||
                Text(source, "phase") != HouseHoldemPhases.Settled ||
                !Strings(source, "steps").SequenceEqual(steps, StringComparer.Ordinal))
                return [Failure("paid-card-source-round-mismatch", "The House Hold'em source round differs from its final receipt.")];
            var deck = Strings(source, "deck");
            if (deck.Length != 52 || !new[] { deck[0], deck[2] }.SequenceEqual(round.PlayerCards) ||
                !new[] { deck[1], deck[3] }.SequenceEqual(round.DealerCards) ||
                !deck.Skip(4).Take(5).SequenceEqual(round.CommunityCards))
                return [Failure("paid-card-source-round-mismatch", "The House Hold'em source deck differs from the saved dealt cards.")];
            var startKey = Text(source, "startIdempotencyKey");
            if (startKey.Length == 0 ||
                HouseHoldemFirestoreStore.LookupKey($"{user}\n{startKey}") != summary.RoundId)
                return [Failure("paid-card-source-round-mismatch", "The House Hold'em source round has no valid opening operation key.")];

            var events = (await database.Collection("houseHoldemRoundEvents")
                .WhereEqualTo("roundId", summary.RoundId).Limit(100).GetSnapshotAsync(ct)).Documents;
            if (events.Count != steps.Length + 1 || events.Any(entry =>
                    Text(entry, "roundId") != summary.RoundId || Text(entry, "userId") != user ||
                    Number(entry, "schemaVersion") != 1 ||
                    entry.Id != $"{summary.RoundId}-{HouseHoldemFirestoreStore.LookupKey(Text(entry, "idempotencyKey"))}" ||
                    !entry.TryGetValue<Timestamp>("createdAt", out _)))
                return [Failure("paid-card-event-mismatch", "House Hold'em accepted command events do not cover the saved actions.")];
            var opening = events.Where(entry => Text(entry, "command") == "start").ToArray();
            var actionEvents = events.Where(entry => Text(entry, "command") != "start").ToArray();
            if (opening.Length != 1 || Text(opening[0], "idempotencyKey") != startKey ||
                opening[0].GetValue<Timestamp>("createdAt").ToDateTime() != summary.StartedAtUtc ||
                !actionEvents.GroupBy(entry => Text(entry, "command")).ToDictionary(group => group.Key, group => group.Count())
                    .OrderBy(item => item.Key).SequenceEqual(steps.GroupBy(step => step)
                        .ToDictionary(group => group.Key, group => group.Count()).OrderBy(item => item.Key)))
                return [Failure("paid-card-event-mismatch", "House Hold'em accepted commands differ from the saved action list.")];
            if (actionEvents.Any(entry => entry.GetValue<Timestamp>("createdAt").ToDateTime() < summary.StartedAtUtc ||
                    entry.GetValue<Timestamp>("createdAt").ToDateTime() > summary.CompletedAtUtc) ||
                !actionEvents.Any(entry => Text(entry, "command") == steps[^1] &&
                    entry.GetValue<Timestamp>("createdAt").ToDateTime() == summary.CompletedAtUtc))
                return [Failure("paid-card-event-mismatch", "House Hold'em action timestamps differ from the settled receipt.")];

            var initial = checked(round.AnteCents + round.BlindCents + round.TripsCents);
            var expected = new List<LinkedLedger>
            {
                new($"house-holdem-{summary.RoundId}-initial-wager", "house-holdem-wager",
                    -initial, startKey, opening[0].GetValue<Timestamp>("createdAt"))
            };
            var playAction = steps.SingleOrDefault(step => step is HouseHoldemActions.Play1X or
                HouseHoldemActions.Play2X or HouseHoldemActions.Play3X or HouseHoldemActions.Play4X);
            if (playAction is not null)
            {
                var playEvents = actionEvents.Where(entry => Text(entry, "command") == playAction).ToArray();
                if (playEvents.Length != 1 || round.PlayWagerCents <= 0)
                    return [Failure("paid-card-event-mismatch", "The House Hold'em Play wager has no unique accepted action.")];
                var play = playEvents[0];
                var key = Text(play, "idempotencyKey");
                expected.Add(new LinkedLedger(
                    $"house-holdem-{summary.RoundId}-{HouseHoldemFirestoreStore.LookupKey(key)}-wager",
                    "house-holdem-play-wager", -round.PlayWagerCents, key,
                    play.GetValue<Timestamp>("createdAt")));
            }
            else if (round.PlayWagerCents != 0)
                return [Failure("paid-card-financial-receipt-mismatch", "The House Hold'em Play wager lacks a saved action.")];

            var payoutId = $"house-holdem-{summary.RoundId}-payout";
            var payoutLedger = await database.Collection("balanceTransactions").Document(payoutId).GetSnapshotAsync(ct);
            var payoutKey = payoutLedger.Exists ? Text(payoutLedger, "idempotencyKey") : "";
            var settlementKey = payoutKey.EndsWith("-payout", StringComparison.Ordinal)
                ? payoutKey[..^"-payout".Length] : "";
            var settlementEvents = actionEvents.Where(entry =>
                Text(entry, "idempotencyKey") == settlementKey &&
                Text(entry, "command") == steps[^1] &&
                entry.GetValue<Timestamp>("createdAt").ToDateTime() == summary.CompletedAtUtc).ToArray();
            if (payout > 0 && payoutLedger.Exists &&
                (settlementEvents.Length != 1 || settlementKey.Length == 0))
                return [Failure("paid-card-ledger-mismatch", "The House Hold'em payout is not linked to the settling action.")];
            expected.Add(new LinkedLedger(payoutId, "house-holdem-payout", payout, payoutKey,
                Timestamp.FromDateTime(summary.CompletedAtUtc)));
            return await CheckLinkedLedgersAsync(database, summary, expected, false, ct);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            ArgumentException or OverflowException or InvalidCastException or FormatException)
        {
            return [Failure("paid-card-financial-evidence-invalid", "The saved House Hold'em commands cannot be reconciled with the wallet ledger.")];
        }
    }

    private static async Task<IReadOnlyList<RoundAuditFinding>> CheckLinkedLedgersAsync(
        FirestoreDb database, RoundAuditSummary summary, IReadOnlyList<LinkedLedger> expected,
        bool queryByRoundId, CancellationToken ct)
    {
        var collection = database.Collection("balanceTransactions");
        var reads = await Task.WhenAll(expected.Select(item => collection.Document(item.Id).GetSnapshotAsync(ct)));
        var findings = new List<RoundAuditFinding>();
        for (var index = 0; index < expected.Count; index++)
        {
            var item = expected[index];
            var read = reads[index];
            if (item.Cents == 0)
            {
                if (read.Exists)
                    findings.Add(Failure("paid-card-ledger-extra", $"Wallet ledger {item.Id} exists for a zero-credit action."));
            }
            else if (!read.Exists)
                findings.Add(Failure("paid-card-ledger-missing", $"Expected wallet ledger {item.Id} is absent."));
            else if (!LinkedLedgerMatches(read, summary.Participants[0].ActorId, item))
                findings.Add(Failure("paid-card-ledger-mismatch", $"Wallet ledger {item.Id} differs from the accepted action."));
        }
        Query query;
        if (queryByRoundId)
            query = collection.WhereEqualTo("roundId", summary.RoundId).Limit(200);
        else
        {
            var prefix = $"house-holdem-{summary.RoundId}-";
            query = collection.OrderBy(FieldPath.DocumentId).StartAt(prefix).EndAt(prefix + "\uf8ff").Limit(100);
        }
        var actual = (await query.GetSnapshotAsync(ct)).Documents;
        var expectedIds = expected.Where(item => item.Cents != 0).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        if (actual.Any(item => !expectedIds.Contains(item.Id)))
            findings.Add(Failure("paid-card-ledger-extra", "A round-linked wallet ledger has no accepted wager or payout action."));
        if (expectedIds.Count != expected.Count(item => item.Cents != 0) ||
            actual.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != expectedIds.Count)
            findings.Add(Failure("paid-card-ledger-set-mismatch", "The round-linked wallet ledger set differs from accepted actions."));
        return findings;
    }

    private static bool LinkedLedgerMatches(DocumentSnapshot read, string user, LinkedLedger item) =>
        Text(read, "transactionId") == item.Id && Text(read, "userId") == user &&
        Text(read, "currencyId") == "slotsCredits" && Text(read, "type") == item.Type &&
        Text(read, "idempotencyKey") == item.Key && LedgerCents(read) == item.Cents &&
        read.TryGetValue<Timestamp>("createdAt", out var createdAt) && createdAt == item.CreatedAt &&
        MoneyCents(read, "balanceAfter") is not null &&
        (item.RoundId is null || Text(read, "roundId") == item.RoundId) &&
        (item.BalanceAfterCents is null ||
            (Number(read, "amountCents") == item.Cents &&
             Number(read, "balanceAfterCents") == item.BalanceAfterCents &&
             MoneyCents(read, "balanceAfter") == item.BalanceAfterCents));

    private static bool SourceResultMatches(DocumentSnapshot result, RoundAuditSummary summary,
        string user, long wager, long payout, string outcome, bool microsecondTolerance = false) =>
        result.Exists && Text(result, "resultId") == result.Id &&
        result.Id == $"{summary.GameId}-{summary.RoundId}" &&
        Text(result, "game") == summary.GameId && Text(result, "mode") == "credit" &&
        Text(result, "userId") == user && Text(result, "matchId") == summary.RoundId &&
        Number(result, "wagerCents") == wager && Number(result, "payoutCents") == payout &&
        Number(result, "netCents") == checked(payout - wager) &&
        Text(result, "outcome") == outcome && Text(result, "settlementStatus") == "paid" &&
        Text(result, "claimStatus") == "completed" && Number(result, "schemaVersion") == 1 &&
        result.TryGetValue<Timestamp>("startedAt", out var startedAt) &&
        SameTime(startedAt.ToDateTime(), summary.StartedAtUtc, microsecondTolerance) &&
        result.TryGetValue<Timestamp>("completedAt", out var completedAt) &&
        SameTime(completedAt.ToDateTime(), summary.CompletedAtUtc, microsecondTolerance);

    private static bool SameTime(DateTime stored, DateTime expected, bool microsecondTolerance) =>
        Math.Abs((stored - expected).Ticks) <= (microsecondTolerance ? 10 : 0);

    private static string SingleActor(RoundAuditSummary summary, JsonElement root)
    {
        var wager = root.GetProperty("wagerCents").GetInt64();
        var payout = root.GetProperty("payoutCents").GetInt64();
        if (wager < 0 || payout < 0 || root.GetProperty("netCents").GetInt64() != checked(payout - wager) ||
            summary.Participants.Count != 1 || !summary.Participants[0].IsAccountBacked ||
            summary.Participants[0].WagerCents != wager || summary.Participants[0].PayoutCents != payout ||
            summary.Participants[0].NetCents != checked(payout - wager))
            throw new InvalidOperationException("The financial receipt differs from its participant summary.");
        return summary.Participants[0].ActorId;
    }

    private static string[] Strings(DocumentSnapshot document, string field)
    {
        if (!document.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable items)
            throw new InvalidOperationException($"The source round is missing {field}.");
        return items.Cast<object?>().Select(item => item as string ??
            throw new InvalidOperationException($"The source round {field} is invalid.")).ToArray();
    }

    private static long[] Longs(DocumentSnapshot document, string field)
    {
        if (!document.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable items)
            throw new InvalidOperationException($"The source round is missing {field}.");
        return items.Cast<object?>().Select(item => item is long number ? number :
            throw new InvalidOperationException($"The source round {field} is invalid.")).ToArray();
    }

    private static long Number(DocumentSnapshot document, string field) =>
        document.TryGetValue<long>(field, out var value) ? value : long.MinValue;

    private static RoundAuditFinding Failure(string code, string message) => new(code, "error", message);

    private static long Cents(decimal amount) => checked((long)(amount * 100m));

    private static long? LedgerCents(DocumentSnapshot document)
    {
        var cents = MoneyCents(document, "amount");
        return document.TryGetValue<long>("amountCents", out var recorded) && recorded != cents
            ? null : cents;
    }

    private static long? MoneyCents(DocumentSnapshot document, string field)
    {
        if (!document.TryGetValue<double>(field, out var amount) || !double.IsFinite(amount)) return null;
        var scaled = amount * 100d;
        var rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
        if (Math.Abs(scaled - rounded) > 0.000001d) return null;
        return checked((long)rounded);
    }

    private static string Text(DocumentSnapshot document, string field) =>
        document.TryGetValue<string>(field, out var value) ? value : "";
}
