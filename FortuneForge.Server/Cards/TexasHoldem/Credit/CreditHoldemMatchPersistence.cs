using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal static class CreditHoldemMatchPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CreditHoldemMatch Deserialize(string json)
    {
        var document = JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("A stored Hold'em match must be an object.");
        if (document["players"] is not JsonArray players)
            throw new JsonException("A stored Hold'em match must include its players.");

        foreach (var node in players)
        {
            if (node is not JsonObject player)
                throw new JsonException("A stored Hold'em player must be an object.");

            // Pre-centralization tables stored isBot instead. Translate only an
            // explicit Boolean: guessing would risk charging or paying a bot as
            // an account holder. Keep this compatibility at the persistence edge.
            var hasCurrentRole = TryBoolean(player["isAccountBacked"], out var accountBacked);
            var hasLegacyRole = TryBoolean(player["isBot"], out var bot);
            if (player.ContainsKey("isAccountBacked"))
            {
                if (!hasCurrentRole || player.ContainsKey("isBot") && (!hasLegacyRole || accountBacked == bot))
                    throw new JsonException("A stored Hold'em player's account role is invalid.");
            }
            else
            {
                if (!hasLegacyRole)
                    throw new JsonException("A stored Hold'em player is missing its account role.");
                accountBacked = !bot;
                player["isAccountBacked"] = accountBacked;
            }

            if (!accountBacked && player["botSkillLevel"] is JsonValue skillValue &&
                skillValue.TryGetValue<int>(out var skill))
            {
                if (!player.ContainsKey("hostMetadata")) player["hostMetadata"] = new JsonObject();
                if (player["hostMetadata"] is not JsonObject metadata)
                    throw new JsonException("A stored Hold'em player's private metadata is invalid.");
                if (!metadata.ContainsKey("managed-skill"))
                    metadata["managed-skill"] = skill.ToString(CultureInfo.InvariantCulture);
            }

            player.Remove("isBot");
            player.Remove("botSkillLevel");
        }

        return document.Deserialize<CreditHoldemMatch>(JsonOptions)
            ?? throw new JsonException("A stored Hold'em match is invalid.");
    }

    private static bool TryBoolean(JsonNode? node, out bool value)
    {
        value = false;
        return node is JsonValue json && json.TryGetValue(out value);
    }
}
