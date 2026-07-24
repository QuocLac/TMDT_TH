using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Shipping.Internal;

public enum InternalReturnShippingAction
{
    PreparePickup,
    MarkInTransit,
    MarkReceived
}

public sealed record InternalReturnShippingResult(
    long ReturnRequestId,
    ReturnRequestStatus Status,
    string TrackingCode,
    string Message);

public interface IInternalReturnShippingService
{
    Task<InternalReturnShippingResult> UpdateAsync(
        long returnRequestId,
        InternalReturnShippingAction action,
        byte[] rowVersion,
        string actor,
        string? note,
        CancellationToken cancellationToken);
}

public sealed class InternalReturnShippingService
    : IInternalReturnShippingService
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public InternalReturnShippingService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<InternalReturnShippingResult> UpdateAsync(
        long returnRequestId,
        InternalReturnShippingAction action,
        byte[] rowVersion,
        string actor,
        string? note,
        CancellationToken cancellationToken)
    {
        var request = await _context.ReturnRequests
            .AsSplitQuery()
            .Include(item => item.Order)
                .ThenInclude(order => order.Shipments)
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(
                item => item.Id == returnRequestId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Không tìm thấy yêu cầu hoàn trả.");

        if (rowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Dữ liệu vừa thay đổi. Vui lòng tải lại trang.");
        }

        _context.Entry(request)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var previousStatus = request.Status;
        var shipment = request.Shipments
            .Where(item => item.Direction == ShipmentDirection.Return)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();

        switch (action)
        {
            case InternalReturnShippingAction.PreparePickup:
                if (request.Status is not (
                    ReturnRequestStatus.Approved
                    or ReturnRequestStatus.AwaitingReturnShipment))
                {
                    throw new InvalidOperationException(
                        "Yêu cầu chưa sẵn sàng để tiếp nhận hàng hoàn.");
                }

                shipment ??= CreateReturnShipment(request, nowUtc);
                request.Status = ReturnRequestStatus.AwaitingPickup;
                shipment.Status = ShipmentStatus.Created;
                shipment.UpdatedAt = nowUtc;
                break;

            case InternalReturnShippingAction.MarkInTransit:
                if (request.Status != ReturnRequestStatus.AwaitingPickup)
                {
                    throw new InvalidOperationException(
                        "Hàng hoàn chưa ở bước chờ bàn giao.");
                }

                if (shipment is null)
                {
                    throw new InvalidOperationException(
                        "Chưa có hồ sơ vận chuyển hàng hoàn.");
                }

                request.Status = ReturnRequestStatus.ReturnInTransit;
                shipment.Status = ShipmentStatus.InTransit;
                shipment.CarrierHandoffAt ??= nowUtc;
                shipment.UpdatedAt = nowUtc;
                break;

            case InternalReturnShippingAction.MarkReceived:
                if (request.Status != ReturnRequestStatus.ReturnInTransit)
                {
                    throw new InvalidOperationException(
                        "Hàng hoàn chưa ở trạng thái đang vận chuyển.");
                }

                if (shipment is null)
                {
                    throw new InvalidOperationException(
                        "Chưa có hồ sơ vận chuyển hàng hoàn.");
                }

                request.Status = ReturnRequestStatus.ReceivedAtWarehouse;
                request.ReceivedAt = nowUtc;
                shipment.Status = ShipmentStatus.Delivered;
                shipment.DeliveredAt = nowUtc;
                shipment.UpdatedAt = nowUtc;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }

        request.UpdatedAt = nowUtc;
        if (!string.IsNullOrWhiteSpace(note))
        {
            shipment.ProviderReason = note.Trim();
        }

        request.Order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = OrderHistoryCategory.Return,
            FromStatus = previousStatus.ToString(),
            ToStatus = request.Status.ToString(),
            Code = $"RETURN_{action.ToString().ToUpperInvariant()}",
            Title = ActionTitle(action),
            Description = string.IsNullOrWhiteSpace(note)
                ? ActionDescription(action)
                : note.Trim(),
            ChangedBy = NormalizeActor(actor),
            CustomerVisible = true,
            OccurredAt = nowUtc,
            CorrelationId = $"return:shipping:{request.Id}:{action}"
        });

        shipment.TrackingCode ??=
            InternalTrackingCode.ForReturn(request, nowUtc);

        await _context.SaveChangesAsync(cancellationToken);

        return new InternalReturnShippingResult(
            request.Id,
            request.Status,
            shipment.TrackingCode,
            $"Đã cập nhật: {ActionTitle(action)}.");
    }

    private static Shipment CreateReturnShipment(
        ReturnRequest request,
        DateTime nowUtc)
    {
        var outbound = request.Order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Đơn hàng chưa có hồ sơ giao hàng ban đầu.");

        var shipment = new Shipment
        {
            OrderId = request.OrderId,
            Direction = ShipmentDirection.Return,
            ParentShipmentId = outbound.Id,
            ReturnRequestId = request.Id,
            Provider = "FastBuy",
            Status = ShipmentStatus.Created,
            ServiceCode = "RETURN",
            ServiceName = "Tiếp nhận hàng hoàn",
            TrackingCode = InternalTrackingCode.ForReturn(
                request,
                nowUtc),
            Fee = 0m,
            CodAmount = 0m,
            WeightGram = outbound.WeightGram,
            LengthCm = outbound.LengthCm,
            WidthCm = outbound.WidthCm,
            HeightCm = outbound.HeightCm,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };

        request.Shipments.Add(shipment);
        return shipment;
    }

    private static string ActionTitle(InternalReturnShippingAction action) =>
        action switch
        {
            InternalReturnShippingAction.PreparePickup =>
                "Đã sắp xếp tiếp nhận hàng hoàn",
            InternalReturnShippingAction.MarkInTransit =>
                "Hàng hoàn đang được vận chuyển",
            InternalReturnShippingAction.MarkReceived =>
                "FastBuy đã nhận hàng hoàn",
            _ => "Cập nhật hoàn trả"
        };

    private static string ActionDescription(InternalReturnShippingAction action) =>
        action switch
        {
            InternalReturnShippingAction.PreparePickup =>
                "Khách hàng có thể đóng gói toàn bộ sản phẩm để bàn giao.",
            InternalReturnShippingAction.MarkInTransit =>
                "Hàng hoàn đang trên đường về điểm tiếp nhận của FastBuy.",
            InternalReturnShippingAction.MarkReceived =>
                "Hàng hoàn đã được tiếp nhận và sẽ chuyển sang kiểm định.",
            _ => "Tiến trình hoàn trả đã được cập nhật."
        };

    private static string NormalizeActor(string? actor)
    {
        var value = string.IsNullOrWhiteSpace(actor) ? "Admin" : actor.Trim();
        return value.Length <= 100 ? value : value[..100];
    }
}
