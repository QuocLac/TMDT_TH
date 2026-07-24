using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Shipping.Internal;

public sealed record InternalShippingTransitionResult(
    long ShipmentId,
    ShipmentStatus Status,
    FulfillmentStatus FulfillmentStatus,
    string? TrackingCode,
    string Message);

public interface IInternalShippingLifecycleService
{
    IReadOnlyList<ShipmentStatus> GetAllowedTargets(ShipmentStatus current);

    Task<InternalShippingTransitionResult> TransitionAsync(
        long shipmentId,
        ShipmentStatus target,
        byte[] rowVersion,
        string actor,
        string? note,
        CancellationToken cancellationToken);
}

public sealed class InternalShippingLifecycleService
    : IInternalShippingLifecycleService
{
    private static readonly IReadOnlyDictionary<ShipmentStatus, ShipmentStatus[]>
        AllowedTransitions = new Dictionary<ShipmentStatus, ShipmentStatus[]>
        {
            [ShipmentStatus.Draft] =
            [
                ShipmentStatus.Picking
            ],
            [ShipmentStatus.PendingCreation] =
            [
                ShipmentStatus.Picking
            ],
            [ShipmentStatus.Picking] =
            [
                ShipmentStatus.Created
            ],
            [ShipmentStatus.Created] =
            [
                ShipmentStatus.InTransit
            ],
            [ShipmentStatus.InTransit] =
            [
                ShipmentStatus.Delivered,
                ShipmentStatus.DeliveryFailed
            ],
            [ShipmentStatus.DeliveryFailed] =
            [
                ShipmentStatus.InTransit,
                ShipmentStatus.Returning
            ],
            [ShipmentStatus.Returning] =
            [
                ShipmentStatus.Returned
            ],
            [ShipmentStatus.Exception] =
            [
                ShipmentStatus.Picking
            ]
        };

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public InternalShippingLifecycleService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<ShipmentStatus> GetAllowedTargets(
        ShipmentStatus current) =>
        AllowedTransitions.TryGetValue(current, out var targets)
            ? targets
            : [];

    public async Task<InternalShippingTransitionResult> TransitionAsync(
        long shipmentId,
        ShipmentStatus target,
        byte[] rowVersion,
        string actor,
        string? note,
        CancellationToken cancellationToken)
    {
        if (shipmentId <= 0)
        {
            throw new InvalidOperationException(
                "Mã hồ sơ giao hàng không hợp lệ.");
        }

        actor = NormalizeActor(actor);
        note = NormalizeNote(note);

        var shipment = await _context.Shipments
            .AsSplitQuery()
            .Include(item => item.Order)
                .ThenInclude(order => order.PaymentTransactions)
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .SingleOrDefaultAsync(
                item => item.Id == shipmentId
                    && item.Direction == ShipmentDirection.Outbound,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Không tìm thấy hồ sơ giao hàng.");

        EnsureOrderMayEnterDelivery(shipment.Order, shipment.Status, target);

        var allowedTargets = GetAllowedTargets(shipment.Status);
        if (!allowedTargets.Contains(target))
        {
            throw new InvalidOperationException(
                "Không thể chuyển sang trạng thái đã chọn từ bước hiện tại.");
        }

        if (rowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Dữ liệu vừa thay đổi. Vui lòng tải lại trang.");
        }

        _context.Entry(shipment)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var previousStatus = shipment.Status;
        var previousFulfillment = shipment.Order.FulfillmentStatus;
        var previousPayment = shipment.Order.PaymentStatus;

        var replaceLegacyTrackingCode =
            !string.Equals(
                shipment.Provider,
                "FastBuy",
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(shipment.ExternalOrderCode);

        NormalizeInternalShipment(shipment);

        if (replaceLegacyTrackingCode)
        {
            shipment.TrackingCode = null;
        }

        shipment.Status = target;
        shipment.UpdatedAt = nowUtc;
        shipment.ProviderUpdatedAt = nowUtc;
        shipment.ProviderReason = note;

        if (RequiresTrackingCode(target)
            && !InternalTrackingCode.IsValid(shipment.TrackingCode))
        {
            shipment.TrackingCode =
                InternalTrackingCode.ForOutbound(shipment, nowUtc);
        }

        ApplyShipmentTimestamps(shipment, target, nowUtc);
        ApplyOrderState(shipment.Order, target, nowUtc);
        AddFulfillmentHistory(
            shipment.Order,
            shipment,
            previousStatus,
            target,
            previousFulfillment,
            actor,
            note,
            nowUtc);

        if (previousPayment != shipment.Order.PaymentStatus)
        {
            AddPaymentHistory(
                shipment.Order,
                previousPayment,
                shipment.Order.PaymentStatus,
                actor,
                nowUtc);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new InternalShippingTransitionResult(
            shipment.Id,
            shipment.Status,
            shipment.Order.FulfillmentStatus,
            shipment.TrackingCode,
            BuildSuccessMessage(target, shipment.TrackingCode));
    }

    private static void EnsureOrderMayEnterDelivery(
        Order order,
        ShipmentStatus current,
        ShipmentStatus target)
    {
        if (order.PaymentStatus is not (
            PaymentStatus.Paid or PaymentStatus.CodPending))
        {
            throw new InvalidOperationException(
                "Đơn hàng chưa đủ điều kiện thanh toán để tiếp tục giao.");
        }

        if (current is ShipmentStatus.Draft or ShipmentStatus.PendingCreation
            && target == ShipmentStatus.Picking
            && order.OrderStatus is not (
                OrderStatus.Confirmed or OrderStatus.Processing))
        {
            throw new InvalidOperationException(
                "Hãy xác nhận đơn hàng trước khi bắt đầu chuẩn bị giao.");
        }

        if (order.OrderStatus is OrderStatus.Cancelled or OrderStatus.Closed)
        {
            throw new InvalidOperationException(
                "Đơn hàng đã kết thúc nên không thể cập nhật giao hàng.");
        }
    }

    private static void NormalizeInternalShipment(Shipment shipment)
    {
        shipment.Provider = "FastBuy";
        shipment.ProviderStatus = null;
        shipment.ExternalOrderCode = null;
        shipment.LastSyncedAt = null;
        shipment.ShipperName = null;
        shipment.ShipperPhone = null;
        shipment.CurrentHub = null;

        if (string.IsNullOrWhiteSpace(shipment.ServiceName)
            || shipment.ServiceName.Contains(
                "GHN",
                StringComparison.OrdinalIgnoreCase))
        {
            shipment.ServiceName = "Giao hàng nhanh";
        }
    }

    private static bool RequiresTrackingCode(ShipmentStatus target) =>
        target is ShipmentStatus.Created
            or ShipmentStatus.InTransit
            or ShipmentStatus.Delivered
            or ShipmentStatus.DeliveryFailed
            or ShipmentStatus.Returning
            or ShipmentStatus.Returned;

    private static void ApplyShipmentTimestamps(
        Shipment shipment,
        ShipmentStatus target,
        DateTime nowUtc)
    {
        switch (target)
        {
            case ShipmentStatus.Created:
                shipment.EstimatedDeliveryAt ??= nowUtc.AddDays(2);
                break;
            case ShipmentStatus.InTransit:
                shipment.CarrierHandoffAt ??= nowUtc;
                shipment.EstimatedDeliveryAt ??= nowUtc.AddDays(2);
                shipment.DeliveredAt = null;
                break;
            case ShipmentStatus.Delivered:
                shipment.CarrierHandoffAt ??= nowUtc;
                shipment.DeliveredAt = nowUtc;
                break;
            case ShipmentStatus.DeliveryFailed:
                shipment.DeliveredAt = null;
                shipment.ProviderReason ??=
                    "Chưa thể giao hàng. Đơn sẽ được sắp xếp giao lại.";
                break;
            case ShipmentStatus.Returning:
                shipment.DeliveredAt = null;
                shipment.ProviderReason ??=
                    "Đơn đang được đưa về điểm tiếp nhận của FastBuy.";
                break;
            case ShipmentStatus.Returned:
                shipment.DeliveredAt = null;
                break;
        }
    }

    private static void ApplyOrderState(
        Order order,
        ShipmentStatus target,
        DateTime nowUtc)
    {
        switch (target)
        {
            case ShipmentStatus.Picking:
                order.OrderStatus = OrderStatus.Processing;
                order.FulfillmentStatus = FulfillmentStatus.Preparing;
                break;
            case ShipmentStatus.Created:
                order.OrderStatus = OrderStatus.Processing;
                order.FulfillmentStatus = FulfillmentStatus.ReadyToShip;
                break;
            case ShipmentStatus.InTransit:
                order.OrderStatus = OrderStatus.Processing;
                order.FulfillmentStatus = FulfillmentStatus.Shipped;
                break;
            case ShipmentStatus.Delivered:
                order.OrderStatus = OrderStatus.Completed;
                order.FulfillmentStatus = FulfillmentStatus.Delivered;
                order.CompletedAt = nowUtc;
                CompleteCodPayment(order, nowUtc);
                break;
            case ShipmentStatus.DeliveryFailed:
                order.OrderStatus = OrderStatus.Processing;
                order.FulfillmentStatus = FulfillmentStatus.DeliveryFailed;
                break;
            case ShipmentStatus.Returning:
                order.OrderStatus = OrderStatus.Processing;
                order.FulfillmentStatus = FulfillmentStatus.Returning;
                break;
            case ShipmentStatus.Returned:
                order.OrderStatus = OrderStatus.Closed;
                order.FulfillmentStatus = FulfillmentStatus.Returned;
                order.CompletedAt ??= nowUtc;
                CancelUncollectedCod(order, nowUtc);
                break;
        }

        order.UpdatedAt = nowUtc;
    }

    private static void CompleteCodPayment(Order order, DateTime nowUtc)
    {
        if (order.PaymentStatus != PaymentStatus.CodPending)
        {
            return;
        }

        order.PaymentStatus = PaymentStatus.Paid;
        var transaction = order.PaymentTransactions
            .Where(item => item.Status == PaymentStatus.CodPending)
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();

        if (transaction is not null)
        {
            transaction.Status = PaymentStatus.Paid;
            transaction.CompletedAt = nowUtc;
            transaction.UpdatedAt = nowUtc;
        }
    }

    private static void CancelUncollectedCod(Order order, DateTime nowUtc)
    {
        if (order.PaymentStatus != PaymentStatus.CodPending)
        {
            return;
        }

        order.PaymentStatus = PaymentStatus.Cancelled;
        foreach (var transaction in order.PaymentTransactions.Where(item =>
                     item.Status == PaymentStatus.CodPending))
        {
            transaction.Status = PaymentStatus.Cancelled;
            transaction.CompletedAt = nowUtc;
            transaction.UpdatedAt = nowUtc;
        }
    }

    private static void AddFulfillmentHistory(
        Order order,
        Shipment shipment,
        ShipmentStatus previousStatus,
        ShipmentStatus target,
        FulfillmentStatus previousFulfillment,
        string actor,
        string? note,
        DateTime nowUtc)
    {
        var description = string.IsNullOrWhiteSpace(note)
            ? StatusDescription(target)
            : note;

        if (target == ShipmentStatus.Created
            && !string.IsNullOrWhiteSpace(shipment.TrackingCode))
        {
            description = $"{description} Mã theo dõi: {shipment.TrackingCode}.";
        }

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = OrderHistoryCategory.Fulfillment,
            FromStatus = previousFulfillment.ToString(),
            ToStatus = order.FulfillmentStatus.ToString(),
            Code = $"DELIVERY_{target.ToString().ToUpperInvariant()}",
            Title = StatusTitle(target),
            Description = description,
            ChangedBy = actor,
            CustomerVisible = true,
            OccurredAt = nowUtc,
            CorrelationId =
                $"delivery:{order.Id}:{previousStatus}:{target}:{shipment.Id}"
        });
    }

    private static void AddPaymentHistory(
        Order order,
        PaymentStatus previous,
        PaymentStatus current,
        string actor,
        DateTime nowUtc)
    {
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = OrderHistoryCategory.Payment,
            FromStatus = previous.ToString(),
            ToStatus = current.ToString(),
            Code = current == PaymentStatus.Paid
                ? "COD_COLLECTED"
                : "COD_NOT_COLLECTED",
            Title = current == PaymentStatus.Paid
                ? "Đã ghi nhận thanh toán khi nhận hàng"
                : "Đã dừng thu tiền khi nhận hàng",
            Description = current == PaymentStatus.Paid
                ? "Khoản thanh toán khi nhận hàng đã được ghi nhận cùng thời điểm giao thành công."
                : "Đơn đã hoàn về nên khoản thanh toán khi nhận hàng không được thu.",
            ChangedBy = actor,
            CustomerVisible = true,
            OccurredAt = nowUtc,
            CorrelationId = $"delivery-payment:{order.Id}:{current}"
        });
    }

    public static string StatusTitle(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Draft => "Đã ghi nhận",
        ShipmentStatus.PendingCreation => "Đã ghi nhận",
        ShipmentStatus.Picking => "Đang chuẩn bị hàng",
        ShipmentStatus.Created => "Sẵn sàng giao",
        ShipmentStatus.InTransit => "Đang giao hàng",
        ShipmentStatus.Delivered => "Giao hàng thành công",
        ShipmentStatus.DeliveryFailed => "Giao hàng chưa thành công",
        ShipmentStatus.Returning => "Đang đưa hàng về",
        ShipmentStatus.Returned => "Đã tiếp nhận hàng hoàn",
        ShipmentStatus.Cancelled => "Đã dừng giao hàng",
        ShipmentStatus.Exception => "Cần kiểm tra",
        _ => "Cập nhật giao hàng"
    };

    public static string StatusDescription(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Picking =>
            "FastBuy đang kiểm tra và đóng gói sản phẩm.",
        ShipmentStatus.Created =>
            "Đơn đã sẵn sàng để bàn giao giao hàng.",
        ShipmentStatus.InTransit =>
            "Đơn đang trên đường đến địa chỉ nhận hàng.",
        ShipmentStatus.Delivered =>
            "Khách hàng đã nhận được đơn hàng.",
        ShipmentStatus.DeliveryFailed =>
            "Đơn chưa thể giao và sẽ được sắp xếp xử lý tiếp.",
        ShipmentStatus.Returning =>
            "Đơn đang được đưa về điểm tiếp nhận của FastBuy.",
        ShipmentStatus.Returned =>
            "Đơn đã được FastBuy tiếp nhận lại.",
        ShipmentStatus.Cancelled =>
            "Quy trình giao hàng đã được dừng.",
        _ => "Trạng thái giao hàng đã được cập nhật."
    };

    private static string BuildSuccessMessage(
        ShipmentStatus target,
        string? trackingCode)
    {
        var message = $"Đã cập nhật: {StatusTitle(target)}.";
        return target == ShipmentStatus.Created
            && !string.IsNullOrWhiteSpace(trackingCode)
                ? $"{message} Mã theo dõi {trackingCode} đã được cấp."
                : message;
    }

    private static string NormalizeActor(string? actor)
    {
        var normalized = string.IsNullOrWhiteSpace(actor)
            ? "Admin"
            : actor.Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static string? NormalizeNote(string? note)
    {
        var normalized = note?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
