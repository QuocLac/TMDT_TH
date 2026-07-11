using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Services.Shipping;

public sealed class ReturnShippingOutboxWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<GhnShippingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReturnShippingOutboxWorker> _logger;

    public ReturnShippingOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<GhnShippingOptions> options,
        TimeProvider timeProvider,
        ILogger<ReturnShippingOutboxWorker> logger)
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
                _logger.LogError(exception, "Return GHN outbox worker iteration failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(options.WorkerPollSeconds),
                stoppingToken);
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
        var staleBeforeUtc = nowUtc.AddMinutes(-options.WorkerLockMinutes);

        var message = await context.IntegrationOutboxMessages
            .Where(item =>
                item.Provider == "GHN"
                && item.MessageType == ReturnShippingExecutionService.CreateMessageType
                && item.AggregateType == nameof(ReturnRequest)
                && item.AttemptCount < options.WorkerMaxAttempts
                && (
                    (item.Status == IntegrationOutboxStatus.Pending
                        && (item.NextAttemptAt == null || item.NextAttemptAt <= nowUtc))
                    || (item.Status == IntegrationOutboxStatus.Failed
                        && item.NextAttemptAt != null
                        && item.NextAttemptAt <= nowUtc)
                    || (item.Status == IntegrationOutboxStatus.Processing
                        && item.LockedAt != null
                        && item.LockedAt <= staleBeforeUtc)))
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
            await ProcessCreateAsync(
                context,
                gateway,
                options,
                message,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderOperationException exception)
        {
            await MarkFailedAsync(
                context,
                message.Id,
                exception.Message,
                exception.Retryable,
                cancellationToken);
            LogFlow(exception, message.Id);
        }
        catch (CommerceFlowException exception)
        {
            await MarkFailedAsync(
                context,
                message.Id,
                exception.Message,
                exception is CommerceConcurrencyException or CommerceTransactionException,
                cancellationToken);
            LogFlow(exception, message.Id);
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(
                context,
                message.Id,
                exception.Message,
                retryable: true,
                cancellationToken);
            _logger.LogError(
                exception,
                "Return shipment outbox message {MessageId} crashed.",
                message.Id);
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
        var tracker = new CommerceFlowTracker(
            "ReturnShipmentProviderCreate",
            nameof(ReturnRequest),
            message.AggregateId,
            "CreateReturnShipmentOnProvider",
            correlationId: message.CorrelationId,
            idempotencyKey: message.IdempotencyKey);

        tracker.MoveTo(CommerceFlowStage.ParseProviderResponse);
        GhnCreateReturnShipmentMessage payload;
        try
        {
            payload = JsonSerializer.Deserialize<GhnCreateReturnShipmentMessage>(
                message.Payload,
                JsonOptions)
                ?? throw new JsonException("Payload is null.");
        }
        catch (JsonException exception)
        {
            throw new BusinessRuleViolationException(
                "RETURN_SHIPMENT_OUTBOX_PAYLOAD_INVALID",
                "Payload tạo return shipment không hợp lệ.",
                tracker.Snapshot(),
                exception);
        }

        if (!string.Equals(
                message.AggregateId,
                payload.ReturnRequestId.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException(
                "RETURN_SHIPMENT_OUTBOX_AGGREGATE_MISMATCH",
                "AggregateId của outbox không khớp ReturnRequestId trong payload.",
                tracker.Snapshot());
        }

        var snapshot = await context.ReturnRequests
            .AsNoTracking()
            .Include(item => item.Order)
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(
                item => item.Id == payload.ReturnRequestId,
                cancellationToken);

        tracker.MoveTo(CommerceFlowStage.LoadAggregate);
        if (snapshot is null)
        {
            throw new BusinessRuleViolationException(
                "RETURN_REQUEST_NOT_FOUND",
                "Return request không còn tồn tại khi worker chạy.",
                tracker.Snapshot());
        }

        var snapshotShipment = snapshot.Shipments
            .SingleOrDefault(item => item.Id == payload.ShipmentId);
        if (snapshotShipment is null
            || snapshotShipment.Direction != ShipmentDirection.Return)
        {
            throw new BusinessRuleViolationException(
                "RETURN_SHIPMENT_NOT_FOUND",
                "Không tìm thấy return shipment tương ứng với outbox.",
                tracker.Snapshot());
        }

        if (!string.IsNullOrWhiteSpace(snapshotShipment.ExternalOrderCode))
        {
            await MarkCompletedAsync(context, message.Id, cancellationToken);
            return;
        }

        if (snapshotShipment.Status != ShipmentStatus.PendingCreation)
        {
            throw new InvalidStateTransitionException(
                "RETURN_SHIPMENT_CREATE_REQUIRES_PENDING",
                $"Return shipment phải ở PendingCreation; hiện tại {snapshotShipment.Status}.",
                tracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    snapshotShipment.Status.ToString()).Snapshot());
        }

        tracker.MoveTo(CommerceFlowStage.CallProvider);
        ShippingOperationResult<ShippingCreateResult> result;
        try
        {
            result = await gateway.CreateAsync(
                ReturnShippingExecutionService.BuildCreateRequest(
                    snapshot,
                    snapshotShipment,
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
                "GHN_RETURN_CREATE_CALL_FAILED",
                "Lỗi khi gọi GHN tạo vận đơn hoàn trả.",
                tracker.Snapshot(),
                retryable: true,
                innerException: exception);
        }

        if (!result.Success || result.Data is null)
        {
            throw new ProviderOperationException(
                result.ErrorCode ?? "GHN_RETURN_CREATE_FAILED",
                string.IsNullOrWhiteSpace(result.Message)
                    ? "GHN từ chối tạo vận đơn hoàn trả."
                    : result.Message,
                tracker.Snapshot(),
                result.Retryable);
        }

        tracker.MoveTo(CommerceFlowStage.ApplyProviderResponse);
        await CommerceFlowTransaction.ExecuteAsync(
            context,
            tracker,
            async token =>
            {
                var request = await context.ReturnRequests
                    .Include(item => item.Order)
                        .ThenInclude(order => order.StatusHistory)
                    .Include(item => item.Shipments)
                    .SingleOrDefaultAsync(
                        item => item.Id == payload.ReturnRequestId,
                        token)
                    ?? throw new BusinessRuleViolationException(
                        "RETURN_REQUEST_NOT_FOUND",
                        "Return request bị xóa trước khi áp dụng kết quả GHN.",
                        tracker.Snapshot());

                var shipment = request.Shipments
                    .SingleOrDefault(item => item.Id == payload.ShipmentId)
                    ?? throw new BusinessRuleViolationException(
                        "RETURN_SHIPMENT_NOT_FOUND",
                        "Return shipment bị xóa trước khi áp dụng kết quả GHN.",
                        tracker.Snapshot());

                if (!string.IsNullOrWhiteSpace(shipment.ExternalOrderCode))
                {
                    await CompleteMessageInsideTransactionAsync(
                        context,
                        message.Id,
                        token);
                    return true;
                }

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                shipment.ExternalOrderCode = result.Data.ExternalOrderCode;
                shipment.TrackingCode = result.Data.TrackingCode;
                shipment.Fee = result.Data.TotalFee;
                shipment.EstimatedDeliveryAt = result.Data.ExpectedDeliveryAt;
                shipment.ProviderStatus = "ready_to_pick";
                shipment.ProviderUpdatedAt = nowUtc;
                shipment.LastSyncedAt = nowUtc;
                shipment.Status = ShipmentStatus.Created;
                shipment.UpdatedAt = nowUtc;

                request.Status = ReturnRequestStatus.AwaitingPickup;
                request.UpdatedAt = nowUtc;

                request.Order.StatusHistory.Add(new OrderStatusHistory
                {
                    Category = OrderHistoryCategory.Return,
                    FromStatus = ReturnRequestStatus.AwaitingReturnShipment.ToString(),
                    ToStatus = ReturnRequestStatus.AwaitingPickup.ToString(),
                    Code = "RETURN_SHIPMENT_CREATED",
                    Title = "GHN đã tạo vận đơn hoàn trả",
                    Description =
                        $"Mã vận đơn {result.Data.ExternalOrderCode}; phí {result.Data.TotalFee:N0} ₫.",
                    ChangedBy = "ReturnShippingOutboxWorker",
                    CustomerVisible = true,
                    OccurredAt = nowUtc,
                    CorrelationId = ToCorrelationId(tracker.CorrelationId)
                });

                await CompleteMessageInsideTransactionAsync(
                    context,
                    message.Id,
                    token);

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await context.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);
    }

    private async Task MarkCompletedAsync(
        ApplicationDbContext context,
        long messageId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var message = await context.IntegrationOutboxMessages
            .SingleAsync(item => item.Id == messageId, cancellationToken);
        message.Status = IntegrationOutboxStatus.Completed;
        message.CompletedAt = _timeProvider.GetUtcNow().UtcDateTime;
        message.NextAttemptAt = null;
        message.LockedAt = null;
        message.LastError = null;
        message.UpdatedAt = message.CompletedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        ApplicationDbContext context,
        long messageId,
        string error,
        bool retryable,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var message = await context.IntegrationOutboxMessages
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        if (message is null)
        {
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        message.Status = IntegrationOutboxStatus.Failed;
        message.LastError = Truncate(error, 2000);
        message.LockedAt = null;
        message.NextAttemptAt = retryable
            ? nowUtc.Add(CalculateBackoff(message.AttemptCount))
            : null;
        message.UpdatedAt = nowUtc;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteMessageInsideTransactionAsync(
        ApplicationDbContext context,
        long messageId,
        CancellationToken cancellationToken)
    {
        var message = await context.IntegrationOutboxMessages
            .SingleAsync(item => item.Id == messageId, cancellationToken);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        message.Status = IntegrationOutboxStatus.Completed;
        message.CompletedAt = nowUtc;
        message.NextAttemptAt = null;
        message.LockedAt = null;
        message.LastError = null;
        message.UpdatedAt = nowUtc;
    }

    private void LogFlow(CommerceFlowException exception, long messageId)
    {
        _logger.LogError(
            exception,
            "Return shipping flow failed. MessageId={MessageId}; ErrorCode={ErrorCode}; Stage={Stage}; CorrelationId={CorrelationId}",
            messageId,
            exception.ErrorCode,
            exception.FlowContext.Stage,
            exception.FlowContext.CorrelationId);
    }

    private static TimeSpan CalculateBackoff(int attempt)
    {
        var seconds = Math.Min(
            1800,
            Math.Pow(2, Math.Clamp(attempt, 1, 10)) * 5);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string ToCorrelationId(string value) =>
        value.Length <= 64 ? value : value[..64];
}
