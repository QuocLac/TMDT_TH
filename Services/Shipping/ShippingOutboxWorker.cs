using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Services.Shipping;

public sealed class ShippingOutboxWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<GhnShippingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShippingOutboxWorker> _logger;

    public ShippingOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<GhnShippingOptions> options,
        TimeProvider timeProvider,
        ILogger<ShippingOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (!options.Enabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            try
            {
                for (var index = 0; index < options.WorkerBatchSize; index++)
                {
                    if (!await ProcessOneAsync(options, stoppingToken))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "GHN outbox worker iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.WorkerPollSeconds), stoppingToken);
        }
    }

    private async Task<bool> ProcessOneAsync(
        GhnShippingOptions options,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var gateway = scope.ServiceProvider.GetRequiredService<IShippingGateway>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var staleLockBeforeUtc = nowUtc.AddMinutes(-options.WorkerLockMinutes);
        var message = await context.IntegrationOutboxMessages
            .Where(item =>
                item.Provider == "GHN"
                && item.AggregateType == nameof(Shipment)
                && (item.MessageType == ShippingExecutionService.CreateMessageType
                    || item.MessageType == ShippingExecutionService.CancelMessageType
                    || item.MessageType == "CancelShipmentRequested")
                && (
                    (item.Status == IntegrationOutboxStatus.Pending
                        && (item.NextAttemptAt == null || item.NextAttemptAt <= nowUtc))
                    || (item.Status == IntegrationOutboxStatus.Failed
                        && item.NextAttemptAt != null
                        && item.NextAttemptAt <= nowUtc)
                    || (item.Status == IntegrationOutboxStatus.Processing
                        && item.LockedAt != null
                        && item.LockedAt <= staleLockBeforeUtc))
                && item.AttemptCount < options.WorkerMaxAttempts)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (message is null)
        {
            return false;
        }

        message.Status = IntegrationOutboxStatus.Processing;
        message.LockedAt = nowUtc;
        message.AttemptCount++;
        message.UpdatedAt = nowUtc;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return true;
        }

        try
        {
            if (message.MessageType == ShippingExecutionService.CreateMessageType)
            {
                await ProcessCreateAsync(
                    context,
                    gateway,
                    options,
                    message,
                    cancellationToken);
            }
            else
            {
                await ProcessCancelAsync(
                    context,
                    gateway,
                    message,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderOperationException exception)
        {
            await MarkFailedAsync(
                context,
                message,
                exception.Message,
                exception.Retryable,
                cancellationToken);

            _logger.LogWarning(
                exception,
                "GHN outbox message {MessageId} failed at {Stage} on attempt {AttemptCount}. CorrelationId={CorrelationId}",
                message.Id,
                exception.FlowContext.Stage,
                message.AttemptCount,
                exception.FlowContext.CorrelationId);
        }
        catch (CommerceFlowException exception)
        {
            await MarkFailedAsync(
                context,
                message,
                exception.Message,
                retryable: exception is CommerceConcurrencyException or CommerceTransactionException,
                cancellationToken);

            _logger.LogError(
                exception,
                "Shipping flow {FlowName} failed at {Stage}. ErrorCode={ErrorCode}; Aggregate={AggregateType}:{AggregateId}; CorrelationId={CorrelationId}",
                exception.FlowContext.FlowName,
                exception.FlowContext.Stage,
                exception.ErrorCode,
                exception.FlowContext.AggregateType,
                exception.FlowContext.AggregateId,
                exception.FlowContext.CorrelationId);
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(
                context,
                message,
                exception.Message,
                retryable: true,
                cancellationToken);
            _logger.LogError(exception, "GHN outbox message {MessageId} crashed.", message.Id);
        }

        return true;
    }

    private async Task ProcessCreateAsync(
        ApplicationDbContext context,
        IShippingGateway gateway,
        GhnShippingOptions options,
        IntegrationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<GhnCreateShipmentMessage>(
            message.Payload,
            "SHIPMENT_CREATE_PAYLOAD_INVALID",
            message);

        var snapshot = await context.Shipments
            .AsNoTracking()
            .Include(item => item.Order)
                .ThenInclude(order => order.Items)
            .SingleOrDefaultAsync(item => item.Id == payload.ShipmentId, cancellationToken);

        var providerTracker = CreateTracker(
            "OutboundShipmentProviderCreate",
            payload.ShipmentId,
            "CreateShipmentOnProvider",
            message);
        providerTracker.MoveTo(CommerceFlowStage.LoadAggregate);

        if (snapshot is null)
        {
            throw new BusinessRuleViolationException(
                "SHIPMENT_NOT_FOUND",
                "Shipment không còn tồn tại khi worker xử lý tạo vận đơn.",
                providerTracker.Snapshot());
        }

        if (snapshot.Direction != ShipmentDirection.Outbound)
        {
            throw new BusinessRuleViolationException(
                "OUTBOUND_FLOW_REJECTED_RETURN_SHIPMENT",
                "Worker chiều đi từ chối shipment chiều về.",
                providerTracker.Snapshot());
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ExternalOrderCode))
        {
            await CompleteAlreadyAppliedAsync(context, message, cancellationToken);
            return;
        }

        if (snapshot.Status != ShipmentStatus.PendingCreation)
        {
            throw new InvalidStateTransitionException(
                "SHIPMENT_CREATE_REQUIRES_PENDING_CREATION",
                $"Worker chỉ được tạo GHN khi shipment ở PendingCreation; hiện tại là {snapshot.Status}.",
                providerTracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    snapshot.Status.ToString()).Snapshot());
        }

        providerTracker.MoveTo(CommerceFlowStage.CallProvider);
        ShippingOperationResult<ShippingCreateResult> result;
        try
        {
            result = await gateway.CreateAsync(
                ShippingExecutionService.BuildCreateRequest(
                    snapshot.Order,
                    snapshot,
                    options,
                    payload.ServiceId,
                    payload.ServiceTypeId,
                    payload.Note),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProviderOperationException(
                "GHN_CREATE_CALL_FAILED",
                "Lỗi khi gọi GHN tạo vận đơn.",
                providerTracker.Snapshot(),
                retryable: true,
                innerException: exception);
        }

        if (!result.Success || result.Data is null)
        {
            throw new ProviderOperationException(
                result.ErrorCode ?? "GHN_CREATE_REJECTED",
                string.IsNullOrWhiteSpace(result.Message)
                    ? "GHN từ chối tạo vận đơn."
                    : result.Message,
                providerTracker.Snapshot(),
                result.Retryable);
        }

        var providerData = result.Data;
        var appliedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var detail = new ShippingDetail(
            providerData.ExternalOrderCode,
            "ready_to_pick",
            ShipmentStatus.Created,
            providerData.TotalFee,
            snapshot.CodAmount,
            snapshot.WeightGram,
            snapshot.LengthCm,
            snapshot.WidthCm,
            snapshot.HeightCm,
            providerData.ExpectedDeliveryAt,
            appliedAt,
            null,
            null,
            null,
            null,
            providerData.RawResponse);

        var applyTracker = CreateTracker(
            "OutboundShipmentApplyCreateResult",
            payload.ShipmentId,
            "ApplyProviderCreateResult",
            message);

        await CommerceFlowTransaction.ExecuteAsync(
            context,
            applyTracker,
            async token =>
            {
                applyTracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var shipment = await context.Shipments
                    .Include(item => item.Order)
                        .ThenInclude(order => order.PaymentTransactions)
                    .Include(item => item.Order)
                        .ThenInclude(order => order.StatusHistory)
                    .SingleOrDefaultAsync(item => item.Id == payload.ShipmentId, token)
                    ?? throw new BusinessRuleViolationException(
                        "SHIPMENT_NOT_FOUND",
                        "Shipment biến mất trước khi áp dụng kết quả tạo GHN.",
                        applyTracker.Snapshot());

                if (!string.IsNullOrWhiteSpace(shipment.ExternalOrderCode))
                {
                    await MarkCompletedAsync(context, message, token);
                    await context.SaveChangesAsync(token);
                    return true;
                }

                OutboundShipmentAggregateUpdater.ApplyProviderUpdate(
                    shipment.Order,
                    shipment,
                    detail,
                    "ShippingOutboxWorker",
                    appliedAt,
                    applyTracker);

                applyTracker.MoveTo(CommerceFlowStage.UpdateRelatedAggregate);
                shipment.Order.FulfillmentStatus = FulfillmentStatus.ReadyToShip;
                shipment.Order.UpdatedAt = appliedAt;

                applyTracker.MoveTo(CommerceFlowStage.SaveChanges);
                await MarkCompletedAsync(context, message, token);
                await context.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);
    }

    private async Task ProcessCancelAsync(
        ApplicationDbContext context,
        IShippingGateway gateway,
        IntegrationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var payload = message.MessageType == "CancelShipmentRequested"
            ? MapLegacyCancellation(Deserialize<LegacyGhnCancelShipmentMessage>(
                message.Payload,
                "LEGACY_SHIPMENT_CANCEL_PAYLOAD_INVALID",
                message))
            : Deserialize<GhnCancelShipmentMessage>(
                message.Payload,
                "SHIPMENT_CANCEL_PAYLOAD_INVALID",
                message);

        var snapshot = await context.Shipments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == payload.ShipmentId, cancellationToken);

        var providerTracker = CreateTracker(
            "OutboundShipmentProviderCancel",
            payload.ShipmentId,
            "CancelShipmentOnProvider",
            message);
        providerTracker.MoveTo(CommerceFlowStage.LoadAggregate);

        if (snapshot is null)
        {
            throw new BusinessRuleViolationException(
                "SHIPMENT_NOT_FOUND",
                "Shipment không còn tồn tại khi worker xử lý hủy vận đơn.",
                providerTracker.Snapshot());
        }

        if (string.IsNullOrWhiteSpace(payload.ExternalOrderCode))
        {
            throw new BusinessRuleViolationException(
                "SHIPMENT_CANCEL_PROVIDER_REFERENCE_REQUIRED",
                "Payload hủy vận đơn không có ExternalOrderCode.",
                providerTracker.MoveTo(CommerceFlowStage.ValidateInput).Snapshot());
        }

        if (snapshot.Status == ShipmentStatus.Cancelled)
        {
            await CompleteAlreadyAppliedAsync(context, message, cancellationToken);
            return;
        }

        if (snapshot.Status != ShipmentStatus.CancelRequested)
        {
            throw new InvalidStateTransitionException(
                "SHIPMENT_CANCEL_REQUIRES_CANCEL_REQUESTED",
                $"Worker chỉ được gọi hủy GHN khi shipment ở CancelRequested; hiện tại là {snapshot.Status}.",
                providerTracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    snapshot.Status.ToString()).Snapshot());
        }

        providerTracker.MoveTo(CommerceFlowStage.CallProvider);
        ShippingOperationResult<bool> result;
        try
        {
            result = await gateway.CancelAsync(payload.ExternalOrderCode, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProviderOperationException(
                "GHN_CANCEL_CALL_FAILED",
                "Lỗi khi gọi GHN hủy vận đơn.",
                providerTracker.Snapshot(),
                retryable: true,
                innerException: exception);
        }

        if (!result.Success || result.Data != true)
        {
            throw new ProviderOperationException(
                result.ErrorCode ?? "GHN_CANCEL_REJECTED",
                string.IsNullOrWhiteSpace(result.Message)
                    ? "GHN từ chối hủy vận đơn."
                    : result.Message,
                providerTracker.Snapshot(),
                result.Retryable);
        }

        var appliedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var detail = new ShippingDetail(
            payload.ExternalOrderCode,
            "cancel",
            ShipmentStatus.Cancelled,
            snapshot.Fee,
            snapshot.CodAmount,
            snapshot.WeightGram,
            snapshot.LengthCm,
            snapshot.WidthCm,
            snapshot.HeightCm,
            snapshot.EstimatedDeliveryAt,
            appliedAt,
            snapshot.ShipperName,
            snapshot.ShipperPhone,
            snapshot.CurrentHub,
            payload.Reason,
            "{}");

        var applyTracker = CreateTracker(
            "OutboundShipmentApplyCancelResult",
            payload.ShipmentId,
            "ApplyProviderCancelResult",
            message);

        await CommerceFlowTransaction.ExecuteAsync(
            context,
            applyTracker,
            async token =>
            {
                applyTracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var shipment = await context.Shipments
                    .Include(item => item.Order)
                        .ThenInclude(order => order.PaymentTransactions)
                    .Include(item => item.Order)
                        .ThenInclude(order => order.StatusHistory)
                    .SingleOrDefaultAsync(item => item.Id == payload.ShipmentId, token)
                    ?? throw new BusinessRuleViolationException(
                        "SHIPMENT_NOT_FOUND",
                        "Shipment biến mất trước khi áp dụng kết quả hủy GHN.",
                        applyTracker.Snapshot());

                if (shipment.Status == ShipmentStatus.Cancelled)
                {
                    await MarkCompletedAsync(context, message, token);
                    await context.SaveChangesAsync(token);
                    return true;
                }

                OutboundShipmentAggregateUpdater.ApplyProviderUpdate(
                    shipment.Order,
                    shipment,
                    detail,
                    "ShippingOutboxWorker",
                    appliedAt,
                    applyTracker);

                applyTracker.MoveTo(CommerceFlowStage.SaveChanges);
                await MarkCompletedAsync(context, message, token);
                await context.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);
    }

    private async Task CompleteAlreadyAppliedAsync(
        ApplicationDbContext context,
        IntegrationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        await MarkCompletedAsync(context, message, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private Task MarkCompletedAsync(
        ApplicationDbContext context,
        IntegrationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        message.Status = IntegrationOutboxStatus.Completed;
        message.CompletedAt = nowUtc;
        message.NextAttemptAt = null;
        message.LastError = null;
        message.LockedAt = null;
        message.UpdatedAt = nowUtc;
        return Task.CompletedTask;
    }

    private async Task MarkFailedAsync(
        ApplicationDbContext context,
        IntegrationOutboxMessage message,
        string error,
        bool retryable,
        CancellationToken cancellationToken)
    {
        // A failed provider-response transaction may leave rolled-back aggregate
        // changes tracked. Clear them before persisting only the outbox failure.
        var messageId = message.Id;
        context.ChangeTracker.Clear();
        var persistedMessage = await context.IntegrationOutboxMessages
            .SingleAsync(item => item.Id == messageId, cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        persistedMessage.Status = IntegrationOutboxStatus.Failed;
        persistedMessage.LastError = Truncate(error, 2000);
        persistedMessage.LockedAt = null;
        persistedMessage.NextAttemptAt = retryable
            ? nowUtc.Add(CalculateBackoff(persistedMessage.AttemptCount))
            : null;
        persistedMessage.UpdatedAt = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static GhnCancelShipmentMessage MapLegacyCancellation(
        LegacyGhnCancelShipmentMessage payload)
    {
        return new GhnCancelShipmentMessage(
            payload.OrderId,
            payload.ShipmentId,
            payload.ExternalOrderCode ?? string.Empty,
            string.IsNullOrWhiteSpace(payload.ReasonText)
                ? "Hủy theo cancellation request legacy."
                : payload.ReasonText);
    }

    private static T Deserialize<T>(
        string payload,
        string errorCode,
        IntegrationOutboxMessage message)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw new JsonException("Payload was null after deserialization.");
        }
        catch (JsonException exception)
        {
            var tracker = CreateTracker(
                "ShippingOutboxPayload",
                ParseAggregateId(message.AggregateId),
                "DeserializeOutboxPayload",
                message);
            tracker.MoveTo(CommerceFlowStage.ParseProviderResponse)
                .AddMetadata("MessageType", message.MessageType);

            throw new BusinessRuleViolationException(
                errorCode,
                "Payload outbox vận chuyển không hợp lệ.",
                tracker.Snapshot(),
                exception);
        }
    }

    private static CommerceFlowTracker CreateTracker(
        string flowName,
        long shipmentId,
        string action,
        IntegrationOutboxMessage message) =>
        new CommerceFlowTracker(
            flowName,
            nameof(Shipment),
            shipmentId.ToString(CultureInfo.InvariantCulture),
            action,
            correlationId: message.CorrelationId,
            idempotencyKey: message.IdempotencyKey)
            .AddMetadata("OutboxMessageId", message.Id)
            .AddMetadata("OutboxAttempt", message.AttemptCount);

    private static long ParseAggregateId(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : 0;

    private static TimeSpan CalculateBackoff(int attempt)
    {
        var seconds = Math.Min(1_800, Math.Pow(2, Math.Clamp(attempt, 1, 10)) * 5);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record LegacyGhnCancelShipmentMessage(
        int OrderId,
        long CancellationRequestId,
        long ShipmentId,
        string? ExternalOrderCode,
        string? ReasonText);
}
