using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Shipping.Ghn;

public sealed class GhnShippingWebhookProcessor : IGhnShippingWebhookProcessor
{
    private readonly ApplicationDbContext _context;
    private readonly IShippingWebhookParser _parser;
    private readonly GhnShippingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GhnShippingWebhookProcessor> _logger;

    public GhnShippingWebhookProcessor(
        ApplicationDbContext context,
        IShippingWebhookParser parser,
        IOptions<GhnShippingOptions> options,
        TimeProvider timeProvider,
        ILogger<GhnShippingWebhookProcessor> logger)
    {
        _context = context;
        _parser = parser;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WebhookProcessResult> ProcessAsync(
        string rawPayload,
        string? suppliedSecret,
        CancellationToken cancellationToken)
    {
        if (!ValidateSecret(suppliedSecret))
        {
            return new WebhookProcessResult(
                false,
                false,
                false,
                "Webhook secret không hợp lệ.");
        }

        GhnWebhookEvent webhook;
        try
        {
            webhook = _parser.Parse(rawPayload);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            _logger.LogWarning(exception, "GHN webhook payload is invalid.");
            return new WebhookProcessResult(
                false,
                false,
                false,
                "Webhook payload không hợp lệ.");
        }

        var tracker = new CommerceFlowTracker(
            "OutboundShipmentWebhook",
            nameof(Shipment),
            webhook.OrderCode,
            "ApplyGhnWebhook",
            correlationId: webhook.DeduplicationKey,
            idempotencyKey: webhook.DeduplicationKey)
            .AddMetadata("ProviderStatus", webhook.ProviderStatus)
            .AddMetadata("ProviderOccurredAt", webhook.OccurredAt)
            .AddMetadata("ExternalEventId", webhook.ExternalEventId);

        tracker.MoveTo(CommerceFlowStage.WriteInbox);
        var inboxResult = await StoreInboxAsync(webhook, cancellationToken);
        if (inboxResult.Duplicate)
        {
            return new WebhookProcessResult(
                true,
                true,
                false,
                "Webhook đã được ghi nhận trước đó.");
        }

        var inboxId = inboxResult.InboxId;

        try
        {
            return await CommerceFlowTransaction.ExecuteAsync(
                _context,
                tracker,
                async token =>
                {
                    tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                    var inbox = await _context.IntegrationInboxEvents
                        .SingleAsync(item => item.Id == inboxId, token);

                    var shipment = await _context.Shipments
                        .Include(item => item.Order)
                            .ThenInclude(order => order.PaymentTransactions)
                        .Include(item => item.Order)
                            .ThenInclude(order => order.StatusHistory)
                        .Include(item => item.Order)
                            .ThenInclude(order => order.CancellationRequests)
                        .Include(item => item.ReturnRequest)
                            .ThenInclude(returnRequest => returnRequest!.Order)
                                .ThenInclude(order => order.StatusHistory)
                        .SingleOrDefaultAsync(
                            item => item.Provider == "GHN"
                                && (item.ExternalOrderCode == webhook.OrderCode
                                    || item.TrackingCode == webhook.OrderCode),
                            token);

                    if (shipment is null)
                    {
                        inbox.Status = IntegrationEventStatus.Failed;
                        inbox.LastError = "SHIPMENT_NOT_FOUND: Không tìm thấy shipment theo OrderCode GHN.";
                        inbox.ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime;

                        tracker.MoveTo(CommerceFlowStage.SaveChanges);
                        await _context.SaveChangesAsync(token);
                        return new WebhookProcessResult(
                            false,
                            false,
                            false,
                            inbox.LastError);
                    }

                    tracker.AddMetadata("ShipmentId", shipment.Id)
                        .AddMetadata("ShipmentDirection", shipment.Direction)
                        .AddMetadata("CurrentShipmentStatus", shipment.Status);

                    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                    var detail = new ShippingDetail(
                        webhook.OrderCode,
                        webhook.ProviderStatus,
                        GhnShipmentStatusMapper.Map(webhook.ProviderStatus),
                        webhook.TotalFee ?? shipment.Fee,
                        webhook.CodAmount ?? shipment.CodAmount,
                        webhook.WeightGram ?? shipment.WeightGram,
                        webhook.LengthCm ?? shipment.LengthCm,
                        webhook.WidthCm ?? shipment.WidthCm,
                        webhook.HeightCm ?? shipment.HeightCm,
                        shipment.EstimatedDeliveryAt,
                        webhook.OccurredAt,
                        shipment.ShipperName,
                        shipment.ShipperPhone,
                        webhook.Warehouse,
                        webhook.Reason,
                        webhook.RawPayload);

                    ShipmentTransitionDecision decision;
                    try
                    {
                        if (shipment.Direction == ShipmentDirection.Return)
                        {
                            var returnRequest = shipment.ReturnRequest
                                ?? throw new BusinessRuleViolationException(
                                    "RETURN_REQUEST_NOT_FOUND_FOR_SHIPMENT",
                                    "Return shipment không liên kết tới return request.",
                                    tracker.Snapshot());

                            decision = ReturnShipmentAggregateUpdater.ApplyProviderUpdate(
                                returnRequest,
                                shipment,
                                detail,
                                "GHN Webhook",
                                nowUtc,
                                tracker);
                        }
                        else
                        {
                            decision = OutboundShipmentAggregateUpdater.ApplyProviderUpdate(
                                shipment.Order,
                                shipment,
                                detail,
                                "GHN Webhook",
                                nowUtc,
                                tracker);
                        }
                    }
                    catch (ProviderEventOrderException exception)
                    {
                        inbox.Status = IntegrationEventStatus.Ignored;
                        inbox.LastError = Truncate(exception.Message, 2000);
                        inbox.ProcessedAt = nowUtc;

                        tracker.MoveTo(CommerceFlowStage.SaveChanges);
                        await _context.SaveChangesAsync(token);
                        return new WebhookProcessResult(
                            true,
                            false,
                            true,
                            exception.Message);
                    }

                    if (!decision.Apply)
                    {
                        inbox.Status = IntegrationEventStatus.Ignored;
                        inbox.LastError = decision.IgnoreReason;
                        inbox.ProcessedAt = nowUtc;

                        tracker.MoveTo(CommerceFlowStage.SaveChanges);
                        await _context.SaveChangesAsync(token);
                        return new WebhookProcessResult(
                            true,
                            decision.Duplicate,
                            true,
                            decision.IgnoreReason ?? "Provider event bị bỏ qua.");
                    }

                    inbox.Status = IntegrationEventStatus.Processed;
                    inbox.ProcessedAt = nowUtc;
                    inbox.LastError = null;

                    tracker.MoveTo(CommerceFlowStage.SaveChanges);
                    await _context.SaveChangesAsync(token);
                    return new WebhookProcessResult(
                        true,
                        false,
                        false,
                        shipment.Direction == ShipmentDirection.Return
                            ? "Webhook GHN đã được áp dụng vào return shipment lifecycle."
                            : "Webhook GHN đã được áp dụng vào outbound shipment lifecycle.");
                },
                cancellationToken);
        }
        catch (CommerceFlowException exception)
        {
            await MarkInboxFailedAsync(inboxId, exception, cancellationToken);

            _logger.LogError(
                exception,
                "GHN webhook flow failed. ErrorCode={ErrorCode}; Stage={Stage}; CorrelationId={CorrelationId}",
                exception.ErrorCode,
                exception.FlowContext.Stage,
                exception.FlowContext.CorrelationId);

            return new WebhookProcessResult(
                false,
                false,
                false,
                exception.Message);
        }
    }

    private async Task<InboxStoreResult> StoreInboxAsync(
        GhnWebhookEvent webhook,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var inbox = new IntegrationInboxEvent
        {
            Provider = "GHN",
            EventType = webhook.EventType,
            ExternalEventId = webhook.ExternalEventId,
            DeduplicationKey = webhook.DeduplicationKey,
            Status = IntegrationEventStatus.Received,
            Payload = webhook.RawPayload,
            ProviderOccurredAt = webhook.OccurredAt,
            ReceivedAt = nowUtc,
            AttemptCount = 1,
            CorrelationId = Truncate(webhook.OrderCode, 64),
            CreatedAt = nowUtc
        };

        _context.IntegrationInboxEvents.Add(inbox);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new InboxStoreResult(inbox.Id, Duplicate: false);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            var existingId = await _context.IntegrationInboxEvents
                .AsNoTracking()
                .Where(item => item.Provider == "GHN"
                    && item.DeduplicationKey == webhook.DeduplicationKey)
                .Select(item => (long?)item.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (existingId.HasValue)
            {
                return new InboxStoreResult(existingId.Value, Duplicate: true);
            }

            throw;
        }
    }

    private async Task MarkInboxFailedAsync(
        long inboxId,
        CommerceFlowException exception,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        var inbox = await _context.IntegrationInboxEvents
            .SingleOrDefaultAsync(item => item.Id == inboxId, cancellationToken);
        if (inbox is null)
        {
            return;
        }

        inbox.Status = IntegrationEventStatus.Failed;
        inbox.LastError = Truncate(exception.Message, 2000);
        inbox.ProcessedAt = _timeProvider.GetUtcNow().UtcDateTime;
        inbox.AttemptCount++;
        inbox.UpdatedAt = inbox.ProcessedAt;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private bool ValidateSecret(string? suppliedSecret)
    {
        if (string.IsNullOrWhiteSpace(suppliedSecret)
            || string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(_options.WebhookSecret.Trim());
        var actual = Encoding.UTF8.GetBytes(suppliedSecret.Trim());
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record InboxStoreResult(long InboxId, bool Duplicate);
}
