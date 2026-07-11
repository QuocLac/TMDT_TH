using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebApplication2.Services.Shipping.Ghn;

public sealed class GhnShippingWebhookParser : IShippingWebhookParser
{
    public GhnWebhookEvent Parse(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new FormatException("Webhook payload rỗng.");
        }

        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;

        var orderCode = ReadRequired(root, "OrderCode");
        var status = ReadRequired(root, "Status").ToLowerInvariant();
        var type = ReadOptional(root, "Type") ?? "Switch_status";
        var time = ReadDateTime(root, "Time");
        var externalEventId = string.Join(
            "|",
            orderCode,
            status,
            time?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            type);

        var rawPayloadHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload)))
            .ToLowerInvariant();
        var stableSource = string.Join(
            "\u001f",
            externalEventId,
            ReadOptional(root, "TotalFee") ?? string.Empty,
            ReadOptional(root, "CODAmount") ?? string.Empty,
            ReadOptional(root, "ReasonCode") ?? string.Empty,
            ReadOptional(root, "Warehouse") ?? string.Empty,
            rawPayloadHash);
        var deduplicationKey = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(stableSource)))
            .ToLowerInvariant();

        return new GhnWebhookEvent(
            deduplicationKey,
            externalEventId.Length <= 150 ? externalEventId : externalEventId[..150],
            orderCode,
            status,
            type,
            time,
            ReadDecimal(root, "TotalFee"),
            ReadDecimal(root, "CODAmount"),
            ReadInt(root, "Weight"),
            ReadInt(root, "Length"),
            ReadInt(root, "Width"),
            ReadInt(root, "Height"),
            ReadOptional(root, "Warehouse"),
            ReadOptional(root, "Reason") ?? ReadOptional(root, "ReasonCode"),
            rawPayload);
    }

    private static string ReadRequired(JsonElement root, string name)
    {
        var value = ReadOptional(root, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new FormatException($"Webhook thiếu trường {name}.")
            : value;
    }

    private static string? ReadOptional(JsonElement root, string name)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? ReadInt(JsonElement root, string name) =>
        int.TryParse(
            ReadOptional(root, name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static decimal? ReadDecimal(JsonElement root, string name) =>
        decimal.TryParse(
            ReadOptional(root, name),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static DateTime? ReadDateTime(JsonElement root, string name) =>
        DateTimeOffset.TryParse(
            ReadOptional(root, name),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var value)
            ? value.UtcDateTime
            : null;

    private static bool TryGetPropertyIgnoreCase(
        JsonElement root,
        string name,
        out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
