using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Services.Shipping;

public sealed class ShippingExecutionService : IShippingExecutionService
{
    public const string CreateMessageType = "ShipmentCreateRequested";
    public const string CancelMessageType = "ShipmentCancelRequested";

    private readonly ApplicationDbContext _context;
    private readonly IShippingGateway _gateway;
    private readonly GhnShippingOptions _options;
    private readonly TimeProvider _timeProvider;

    public ShippingExecutionService(
        ApplicationDbContext context,
        IShippingGateway gateway,
        IOptions<GhnShippingOptions> options,
        TimeProvider timeProvider)
    {
        _context = context;
        _gateway = gateway;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetServicesAsync(
        long shipmentId,
        CancellationToken cancellationToken)
    {
        var districtId = await _context.Shipments
            .AsNoTracking()
            .Where(item => item.Id == shipmentId && item.Direction == ShipmentDirection.Outbound)
            .Select(item => item.Order.ShippingDistrictId)
            .SingleOrDefaultAsync(cancellationToken);

        if (districtId is not > 0)
        {
            return ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>.Failure(
                "INVALID_DESTINATION",
                "Đơn hàng chưa có mã quận/huyện GHN hợp lệ.");
        }

        return await _gateway.GetAvailableServicesAsync(districtId.Value, cancellationToken);
    }

    public async Task<ShippingOperationResult<ShippingQuote>> QuoteAsync(
        long shipmentId,
        ShippingExecutionInput input,
        CancellationToken cancellationToken)
    {
        var aggregate = await LoadAggregateAsync(shipmentId, tracked: false, cancellationToken);
        if (aggregate is null)
        {
            return ShippingOperationResult<ShippingQuote>.Failure(
                "SHIPMENT_NOT_FOUND",
                "Không tìm thấy shipment.");
        }

        var validation = ValidateExecutionInput(aggregate.Order, aggregate.Shipment, input);
        if (validation is not null)
        {
            return ShippingOperationResult<ShippingQuote>.Failure(
                "INVALID_SHIPMENT_INPUT",
                validation);
        }

        return await _gateway.QuoteAsync(
            BuildQuoteRequest(aggregate.Order, input),
            cancellationToken);
    }

    public Task<ShippingQueueResult> QueueCreateAsync(
        long shipmentId,
        ShippingExecutionInput input,
        string actor,
        CancellationToken cancellationToken)
    {
        var tracker = new CommerceFlowTracker(
            "OutboundShipment",
            nameof(Shipment),
            shipmentId.ToString(CultureInfo.InvariantCulture),
            "QueueCreateShipment",
            idempotencyKey: BuildCreateKey(shipmentId));

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var aggregate = await LoadAggregateAsync(shipmentId, tracked: true, token)
                    ?? throw new BusinessRuleViolationException(
                        "SHIPMENT_NOT_FOUND",
                        "Không tìm thấy shipment cần tạo trên GHN.",
                        tracker.Snapshot());

                tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, aggregate.Shipment.Status.ToString())
                    .AddMetadata("OrderStatus", aggregate.Order.OrderStatus)
                    .AddMetadata("FulfillmentStatus", aggregate.Order.FulfillmentStatus)
                    .AddMetadata("Direction", aggregate.Shipment.Direction);

                EnsureOutbound(aggregate.Shipment, tracker);
                var validation = ValidateExecutionInput(aggregate.Order, aggregate.Shipment, input);
                if (validation is not null)
                {
                    throw new BusinessRuleViolationException(
                        "INVALID_SHIPMENT_INPUT",
                        validation,
                        tracker.Snapshot());
                }

                if (aggregate.Order.OrderStatus != OrderStatus.Processing)
                {
                    throw new BusinessRuleViolationException(
                        "ORDER_NOT_READY_FOR_SHIPMENT",
                        $"Đơn phải ở Processing trước khi tạo vận đơn; trạng thái hiện tại là {aggregate.Order.OrderStatus}.",
                        tracker.Snapshot());
                }

                if (aggregate.Order.FulfillmentStatus != FulfillmentStatus.ReadyToShip)
                {
                    throw new BusinessRuleViolationException(
                        "FULFILLMENT_NOT_READY_TO_SHIP",
                        $"Fulfillment phải ở ReadyToShip trước khi tạo vận đơn; trạng thái hiện tại là {aggregate.Order.FulfillmentStatus}.",
                        tracker.Snapshot());
                }

                if (!string.IsNullOrWhiteSpace(aggregate.Shipment.ExternalOrderCode))
                {
                    return new ShippingQueueResult(
                        null,
                        true,
                        $"Shipment đã có mã GHN {aggregate.Shipment.ExternalOrderCode}.");
                }

                var key = BuildCreateKey(shipmentId);
                tracker.MoveTo(CommerceFlowStage.Deduplicate);
                var existing = await _context.IntegrationOutboxMessages
                    .SingleOrDefaultAsync(
                        item => item.Provider == _gateway.Provider
                            && item.IdempotencyKey == key,
                        token);

                if (existing is not null
                    && existing.Status is IntegrationOutboxStatus.Pending
                        or IntegrationOutboxStatus.Processing
                        or IntegrationOutboxStatus.Completed)
                {
                    if (existing.Status == IntegrationOutboxStatus.Completed
                        && string.IsNullOrWhiteSpace(aggregate.Shipment.ExternalOrderCode))
                    {
                        throw new IdempotencyConflictException(
                            "SHIPMENT_CREATE_COMPLETED_WITHOUT_PROVIDER_REFERENCE",
                            $"Outbox {existing.Id} đã Completed nhưng shipment chưa có mã GHN. Cần reconcile thủ công trước khi retry.",
                            tracker.Snapshot());
                    }

                    if (aggregate.Shipment.Status != ShipmentStatus.PendingCreation
                        && existing.Status != IntegrationOutboxStatus.Completed)
                    {
                        throw new IdempotencyConflictException(
                            "SHIPMENT_CREATE_IDEMPOTENCY_STATE_MISMATCH",
                            $"Outbox {existing.Id} đã tồn tại nhưng shipment đang ở {aggregate.Shipment.Status}.",
                            tracker.Snapshot());
                    }

                    return new ShippingQueueResult(
                        existing.Id,
                        true,
                        "Yêu cầu tạo vận đơn đã tồn tại.");
                }

                OutboundShipmentStateMachine.EnsureInternalTransition(
                    aggregate.Shipment.Status,
                    ShipmentStatus.PendingCreation,
                    tracker);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                aggregate.Shipment.ServiceCode = input.ServiceId > 0
                    ? input.ServiceId.ToString(CultureInfo.InvariantCulture)
                    : null;
                aggregate.Shipment.ServiceName = $"GHN service type {input.ServiceTypeId}";
                aggregate.Shipment.WeightGram = input.WeightGram;
                aggregate.Shipment.LengthCm = input.LengthCm;
                aggregate.Shipment.WidthCm = input.WidthCm;
                aggregate.Shipment.HeightCm = input.HeightCm;
                aggregate.Shipment.CodAmount = aggregate.Order.PaymentStatus == PaymentStatus.CodPending
                    ? aggregate.Order.GrandTotal
                    : 0m;
                aggregate.Shipment.Status = ShipmentStatus.PendingCreation;
                aggregate.Shipment.ProviderStatus = "queued_create";
                aggregate.Shipment.UpdatedAt = nowUtc;

                var payload = JsonSerializer.Serialize(new GhnCreateShipmentMessage(
                    aggregate.Order.Id,
                    aggregate.Shipment.Id,
                    input.ServiceId,
                    input.ServiceTypeId,
                    input.Note?.Trim()));

                tracker.MoveTo(CommerceFlowStage.WriteOutbox);
                if (existing is null)
                {
                    existing = new IntegrationOutboxMessage
                    {
                        Provider = _gateway.Provider,
                        MessageType = CreateMessageType,
                        AggregateType = nameof(Shipment),
                        AggregateId = shipmentId.ToString(CultureInfo.InvariantCulture),
                        IdempotencyKey = key,
                        Status = IntegrationOutboxStatus.Pending,
                        Payload = payload,
                        NextAttemptAt = nowUtc,
                        CorrelationId = ToCorrelationId(tracker.CorrelationId),
                        CreatedAt = nowUtc
                    };
                    _context.IntegrationOutboxMessages.Add(existing);
                }
                else
                {
                    existing.Status = IntegrationOutboxStatus.Pending;
                    existing.Payload = payload;
                    existing.NextAttemptAt = nowUtc;
                    existing.LastError = null;
                    existing.LockedAt = null;
                    existing.UpdatedAt = nowUtc;
                }

                tracker.MoveTo(CommerceFlowStage.WriteTimeline);
                AddTimeline(
                    aggregate.Order,
                    "GHN_CREATE_QUEUED",
                    "Đã xếp hàng tạo vận đơn GHN",
                    $"Shipment #{shipmentId} chờ worker gọi GHN.",
                    actor,
                    tracker.CorrelationId,
                    nowUtc,
                    customerVisible: false);

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                return new ShippingQueueResult(
                    existing.Id,
                    false,
                    "Đã xếp hàng tạo vận đơn GHN.");
            },
            cancellationToken);
    }

    public Task<ShippingQueueResult> QueueCancelAsync(
        long shipmentId,
        string reason,
        string actor,
        CancellationToken cancellationToken)
    {
        var tracker = new CommerceFlowTracker(
            "OutboundShipmentCancellation",
            nameof(Shipment),
            shipmentId.ToString(CultureInfo.InvariantCulture),
            "QueueCancelShipment",
            idempotencyKey: BuildCancelKey(shipmentId));
        tracker.MoveTo(CommerceFlowStage.ValidateInput);
        var normalizedReason = NormalizeRequired(reason, 500, "Lý do hủy vận đơn", tracker);

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var aggregate = await LoadAggregateAsync(shipmentId, tracked: true, token)
                    ?? throw new BusinessRuleViolationException(
                        "SHIPMENT_NOT_FOUND",
                        "Không tìm thấy shipment cần hủy.",
                        tracker.Snapshot());

                EnsureOutbound(aggregate.Shipment, tracker);
                tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, aggregate.Shipment.Status.ToString());
                var hasPendingOrderCancellation = await _context.Set<OrderCancellationRequest>()
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.OrderId == aggregate.Order.Id
                            && item.Status == OrderCancellationStatus.Pending,
                        token);
                if (!hasPendingOrderCancellation)
                {
                    throw new BusinessRuleViolationException(
                        "SHIPMENT_CANCEL_REQUIRES_PENDING_ORDER_CANCELLATION",
                        "Hủy vận đơn chiều đi phải gắn với một yêu cầu hủy đơn Pending. Hãy tạo cancellation request trên đơn hàng trước.",
                        tracker.Snapshot());
                }

                OutboundShipmentStateMachine.EnsureCancellationCanBeRequested(
                    aggregate.Shipment.Status,
                    aggregate.Shipment.CarrierHandoffAt,
                    tracker);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                if (string.IsNullOrWhiteSpace(aggregate.Shipment.ExternalOrderCode))
                {
                    var createKey = BuildCreateKey(shipmentId);
                    var createMessage = await _context.IntegrationOutboxMessages
                        .SingleOrDefaultAsync(
                            item => item.Provider == _gateway.Provider
                                && item.IdempotencyKey == createKey,
                            token);

                    if (createMessage?.Status == IntegrationOutboxStatus.Processing)
                    {
                        throw new BusinessRuleViolationException(
                            "SHIPMENT_CREATE_IN_PROGRESS",
                            "Worker đang gọi GHN tạo vận đơn. Không thể hủy local cho đến khi create flow kết thúc hoặc được reconcile.",
                            tracker.Snapshot());
                    }

                    if (createMessage?.Status == IntegrationOutboxStatus.Completed)
                    {
                        throw new IdempotencyConflictException(
                            "SHIPMENT_CREATE_COMPLETED_WITHOUT_PROVIDER_REFERENCE",
                            "Outbox tạo vận đơn đã Completed nhưng shipment chưa có mã provider. Cần reconcile trước khi hủy.",
                            tracker.Snapshot());
                    }

                    OutboundShipmentStateMachine.EnsureInternalTransition(
                        aggregate.Shipment.Status,
                        ShipmentStatus.Cancelled,
                        tracker);

                    aggregate.Shipment.Status = ShipmentStatus.Cancelled;
                    aggregate.Shipment.ProviderStatus = "cancelled_before_provider_creation";
                    aggregate.Shipment.ProviderReason = normalizedReason;
                    aggregate.Shipment.CancelRequestedAt ??= nowUtc;
                    aggregate.Shipment.CancelledAt ??= nowUtc;
                    aggregate.Shipment.UpdatedAt = nowUtc;

                    if (createMessage is not null
                        && createMessage.Status is IntegrationOutboxStatus.Pending
                            or IntegrationOutboxStatus.Failed)
                    {
                        createMessage.Status = IntegrationOutboxStatus.Completed;
                        createMessage.CompletedAt = nowUtc;
                        createMessage.NextAttemptAt = null;
                        createMessage.LastError = "Shipment bị hủy trước khi gọi provider.";
                        createMessage.UpdatedAt = nowUtc;
                    }

                    tracker.MoveTo(CommerceFlowStage.WriteTimeline);
                    AddTimeline(
                        aggregate.Order,
                        "SHIPMENT_CANCELLED_BEFORE_PROVIDER",
                        "Đã hủy shipment trước khi gửi GHN",
                        normalizedReason,
                        actor,
                        tracker.CorrelationId,
                        nowUtc,
                        customerVisible: false);

                    tracker.MoveTo(CommerceFlowStage.SaveChanges);
                    await _context.SaveChangesAsync(token);
                    return new ShippingQueueResult(
                        null,
                        false,
                        "Đã hủy shipment trước khi gửi GHN.");
                }

                var key = BuildCancelKey(shipmentId);
                tracker.MoveTo(CommerceFlowStage.Deduplicate);
                var existing = await _context.IntegrationOutboxMessages
                    .SingleOrDefaultAsync(
                        item => item.Provider == _gateway.Provider
                            && item.IdempotencyKey == key,
                        token);

                if (existing is not null
                    && existing.Status is IntegrationOutboxStatus.Pending
                        or IntegrationOutboxStatus.Processing
                        or IntegrationOutboxStatus.Completed)
                {
                    return new ShippingQueueResult(
                        existing.Id,
                        true,
                        "Yêu cầu hủy vận đơn đã tồn tại.");
                }

                OutboundShipmentStateMachine.EnsureInternalTransition(
                    aggregate.Shipment.Status,
                    ShipmentStatus.CancelRequested,
                    tracker);

                aggregate.Shipment.Status = ShipmentStatus.CancelRequested;
                aggregate.Shipment.CancelRequestedAt ??= nowUtc;
                aggregate.Shipment.ProviderReason = normalizedReason;
                aggregate.Shipment.UpdatedAt = nowUtc;

                var payload = JsonSerializer.Serialize(new GhnCancelShipmentMessage(
                    aggregate.Order.Id,
                    aggregate.Shipment.Id,
                    aggregate.Shipment.ExternalOrderCode,
                    normalizedReason));

                tracker.MoveTo(CommerceFlowStage.WriteOutbox);
                if (existing is null)
                {
                    existing = new IntegrationOutboxMessage
                    {
                        Provider = _gateway.Provider,
                        MessageType = CancelMessageType,
                        AggregateType = nameof(Shipment),
                        AggregateId = shipmentId.ToString(CultureInfo.InvariantCulture),
                        IdempotencyKey = key,
                        Status = IntegrationOutboxStatus.Pending,
                        Payload = payload,
                        NextAttemptAt = nowUtc,
                        CorrelationId = ToCorrelationId(tracker.CorrelationId),
                        CreatedAt = nowUtc
                    };
                    _context.IntegrationOutboxMessages.Add(existing);
                }
                else
                {
                    existing.Status = IntegrationOutboxStatus.Pending;
                    existing.Payload = payload;
                    existing.NextAttemptAt = nowUtc;
                    existing.LastError = null;
                    existing.LockedAt = null;
                    existing.UpdatedAt = nowUtc;
                }

                tracker.MoveTo(CommerceFlowStage.WriteTimeline);
                AddTimeline(
                    aggregate.Order,
                    "GHN_CANCEL_QUEUED",
                    "Đã gửi yêu cầu hủy vận đơn vào outbox",
                    normalizedReason,
                    actor,
                    tracker.CorrelationId,
                    nowUtc,
                    customerVisible: false);

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                return new ShippingQueueResult(
                    existing.Id,
                    false,
                    "Đã xếp hàng hủy vận đơn GHN. Hệ thống chưa hoàn kho cho đến khi provider xác nhận.");
            },
            cancellationToken);
    }

    public async Task<ShippingOperationResult<bool>> SyncAsync(
        long shipmentId,
        string actor,
        CancellationToken cancellationToken)
    {
        var lookup = await _context.Shipments
            .AsNoTracking()
            .Where(item => item.Id == shipmentId)
            .Select(item => new
            {
                item.ExternalOrderCode,
                item.Direction
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (lookup is null)
        {
            return ShippingOperationResult<bool>.Failure(
                "SHIPMENT_NOT_FOUND",
                "Không tìm thấy shipment.");
        }

        if (lookup.Direction != ShipmentDirection.Outbound)
        {
            return ShippingOperationResult<bool>.Failure(
                "OUTBOUND_FLOW_REJECTED_RETURN_SHIPMENT",
                "Trang này chỉ đồng bộ vận đơn chiều đi.");
        }

        if (string.IsNullOrWhiteSpace(lookup.ExternalOrderCode))
        {
            return ShippingOperationResult<bool>.Failure(
                "TRACKING_NOT_CREATED",
                "Shipment chưa có mã vận đơn GHN.");
        }

        var providerResult = await _gateway.GetDetailAsync(
            lookup.ExternalOrderCode,
            cancellationToken);
        if (!providerResult.Success || providerResult.Data is null)
        {
            return ShippingOperationResult<bool>.Failure(
                providerResult.ErrorCode ?? "GHN_SYNC_FAILED",
                providerResult.Message,
                providerResult.Retryable);
        }

        var tracker = new CommerceFlowTracker(
            "OutboundShipmentReconciliation",
            nameof(Shipment),
            shipmentId.ToString(CultureInfo.InvariantCulture),
            "SyncProviderDetail");

        try
        {
            await CommerceFlowTransaction.ExecuteAsync(
                _context,
                tracker,
                async token =>
                {
                    tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                    var aggregate = await LoadAggregateAsync(shipmentId, tracked: true, token)
                        ?? throw new BusinessRuleViolationException(
                            "SHIPMENT_NOT_FOUND",
                            "Shipment đã bị xóa trước khi áp dụng dữ liệu provider.",
                            tracker.Snapshot());

                    tracker.MoveTo(CommerceFlowStage.ApplyProviderResponse, aggregate.Shipment.Status.ToString());
                    var decision = OutboundShipmentAggregateUpdater.ApplyProviderUpdate(
                        aggregate.Order,
                        aggregate.Shipment,
                        providerResult.Data,
                        actor,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        tracker);

                    if (!decision.Apply)
                    {
                        tracker.AddMetadata("ProviderEventIgnored", decision.IgnoreReason ?? string.Empty);
                    }

                    tracker.MoveTo(CommerceFlowStage.SaveChanges);
                    await _context.SaveChangesAsync(token);
                    return true;
                },
                cancellationToken);

            return ShippingOperationResult<bool>.Ok(true);
        }
        catch (CommerceFlowException exception)
        {
            return ShippingOperationResult<bool>.Failure(
                exception.ErrorCode,
                exception.Message,
                retryable: exception is CommerceConcurrencyException or CommerceTransactionException);
        }
    }

    internal static ShippingCreateRequest BuildCreateRequest(
        Order order,
        Shipment shipment,
        GhnShippingOptions options,
        int serviceId,
        int serviceTypeId,
        string? note)
    {
        if (order.ShippingDistrictId is not > 0
            || string.IsNullOrWhiteSpace(order.ShippingWardCode))
        {
            throw new InvalidOperationException("Order chưa có mã địa chỉ GHN hợp lệ.");
        }

        var totalQuantity = Math.Max(1, order.Items.Sum(item => item.Quantity));
        var itemWeight = Math.Max(1, shipment.WeightGram / totalQuantity);
        var items = order.Items
            .OrderBy(item => item.Id)
            .Select(item => new ShippingParcelItem(
                item.ProductName,
                item.Sku,
                item.Quantity,
                ToProviderMoney(item.UnitPrice),
                itemWeight,
                Math.Max(1, shipment.LengthCm),
                Math.Max(1, shipment.WidthCm),
                Math.Max(1, shipment.HeightCm)))
            .ToArray();

        var storeParty = new ShippingParty(
            options.SenderName,
            options.SenderPhone,
            options.SenderAddress,
            options.FromDistrictId,
            options.FromWardCode);
        var customerParty = new ShippingParty(
            order.CustomerName,
            order.CustomerPhone,
            string.Join(", ", new[]
            {
                order.ShippingAddressLine,
                order.ShippingWard,
                order.ShippingDistrict,
                order.ShippingCity
            }.Where(value => !string.IsNullOrWhiteSpace(value))),
            order.ShippingDistrictId.Value,
            order.ShippingWardCode);

        return new ShippingCreateRequest(
            order.Code,
            storeParty,
            customerParty,
            storeParty,
            serviceId,
            serviceTypeId,
            options.PaymentTypeId,
            options.RequiredNote,
            string.IsNullOrWhiteSpace(note) ? $"Đơn hàng {order.Code}" : note.Trim(),
            shipment.WeightGram,
            shipment.LengthCm,
            shipment.WidthCm,
            shipment.HeightCm,
            Math.Min(ToProviderMoney(order.GrandTotal), 5_000_000),
            order.PaymentStatus == PaymentStatus.CodPending
                ? ToProviderMoney(order.GrandTotal)
                : 0,
            items);
    }

    private ShippingQuoteRequest BuildQuoteRequest(Order order, ShippingExecutionInput input)
    {
        var totalQuantity = Math.Max(1, order.Items.Sum(item => item.Quantity));
        var itemWeight = Math.Max(1, input.WeightGram / totalQuantity);
        var items = order.Items
            .OrderBy(item => item.Id)
            .Select(item => new ShippingParcelItem(
                item.ProductName,
                item.Sku,
                item.Quantity,
                ToProviderMoney(item.UnitPrice),
                itemWeight,
                input.LengthCm,
                input.WidthCm,
                input.HeightCm))
            .ToArray();

        return new ShippingQuoteRequest(
            _options.FromDistrictId,
            _options.FromWardCode,
            order.ShippingDistrictId!.Value,
            order.ShippingWardCode!,
            input.ServiceId,
            input.ServiceTypeId,
            input.WeightGram,
            input.LengthCm,
            input.WidthCm,
            input.HeightCm,
            Math.Min(ToProviderMoney(order.GrandTotal), 5_000_000),
            order.PaymentStatus == PaymentStatus.CodPending
                ? Math.Min(ToProviderMoney(order.GrandTotal), 10_000_000)
                : 0,
            items);
    }

    private async Task<ShipmentAggregate?> LoadAggregateAsync(
        long shipmentId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        IQueryable<Shipment> query = _context.Shipments;
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        var shipment = await query
            .Include(item => item.Order)
                .ThenInclude(order => order.Items)
            .Include(item => item.Order)
                .ThenInclude(order => order.PaymentTransactions)
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .Include(item => item.Order)
                .ThenInclude(order => order.CancellationRequests)
            .SingleOrDefaultAsync(item => item.Id == shipmentId, cancellationToken);

        return shipment is null
            ? null
            : new ShipmentAggregate(shipment.Order, shipment);
    }

    private static string? ValidateExecutionInput(
        Order order,
        Shipment shipment,
        ShippingExecutionInput input)
    {
        if (shipment.Direction != ShipmentDirection.Outbound)
        {
            return "Chỉ vận đơn chiều đi được xử lý ở outbound shipping flow.";
        }

        if (order.ShippingDistrictId is not > 0
            || string.IsNullOrWhiteSpace(order.ShippingWardCode))
        {
            return "Đơn hàng chưa có mã địa chỉ GHN hợp lệ.";
        }

        if (input.ServiceTypeId is not 2 and not 5)
        {
            return "ServiceTypeId GHN chỉ hỗ trợ 2 hoặc 5.";
        }

        if (input.ServiceId < 0)
        {
            return "ServiceId không hợp lệ.";
        }

        if (input.WeightGram is <= 0 or > 50_000)
        {
            return "Khối lượng phải từ 1 đến 50.000 gram.";
        }

        if (input.LengthCm is <= 0 or > 200
            || input.WidthCm is <= 0 or > 200
            || input.HeightCm is <= 0 or > 200)
        {
            return "Kích thước mỗi chiều phải từ 1 đến 200 cm.";
        }

        if (order.Items.Count == 0)
        {
            return "Đơn hàng không có sản phẩm.";
        }

        return null;
    }

    private static void EnsureOutbound(Shipment shipment, CommerceFlowTracker tracker)
    {
        if (shipment.Direction != ShipmentDirection.Outbound)
        {
            throw new BusinessRuleViolationException(
                "OUTBOUND_FLOW_REJECTED_RETURN_SHIPMENT",
                "Outbound shipping flow không được xử lý vận đơn chiều về.",
                tracker.Snapshot());
        }
    }

    private static void AddTimeline(
        Order order,
        string code,
        string title,
        string? description,
        string actor,
        string correlationId,
        DateTime occurredAt,
        bool customerVisible)
    {
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = OrderHistoryCategory.Integration,
            FromStatus = null,
            ToStatus = code,
            Code = code,
            Title = title,
            Description = description,
            ChangedBy = string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(),
            CustomerVisible = customerVisible,
            OccurredAt = occurredAt,
            CorrelationId = ToCorrelationId(correlationId)
        });
    }

    private static int ToProviderMoney(decimal value) =>
        checked((int)Math.Round(
            Math.Clamp(value, 0m, 50_000_000m),
            0,
            MidpointRounding.AwayFromZero));

    private static string NormalizeRequired(
        string value,
        int maxLength,
        string label,
        CommerceFlowTracker tracker)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleViolationException(
                "SHIPMENT_CANCEL_REASON_REQUIRED",
                $"{label} là bắt buộc.",
                tracker.Snapshot());
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new BusinessRuleViolationException(
                "SHIPMENT_CANCEL_REASON_TOO_LONG",
                $"{label} không được vượt {maxLength} ký tự.",
                tracker.Snapshot());
        }

        return normalized;
    }

    private static string BuildCreateKey(long shipmentId) =>
        $"ghn:create:shipment:{shipmentId}";

    private static string BuildCancelKey(long shipmentId) =>
        $"ghn:cancel:shipment:{shipmentId}";

    private static string ToCorrelationId(string value) =>
        value.Length <= 64 ? value : value[..64];

    private sealed record ShipmentAggregate(Order Order, Shipment Shipment);
}

public sealed record GhnCreateShipmentMessage(
    int OrderId,
    long ShipmentId,
    int ServiceId,
    int ServiceTypeId,
    string? Note);

public sealed record GhnCancelShipmentMessage(
    int OrderId,
    long ShipmentId,
    string ExternalOrderCode,
    string Reason);
