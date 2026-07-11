using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Orders;

public sealed class OrderWorkflowService : IOrderWorkflowService
{
    private const int ActorMaxLength = 100;
    private const int ReasonMaxLength = 500;

    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedTransitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.PendingPayment] = [OrderStatus.Placed, OrderStatus.Cancelled],
            [OrderStatus.Placed] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
            [OrderStatus.Processing] = [OrderStatus.Completed, OrderStatus.Cancelled],
            [OrderStatus.Completed] = [OrderStatus.Closed],
            [OrderStatus.Cancelled] = [OrderStatus.Closed],
            [OrderStatus.Closed] = []
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

    public async Task<Order> TransitionAsync(
        int orderId,
        OrderStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            throw new OrderTransitionException("Mã đơn hàng không hợp lệ.");
        }

        if (rowVersion is not { Length: > 0 })
        {
            throw new OrderTransitionException("RowVersion của đơn hàng là bắt buộc.");
        }

        var normalizedActor = Normalize(actor, ActorMaxLength, nameof(actor));
        var normalizedReason = NormalizeOptional(reason, ReasonMaxLength);

        var order = await _context.Orders
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new OrderTransitionException($"Không tìm thấy đơn hàng {orderId}.");

        if (order.OrderStatus == targetStatus)
        {
            return order;
        }

        if (!AllowedTransitions.TryGetValue(order.OrderStatus, out var allowed)
            || !allowed.Contains(targetStatus))
        {
            throw new OrderTransitionException(
                $"Không thể chuyển đơn hàng từ {order.OrderStatus} sang {targetStatus}.");
        }

        ValidateCommercialInvariant(order, targetStatus);

        _context.Entry(order)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;

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
            case OrderStatus.Cancelled:
                order.CancelledAt ??= nowUtc;
                order.CancelReason = normalizedReason;
                break;
        }

        _context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Category = OrderHistoryCategory.Order,
            FromStatus = previousStatus.ToString(),
            ToStatus = targetStatus.ToString(),
            Code = $"ORDER_{targetStatus.ToString().ToUpperInvariant()}",
            Title = GetTitle(targetStatus),
            Description = normalizedReason,
            ChangedBy = normalizedActor,
            CustomerVisible = true,
            OccurredAt = nowUtc
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return order;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new OrderConcurrencyException(
                "Đơn hàng đã được cập nhật bởi một thao tác khác. Vui lòng tải lại dữ liệu.",
                exception);
        }
    }

    private static void ValidateCommercialInvariant(Order order, OrderStatus targetStatus)
    {
        if (targetStatus == OrderStatus.Completed
            && (order.PaymentStatus != PaymentStatus.Paid
                || order.FulfillmentStatus != FulfillmentStatus.Delivered))
        {
            throw new OrderTransitionException(
                "Chỉ có thể hoàn tất đơn đã thanh toán và giao thành công.");
        }

        if (targetStatus == OrderStatus.Cancelled
            && order.FulfillmentStatus is FulfillmentStatus.Shipped
                or FulfillmentStatus.Delivered
                or FulfillmentStatus.Returning
                or FulfillmentStatus.Returned)
        {
            throw new OrderTransitionException(
                "Đơn đã bàn giao vận chuyển không thể hủy theo luồng trước giao hàng.");
        }

        if ((targetStatus is OrderStatus.Placed or OrderStatus.Confirmed or OrderStatus.Processing)
            && (order.PaymentStatus is PaymentStatus.Failed or PaymentStatus.Cancelled))
        {
            throw new OrderTransitionException(
                "Không thể xử lý đơn có giao dịch thanh toán thất bại hoặc đã hủy.");
        }
    }

    private static string GetTitle(OrderStatus status)
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

    private static string Normalize(string value, int maxLength, string parameterName)
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

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new OrderTransitionException(
                $"reason không được vượt {maxLength} ký tự.");
        }

        return normalized;
    }
}
