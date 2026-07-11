using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Returns;

namespace WebApplication2.Services.Commerce.Orders;

public sealed class OrderWorkflowService : IOrderWorkflowService
{
    private const int ActorMaxLength = 100;
    private const int ReasonMaxLength = 500;

    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedOrderTransitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.PendingPayment] = [OrderStatus.Placed],
            [OrderStatus.Placed] = [OrderStatus.Confirmed],
            [OrderStatus.Confirmed] = [OrderStatus.Processing],
            [OrderStatus.Processing] = [],
            [OrderStatus.Completed] = [OrderStatus.Closed],
            [OrderStatus.Cancelled] = [OrderStatus.Closed],
            [OrderStatus.Closed] = []
        };

    private static readonly IReadOnlyDictionary<PaymentStatus, PaymentStatus[]> AllowedPaymentTransitions =
        new Dictionary<PaymentStatus, PaymentStatus[]>
        {
            [PaymentStatus.Pending] = [],
            [PaymentStatus.CodPending] = [],
            [PaymentStatus.Paid] = [],
            [PaymentStatus.Failed] = [],
            [PaymentStatus.Cancelled] = [],
            [PaymentStatus.PartiallyRefunded] = [],
            [PaymentStatus.Refunded] = []
        };

    private static readonly IReadOnlyDictionary<FulfillmentStatus, FulfillmentStatus[]>
        AllowedFulfillmentTransitions =
            new Dictionary<FulfillmentStatus, FulfillmentStatus[]>
            {
                [FulfillmentStatus.Unfulfilled] = [FulfillmentStatus.Preparing],
                [FulfillmentStatus.Preparing] = [FulfillmentStatus.ReadyToShip],
                [FulfillmentStatus.ReadyToShip] = [],
                [FulfillmentStatus.Shipped] = [],
                [FulfillmentStatus.DeliveryFailed] = [],
                [FulfillmentStatus.Delivered] = [],
                [FulfillmentStatus.Returning] = [],
                [FulfillmentStatus.Returned] = [],
                [FulfillmentStatus.Cancelled] = []
            };

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public OrderWorkflowService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<OrderStatus> GetAllowedOrderTransitions(OrderStatus currentStatus) =>
        AllowedOrderTransitions.TryGetValue(currentStatus, out var transitions)
            ? transitions
            : [];

    public IReadOnlyList<PaymentStatus> GetAllowedPaymentTransitions(PaymentStatus currentStatus) =>
        AllowedPaymentTransitions.TryGetValue(currentStatus, out var transitions)
            ? transitions
            : [];

    public IReadOnlyList<FulfillmentStatus> GetAllowedFulfillmentTransitions(
        FulfillmentStatus currentStatus) =>
        AllowedFulfillmentTransitions.TryGetValue(currentStatus, out var transitions)
            ? transitions
            : [];

    public async Task<Order> TransitionAsync(
        int orderId,
        OrderStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var tracker = CreateTracker(orderId, "TransitionOrderStatus", targetStatus);
        try
        {
            var order = await LoadAggregateAsync(orderId, tracker, cancellationToken);
            if (order.OrderStatus == targetStatus)
            {
                return order;
            }

            EnsureTransitionAllowed(
                AllowedOrderTransitions,
                order.OrderStatus,
                targetStatus,
                "đơn hàng",
                tracker);

            tracker.MoveTo(CommerceFlowStage.ValidateInput, order.OrderStatus.ToString());
            var normalizedActor = NormalizeRequired(actor, ActorMaxLength, nameof(actor), tracker);
            var normalizedReason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason), tracker);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, order.OrderStatus.ToString());
            ValidateOrderInvariant(order, targetStatus, nowUtc, tracker);

            tracker.MoveTo(CommerceFlowStage.ValidateConcurrency, order.OrderStatus.ToString());
            ApplyOriginalRowVersion(order, rowVersion, tracker);

            var previousStatus = order.OrderStatus;
            tracker.MoveTo(CommerceFlowStage.ApplyStateTransition, previousStatus.ToString());
            order.OrderStatus = targetStatus;
            order.UpdatedAt = nowUtc;

            if (targetStatus == OrderStatus.Placed)
            {
                order.PlacedAt ??= nowUtc;
            }
            else if (targetStatus == OrderStatus.Confirmed)
            {
                order.ConfirmedAt ??= nowUtc;
            }

            tracker.MoveTo(CommerceFlowStage.WriteTimeline, targetStatus.ToString());
            AddHistory(
                order,
                OrderHistoryCategory.Order,
                previousStatus.ToString(),
                targetStatus.ToString(),
                $"ORDER_{targetStatus.ToString().ToUpperInvariant()}",
                GetOrderTitle(targetStatus),
                normalizedReason,
                normalizedActor,
                nowUtc,
                tracker.CorrelationId,
                customerVisible: true);

            await SaveAggregateAsync(tracker, cancellationToken);
            return order;
        }
        catch (OrderTransitionException)
        {
            throw;
        }
        catch (OrderConcurrencyException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OrderTransitionException(
                "ORDER_WORKFLOW_FAILED",
                "Order status workflow thất bại.",
                tracker.Snapshot(),
                exception);
        }
    }

    public async Task<Order> TransitionPaymentAsync(
        int orderId,
        PaymentStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var tracker = CreateTracker(orderId, "TransitionPaymentStatus", targetStatus);
        try
        {
            var order = await LoadAggregateAsync(orderId, tracker, cancellationToken);
            if (order.PaymentStatus == targetStatus)
            {
                return order;
            }

            EnsureTransitionAllowed(
                AllowedPaymentTransitions,
                order.PaymentStatus,
                targetStatus,
                "thanh toán",
                tracker);

            tracker.MoveTo(CommerceFlowStage.ValidateInput, order.PaymentStatus.ToString());
            _ = NormalizeRequired(actor, ActorMaxLength, nameof(actor), tracker);
            _ = NormalizeRequired(reason, ReasonMaxLength, nameof(reason), tracker);

            throw new OrderTransitionException(
                "PAYMENT_STATUS_REQUIRES_PROVIDER_FLOW",
                "Payment status không được thay đổi thủ công. Paid phải đến từ payment callback hoặc GHN Delivered đối với COD; refund phải đi qua refund workflow.",
                tracker.MoveTo(
                    CommerceFlowStage.ValidateBusinessRules,
                    order.PaymentStatus.ToString()).Snapshot());
        }
        catch (OrderTransitionException)
        {
            throw;
        }
        catch (OrderConcurrencyException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OrderTransitionException(
                "PAYMENT_WORKFLOW_FAILED",
                "Payment status workflow thất bại.",
                tracker.Snapshot(),
                exception);
        }
    }

    public async Task<Order> TransitionFulfillmentAsync(
        int orderId,
        FulfillmentStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var tracker = CreateTracker(orderId, "TransitionFulfillmentStatus", targetStatus);
        try
        {
            var order = await LoadAggregateAsync(orderId, tracker, cancellationToken);
            if (order.FulfillmentStatus == targetStatus)
            {
                return order;
            }

            EnsureTransitionAllowed(
                AllowedFulfillmentTransitions,
                order.FulfillmentStatus,
                targetStatus,
                "xử lý giao hàng",
                tracker);

            tracker.MoveTo(CommerceFlowStage.ValidateInput, order.FulfillmentStatus.ToString());
            var normalizedActor = NormalizeRequired(actor, ActorMaxLength, nameof(actor), tracker);
            var normalizedReason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason), tracker);

            var shipment = GetLatestOutboundShipment(order, tracker);
            tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, order.FulfillmentStatus.ToString());
            ValidateFulfillmentInvariant(order, shipment, targetStatus, tracker);

            tracker.MoveTo(CommerceFlowStage.ValidateConcurrency, order.FulfillmentStatus.ToString());
            ApplyOriginalRowVersion(order, rowVersion, tracker);

            var previousStatus = order.FulfillmentStatus;
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            tracker.MoveTo(CommerceFlowStage.ApplyStateTransition, previousStatus.ToString());
            order.FulfillmentStatus = targetStatus;
            order.UpdatedAt = nowUtc;

            tracker.MoveTo(CommerceFlowStage.WriteTimeline, targetStatus.ToString());
            AddHistory(
                order,
                OrderHistoryCategory.Fulfillment,
                previousStatus.ToString(),
                targetStatus.ToString(),
                $"FULFILLMENT_{targetStatus.ToString().ToUpperInvariant()}",
                GetFulfillmentTitle(targetStatus),
                normalizedReason,
                normalizedActor,
                nowUtc,
                tracker.CorrelationId,
                customerVisible: true);

            await SaveAggregateAsync(tracker, cancellationToken);
            return order;
        }
        catch (OrderTransitionException)
        {
            throw;
        }
        catch (OrderConcurrencyException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OrderTransitionException(
                "FULFILLMENT_WORKFLOW_FAILED",
                "Fulfillment workflow thất bại.",
                tracker.Snapshot(),
                exception);
        }
    }

    private async Task<Order> LoadAggregateAsync(
        int orderId,
        CommerceFlowTracker tracker,
        CancellationToken cancellationToken)
    {
        tracker.MoveTo(CommerceFlowStage.ValidateInput);
        if (orderId <= 0)
        {
            throw new OrderTransitionException(
                "ORDER_ID_INVALID",
                "Mã đơn hàng không hợp lệ.",
                tracker.Snapshot());
        }

        tracker.MoveTo(CommerceFlowStage.LoadAggregate);
        return await _context.Orders
            .Include(item => item.PaymentTransactions)
            .Include(item => item.Shipments)
            .Include(item => item.ReturnRequests)
            .Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new OrderTransitionException(
                "ORDER_NOT_FOUND",
                $"Không tìm thấy đơn hàng {orderId}.",
                tracker.Snapshot());
    }

    private static void EnsureTransitionAllowed<TStatus>(
        IReadOnlyDictionary<TStatus, TStatus[]> map,
        TStatus currentStatus,
        TStatus targetStatus,
        string category,
        CommerceFlowTracker tracker)
        where TStatus : struct, Enum
    {
        tracker.MoveTo(CommerceFlowStage.ValidateStateTransition, currentStatus.ToString())
            .AddMetadata("TargetStatus", targetStatus);

        if (!map.TryGetValue(currentStatus, out var allowed)
            || !allowed.Contains(targetStatus))
        {
            var code = typeof(TStatus) == typeof(OrderStatus)
                && targetStatus.ToString() == nameof(OrderStatus.Completed)
                ? "ORDER_COMPLETED_REQUIRES_GHN_DELIVERED"
                : typeof(TStatus) == typeof(PaymentStatus)
                    ? "PAYMENT_STATUS_REQUIRES_PROVIDER_FLOW"
                    : typeof(TStatus) == typeof(FulfillmentStatus)
                        ? "FULFILLMENT_PROVIDER_STATE_MANUAL_TRANSITION_BLOCKED"
                        : "ORDER_TRANSITION_NOT_ALLOWED";

            throw new OrderTransitionException(
                code,
                $"Không thể chuyển trạng thái {category} từ {currentStatus} sang {targetStatus}.",
                tracker.Snapshot());
        }
    }

    private static void ValidateOrderInvariant(
        Order order,
        OrderStatus targetStatus,
        DateTime nowUtc,
        CommerceFlowTracker tracker)
    {
        if (targetStatus == OrderStatus.Cancelled)
        {
            throw new OrderTransitionException(
                "ORDER_CANCELLATION_REQUIRES_CANCELLATION_FLOW",
                "Hủy đơn phải đi qua cancellation workflow để hoàn kho và xử lý thanh toán an toàn.",
                tracker.Snapshot());
        }

        if (targetStatus == OrderStatus.Placed
            && order.PaymentStatus is not PaymentStatus.Paid and not PaymentStatus.CodPending)
        {
            throw new OrderTransitionException(
                "ORDER_PLACED_REQUIRES_VALID_PAYMENT",
                "Chỉ có thể ghi nhận đơn đã đặt khi thanh toán hợp lệ hoặc đang chờ COD.",
                tracker.Snapshot());
        }

        if (targetStatus is OrderStatus.Confirmed or OrderStatus.Processing
            && order.PaymentStatus is not PaymentStatus.Paid and not PaymentStatus.CodPending)
        {
            throw new OrderTransitionException(
                "ORDER_PROCESSING_REQUIRES_VALID_PAYMENT",
                "Không thể xử lý đơn chưa có trạng thái thanh toán hợp lệ.",
                tracker.Snapshot());
        }

        if (targetStatus == OrderStatus.Closed
            && order.OrderStatus == OrderStatus.Completed)
        {
            var activeReturn = order.ReturnRequests
                .FirstOrDefault(item => item.Status is not
                    ReturnRequestStatus.Rejected
                    and not ReturnRequestStatus.RejectedAfterInspection
                    and not ReturnRequestStatus.Cancelled
                    and not ReturnRequestStatus.Closed);
            if (activeReturn is not null)
            {
                throw new OrderTransitionException(
                    "ORDER_CLOSE_BLOCKED_BY_ACTIVE_RETURN",
                    $"Không thể đóng đơn vì return {activeReturn.Code} đang ở {activeReturn.Status}.",
                    tracker.Snapshot());
            }

            var deliveredOutbound = ReturnPolicy.GetDeliveredOutboundShipment(order);
            if (deliveredOutbound?.DeliveredAt is DateTime deliveredAt
                && ReturnPolicy.NormalizeUtc(nowUtc) <= ReturnPolicy.GetDeadlineUtc(deliveredAt))
            {
                throw new OrderTransitionException(
                    "ORDER_CLOSE_BLOCKED_BY_RETURN_WINDOW",
                    $"Không thể đóng đơn trước khi hết thời hạn hoàn trả {ReturnPolicy.GetDeadlineUtc(deliveredAt):O}.",
                    tracker.Snapshot());
            }
        }
    }

    private static void ValidateFulfillmentInvariant(
        Order order,
        Shipment shipment,
        FulfillmentStatus targetStatus,
        CommerceFlowTracker tracker)
    {
        if (order.OrderStatus != OrderStatus.Processing)
        {
            throw new OrderTransitionException(
                "FULFILLMENT_REQUIRES_PROCESSING_ORDER",
                "Đơn phải ở trạng thái Processing trước khi chuẩn bị giao hàng.",
                tracker.Snapshot());
        }

        if (shipment.Direction != ShipmentDirection.Outbound)
        {
            throw new OrderTransitionException(
                "FULFILLMENT_REJECTED_RETURN_SHIPMENT",
                "Admin order workflow chỉ được chuẩn bị vận đơn chiều đi.",
                tracker.Snapshot());
        }

        if (shipment.Status == ShipmentStatus.Cancelled)
        {
            throw new OrderTransitionException(
                "OUTBOUND_SHIPMENT_CANCELLED",
                "Vận đơn chiều đi hiện tại đã bị hủy.",
                tracker.Snapshot());
        }

        if (targetStatus == FulfillmentStatus.ReadyToShip
            && shipment.Status != ShipmentStatus.Draft)
        {
            throw new OrderTransitionException(
                "READY_TO_SHIP_REQUIRES_DRAFT_SHIPMENT",
                $"Chỉ được chuyển ReadyToShip khi outbound shipment còn Draft; hiện tại là {shipment.Status}.",
                tracker.Snapshot());
        }

        if (targetStatus is FulfillmentStatus.Shipped
            or FulfillmentStatus.Delivered
            or FulfillmentStatus.DeliveryFailed
            or FulfillmentStatus.Returning
            or FulfillmentStatus.Returned)
        {
            throw new OrderTransitionException(
                "FULFILLMENT_PROVIDER_STATE_MANUAL_TRANSITION_BLOCKED",
                "Shipped/Delivered/DeliveryFailed chỉ đến từ GHN outbound flow; Returning/Returned chỉ đến từ return flow.",
                tracker.Snapshot());
        }
    }

    private static Shipment GetLatestOutboundShipment(
        Order order,
        CommerceFlowTracker tracker) =>
        order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault()
            ?? throw new OrderTransitionException(
                "OUTBOUND_SHIPMENT_NOT_FOUND",
                "Đơn hàng chưa có vận đơn chiều đi.",
                tracker.Snapshot());

    private void ApplyOriginalRowVersion(
        Order order,
        byte[] rowVersion,
        CommerceFlowTracker tracker)
    {
        if (rowVersion is not { Length: > 0 })
        {
            throw new OrderTransitionException(
                "ORDER_ROW_VERSION_REQUIRED",
                "RowVersion của đơn hàng là bắt buộc.",
                tracker.Snapshot());
        }

        _context.Entry(order)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;
    }

    private async Task SaveAggregateAsync(
        CommerceFlowTracker tracker,
        CancellationToken cancellationToken)
    {
        tracker.MoveTo(CommerceFlowStage.SaveChanges);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            tracker.MoveTo(CommerceFlowStage.Complete);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new OrderConcurrencyException(
                "Đơn hàng đã được cập nhật bởi thao tác khác. Vui lòng tải lại dữ liệu.",
                tracker.MoveTo(CommerceFlowStage.ValidateConcurrency).Snapshot(),
                exception);
        }
        catch (DbUpdateException exception)
        {
            throw new OrderTransitionException(
                "ORDER_DATABASE_UPDATE_FAILED",
                "Database từ chối cập nhật order workflow.",
                tracker.Snapshot(),
                exception);
        }
    }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string parameterName,
        CommerceFlowTracker tracker)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OrderTransitionException(
                "ORDER_WORKFLOW_INPUT_REQUIRED",
                $"{parameterName} không được để trống.",
                tracker.Snapshot());
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new OrderTransitionException(
                "ORDER_WORKFLOW_INPUT_TOO_LONG",
                $"{parameterName} không được vượt {maxLength} ký tự.",
                tracker.Snapshot());
        }

        return normalized;
    }

    private static void AddHistory(
        Order order,
        OrderHistoryCategory category,
        string? fromStatus,
        string toStatus,
        string code,
        string title,
        string? description,
        string actor,
        DateTime occurredAt,
        string correlationId,
        bool customerVisible)
    {
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = category,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Code = code,
            Title = title,
            Description = description,
            ChangedBy = actor,
            CustomerVisible = customerVisible,
            OccurredAt = occurredAt,
            CorrelationId = correlationId.Length <= 64
                ? correlationId
                : correlationId[..64]
        });
    }

    private static CommerceFlowTracker CreateTracker<TStatus>(
        int orderId,
        string action,
        TStatus targetStatus)
        where TStatus : struct, Enum =>
        new CommerceFlowTracker(
            "OrderWorkflow",
            nameof(Order),
            orderId.ToString(CultureInfo.InvariantCulture),
            action)
            .AddMetadata("TargetStatus", targetStatus);

    private static string GetOrderTitle(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Chờ thanh toán",
        OrderStatus.Placed => "Đã đặt hàng",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Processing => "Đang xử lý",
        OrderStatus.Completed => "Đã hoàn tất",
        OrderStatus.Cancelled => "Đã hủy",
        OrderStatus.Closed => "Đã đóng",
        _ => status.ToString()
    };

    private static string GetFulfillmentTitle(FulfillmentStatus status) => status switch
    {
        FulfillmentStatus.Unfulfilled => "Chưa xử lý",
        FulfillmentStatus.Preparing => "Đang chuẩn bị hàng",
        FulfillmentStatus.ReadyToShip => "Sẵn sàng bàn giao",
        FulfillmentStatus.Shipped => "Đã bàn giao vận chuyển",
        FulfillmentStatus.Delivered => "Đã giao hàng",
        FulfillmentStatus.DeliveryFailed => "Giao hàng thất bại",
        FulfillmentStatus.Returning => "Đang hoàn hàng",
        FulfillmentStatus.Returned => "Đã hoàn hàng",
        FulfillmentStatus.Cancelled => "Đã hủy xử lý hàng",
        _ => status.ToString()
    };
}
