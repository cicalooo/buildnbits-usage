using System.Text.Json;

namespace BuildnBits.Usage.Core.Providers.Grok;

internal static class GrokProductMap
{
    public static string? LabelFor(JsonElement product)
    {
        if (product.ValueKind == JsonValueKind.Number && product.TryGetInt32(out var id))
        {
            return id switch
            {
                2 => GrokBillingParser.BuildLabel,
                4 => GrokBillingParser.BotLabel,
                _ => null
            };
        }

        if (product.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = product.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var name = raw.Trim();
        if (name.StartsWith("PRODUCT_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["PRODUCT_".Length..];
        }

        if (name.Equals("GROK_BUILD", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("BUILD", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("2", StringComparison.OrdinalIgnoreCase))
        {
            return GrokBillingParser.BuildLabel;
        }

        if (name.Equals("GROK_BOT", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("BOT", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CHAT", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("4", StringComparison.OrdinalIgnoreCase))
        {
            return GrokBillingParser.BotLabel;
        }

        return null;
    }
}
