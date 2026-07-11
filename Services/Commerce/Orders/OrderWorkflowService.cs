using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

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
            [OrderStatus.Processing] = [OrderStatus.Completed],
            [OrderStatus.Completed] = [OrderStatus.Closed],
            [OrderStatus.Cancelled] = [OrderStatus.Closed],
            [OrderStatus.Closed] = []
        };

    private static readonly IReadOnlyDictionary<PaymentStatus, PaymentStatus[]> AllowedPaymentTransitions =
        new Dictionary<PaymentStatus, PaymentStatus[]>
        {
            [PaymentStatus.Pending] = [],
            [PaymentStatus.CodPending] = [PaymentStatus.Paid],
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
                [FulfillmentStatus.ReadyToShip] = [FulfillmentStatus.Shipped],
                [FulfillmentStatus.Shipped] =
                [
                    FulfillmentStatus.Delivered,
                    FulfillmentStatus.DeliveryFailed
                ],
                [FulfillmentStatus.DeliveryFailed] = [FulfillmentStatus.Shipped],
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

    public IReadOnlyList<OrderStatus> GetAllowedOrderTransitions(OrderStatus currentStatus)
    {
        return AllowedOrderTransitions.TryGetValue(currentStatus, out var transitions)
            ? transitions
            : [];
    }

    public IReadOnlyList<PaymentStatus> GetAllowedPaymentTransitions(PaymentStatus currentStatus)
    {
        return AllowedPaymentTransitions.TryGetValue(currentStatus, out var transitions)
            ? transitions
            : [];
    }

    public IReadOnlyList<FulfillmentStatus> GetAllowedFulfillmentTransitions(
        FulfillmentStatus currentStatus)
    {
        return AllowedFulfillmentTransitions.TryGetValue(currentStatus, out var transitions)
            ? transitions
            : [];
    }

    public async Task<Order> TransitionAsync(
        int orderId,
        OrderStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var order = await LoadAggregateAsync(orderId, cancellationToken);
        if (order.OrderStatus == targetStatus)
        {
            return order;
        }

        EnsureTransitionAllowed(
            AllowedOrderTransitions,
            order.OrderStatus,
            targetStatus,
            "đơn hàng");

        var normalizedActor = NormalizeRequired(actor, ActorMaxLength, nameof(actor));
        var normalizedReason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason));
        ValidateOrderInvariant(order, targetStatus);
        ApplyOriginalRowVersion(order, rowVersion);

        var previousStatus = order.OrderStatus;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        order.OrderStatus = targetStatus;
        order.UpdatedAt = nowUtc;

        switch (targetStatus)
        {
            case OrderStatus.Placed:
                order.PlacedAt ??= nowUtc;
                break;
            case OrderStatus.Confirmed:
                order.ConfirmedAt ??= nowUtc;
                break;
            case OrderStatus.Completed:
                order.CompletedAt ??= nowUtc;
                break;
        }

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
            customerVisible: true);

        await SaveAggregateAsync(cancellationToken);
        return order;
    }

    public async Task<Order> TransitionPaymentAsync(
        int orderId,
        PaymentStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var order = await LoadAggregateAsync(orderId, cancellationToken);
        if (order.PaymentStatus == targetStatus)
        {
            return order;
        }

        EnsureTransitionAllowed(
            AllowedPaymentTransitions,
            order.PaymentStatus,
            targetStatus,
            "thanh toán");

        var normalizedActor = NormalizeRequired(actor, ActorMaxLength, nameof(actor));
        var normalizedReason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason));
        var transaction = GetLatestPayment(order);
        ValidatePaymentInvariant(order, transaction, targetStatus);
        ApplyOriginalRowVersion(order, rowVersion);

        var previousStatus = order.PaymentStatus;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        order.PaymentStatus = targetStatus;
        order.UpdatedAt = nowUtc;
        transaction.Status = targetStatus;
        transaction.UpdatedAt = nowUtc;

        if (targetStatus is PaymentStatus.Paid or PaymentStatus.Failed or PaymentStatus.Cancelled)
        {
            transaction.CompletedAt ??= nowUtc;
        }

        if (targetStatus == PaymentStatus.Failed)
        {
            transaction.FailureCode ??= "ADMIN_MARKED_FAILED";
            transaction.FailureMessage = normalizedReason;
        }

        AddHistory(
            order,
            OrderHistoryCategory.Payment,
            previousStatus.ToString(),
            targetStatus.ToString(),
            $"PAYMENT_{targetStatus.ToString().ToUpperInvariant()}",
            GetPaymentTitle(targetStatus),
            normalizedReason,
            normalizedActor,
            nowUtc,
            customerVisible: true);

        await SaveAggregateAsync(cancellationToken);
        return order;
    }

    public async Task<Order> TransitionFulfillmentAsync(
        int orderId,
        FulfillmentStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        var order = await LoadAggregateAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == targetStatus)
        {
            return order;
        }

        EnsureTransitionAllowed(
            AllowedFulfillmentTransitions,
            order.FulfillmentStatus,
            targetStatus,
            "xử lý giao hàng");

        var normalizedActor = NormalizeRequired(actor, ActorMaxLength, nameof(actor));
        var normalizedReason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason));
        var shipment = GetLatestShipment(order);
        ValidateFulfillmentInvariant(order, shipment, targetStatus);
        ApplyOriginalRowVersion(order, rowVersion);

        var previousStatus = order.FulfillmentStatus;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        order.FulfillmentStatus = targetStatus;
        order.UpdatedAt = nowUtc;

        if (targetStatus == FulfillmentStatus.Shipped
            && shipment.Status is ShipmentStatus.Created or ShipmentStatus.Picking)
        {
            shipment.Status = ShipmentStatus.InTransit;
            shipment.UpdatedAt = nowUtc;
        }

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
            customerVisible: true);

        if (targetStatus == FulfillmentStatus.Delivered
            && order.PaymentStatus == PaymentStatus.CodPending)
        {
            MarkCodPaymentPaid(order, normalizedActor, nowUtc);
        }

        await SaveAggregateAsync(cancellationToken);
        return order;
    }

    private async Task<Order> LoadAggregateAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            throw new OrderTransitionException("Mã đơn hàng không hợp lệ.");
        }

        return await _context.Orders
            .Include(item => item.PaymentTransactions)
            .Include(item => item.Shipments)
            .Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new OrderTransitionException($"Không tìm thấy đơn hàng {orderId}.");
    }

    private static void EnsureTransitionAllowed<TStatus>(
        IReadOnlyDictionary<TStatus, TStatus[]> map,
        TStatus currentStatus,
        TStatus targetStatus,
        string category)
        where TStatus : struct, Enum
    {
        if (!map.TryGetValue(currentStatus, out var allowed)
            || !allowed.Contains(targetStatus))
        {
            throw new OrderTransitionException(
                $"Không thể chuyển trạng thái {category} từ {currentStatus} sang {targetStatus}.");
        }
    }

    private static void ValidateOrderInvariant(Order order, OrderStatus targetStatus)
    {
        if (targetStatus == OrderStatus.Cancelled)
        {
            throw new OrderTransitionException(
                "Hủy đơn phải đi qua cancellation workflow để hoàn kho và xử lý thanh toán an toàn.");
        }

        if (targetStatus == OrderStatus.Placed
            && order.PaymentStatus is not PaymentStatus.Paid and not PaymentStatus.CodPending)
        {
            throw new OrderTransitionException(
                "Chỉ có thể ghi nhận đơn đã đặt khi thanh toán hợp lệ hoặc đang chờ COD.");
        }

        if (targetStatus is OrderStatus.Confirmed or OrderStatus.Processing
            && order.PaymentStatus != PaymentStatus.Paid
            && order.PaymentStatus != PaymentStatus.CodPending)
        {
            throw new OrderTransitionException(
                "Không thể xử lý đơn chưa có trạng thái thanh toán hợp lệ.");
        }

        if (targetStatus == OrderStatus.Completed
            && (order.PaymentStatus != PaymentStatus.Paid
                || order.FulfillmentStatus != FulfillmentStatus.Delivered))
        {
            throw new OrderTransitionException(
                "Chỉ có thể hoàn tất đơn đã thanh toán và giao thành công.");
        }
    }

    private static void ValidatePaymentInvariant(
        Order order,
        PaymentTransaction transaction,
        PaymentStatus targetStatus)
    {
        if (targetStatus == PaymentStatus.Paid)
        {
            if (order.PaymentStatus != PaymentStatus.CodPending
                || !string.Equals(transaction.Method, "COD", StringComparison.OrdinalIgnoreCase))
            {
                throw new OrderTransitionException(
                    "Admin chỉ được xác nhận Paid thủ công cho giao dịch COD.");
            }

            if (order.FulfillmentStatus != FulfillmentStatus.Delivered)
            {
                throw new OrderTransitionException(
                    "COD chỉ được chuyển Paid sau khi đơn đã giao thành công.");
            }
        }

        if (targetStatus is PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new OrderTransitionException(
                "Hoàn tiền phải đi qua refund workflow để kiểm soát số tiền và idempotency.");
        }
    }

    private static void ValidateFulfillmentInvariant(
        Order order,
        Shipment shipment,
        FulfillmentStatus targetStatus)
    {
        if (order.OrderStatus != OrderStatus.Processing)
        {
            throw new OrderTransitionException(
                "Đơn phải ở trạng thái Processing trước khi cập nhật quá trình giao hàng.");
        }

        if (shipment.Status == ShipmentStatus.Cancelled)
        {
            throw new OrderTransitionException("Vận đơn hiện tại đã bị hủy.");
        }

        if (targetStatus == FulfillmentStatus.Shipped)
        {
            var hasProviderReference = !string.IsNullOrWhiteSpace(shipment.TrackingCode)
                || !string.IsNullOrWhiteSpace(shipment.ExternalOrderCode);
            var providerStatusReady = shipment.Status is ShipmentStatus.Created
                or ShipmentStatus.Picking
                or ShipmentStatus.InTransit;

            if (!hasProviderReference || !providerStatusReady)
            {
                throw new OrderTransitionException(
                    "Chưa thể bàn giao: vận đơn phải có mã provider/tracking và trạng thái hợp lệ.");
            }
        }

        if (targetStatus == FulfillmentStatus.Delivered
            && shipment.Status != ShipmentStatus.Delivered)
        {
            throw new OrderTransitionException(
                "Chỉ xác nhận Delivered khi shipment provider đã ở trạng thái Delivered.");
        }

        if (targetStatus == FulfillmentStatus.DeliveryFailed
            && shipment.Status != ShipmentStatus.DeliveryFailed)
        {
            throw new OrderTransitionException(
                "Chỉ xác nhận giao thất bại khi shipment provider đã báo DeliveryFailed.");
        }
    }

    private static PaymentTransaction GetLatestPayment(Order order)
    {
        return order.PaymentTransactions
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault()
            ?? throw new OrderTransitionException("Đơn hàng chưa có giao dịch thanh toán.");
    }

    private static Shipment GetLatestShipment(Order order)
    {
        return order.Shipments
            .OrderByDescending(item => item.Id)
            .FirstOrDefault()
            ?? throw new OrderTransitionException("Đơn hàng chưa có vận đơn.");
    }

    private static void MarkCodPaymentPaid(
        Order order,
        string actor,
        DateTime nowUtc)
    {
        var transaction = GetLatestPayment(order);
        if (!string.Equals(transaction.Method, "COD", StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderTransitionException(
                "PaymentStatus đang là CodPending nhưng giao dịch gần nhất không phải COD.");
        }

        var previousStatus = order.PaymentStatus;
        order.PaymentStatus = PaymentStatus.Paid;
        transaction.Status = PaymentStatus.Paid;
        transaction.CompletedAt ??= nowUtc;
        transaction.UpdatedAt = nowUtc;

        AddHistory(
            order,
            OrderHistoryCategory.Payment,
            previousStatus.ToString(),
            PaymentStatus.Paid.ToString(),
            "PAYMENT_COD_COLLECTED",
            "Đã thu tiền COD",
            "Hệ thống ghi nhận COD sau khi provider xác nhận giao hàng thành công.",
            actor,
            nowUtc,
            customerVisible: true);
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
            CorrelationId = Guid.NewGuid().ToString("N")
        });
    }

    private void ApplyOriginalRowVersion(Order order, byte[] rowVersion)
    {
        if (rowVersion is not { Length: > 0 })
        {
            throw new OrderTransitionException("RowVersion của đơn hàng là bắt buộc.");
        }

        _context.Entry(order)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;
    }

    private async Task SaveAggregateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new OrderConcurrencyException(
                "Đơn hàng đã được cập nhật bởi thao tác khác. Vui lòng tải lại dữ liệu.",
                exception);
        }
    }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OrderTransitionException($"{parameterName} không được để trống.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new OrderTransitionException(
                $"{parameterName} không được vượt {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string GetOrderTitle(OrderStatus status)
    {
        return status switch
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
    }

    private static string GetPaymentTitle(PaymentStatus status)
    {
        return status switch
        {
            PaymentStatus.Pending => "Chờ thanh toán",
            PaymentStatus.CodPending => "Chờ thu COD",
            PaymentStatus.Paid => "Đã thanh toán",
            PaymentStatus.Failed => "Thanh toán thất bại",
            PaymentStatus.Cancelled => "Thanh toán đã hủy",
            PaymentStatus.PartiallyRefunded => "Đã hoàn một phần",
            PaymentStatus.Refunded => "Đã hoàn tiền",
            _ => status.ToString()
        };
    }

    private static string GetFulfillmentTitle(FulfillmentStatus status)
    {
        return status switch
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
}
