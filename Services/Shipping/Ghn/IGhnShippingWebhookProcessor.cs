namespace WebApplication2.Services.Shipping.Ghn;

public sealed record WebhookProcessResult(
    bool Success,
    bool Duplicate,
    bool Ignored,
    string Message);

public interface IGhnShippingWebhookProcessor
{
    Task<WebhookProcessResult> ProcessAsync(
        string rawPayload,
        string? suppliedSecret,
        CancellationToken cancellationToken);
}
