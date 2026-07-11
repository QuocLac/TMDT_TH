namespace WebApplication2.Services.Shipping.Ghn;

public sealed record GhnWebhookEvent(
    string DeduplicationKey,
    string ExternalEventId,
    string OrderCode,
    string ProviderStatus,
    string EventType,
    DateTime? OccurredAt,
    decimal? TotalFee,
    decimal? CodAmount,
    int? WeightGram,
    int? LengthCm,
    int? WidthCm,
    int? HeightCm,
    string? Warehouse,
    string? Reason,
    string RawPayload);

public interface IShippingWebhookParser
{
    GhnWebhookEvent Parse(string rawPayload);
}
