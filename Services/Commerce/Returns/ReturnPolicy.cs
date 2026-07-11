using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Returns;

public static class ReturnPolicy
{
    public const int ReturnWindowDays = 7;

    public static readonly ReturnRequestStatus[] QuantityReservingStatuses =
    [
        ReturnRequestStatus.Requested,
        ReturnRequestStatus.UnderReview,
        ReturnRequestStatus.Approved,
        ReturnRequestStatus.AwaitingReturnShipment,
        ReturnRequestStatus.AwaitingPickup,
        ReturnRequestStatus.ReturnInTransit,
        ReturnRequestStatus.ReceivedAtWarehouse,
        ReturnRequestStatus.Inspecting,
        ReturnRequestStatus.RefundPending,
        ReturnRequestStatus.RejectedAfterInspection,
        ReturnRequestStatus.Refunded,
        ReturnRequestStatus.Closed
    ];

    public static DateTime GetDeadlineUtc(DateTime deliveredAt)
    {
        var deliveredAtUtc = deliveredAt.Kind switch
        {
            DateTimeKind.Utc => deliveredAt,
            DateTimeKind.Local => deliveredAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(deliveredAt, DateTimeKind.Utc)
        };

        return deliveredAtUtc.AddDays(ReturnWindowDays);
    }

    public static bool IsWithinWindow(DateTime deliveredAt, DateTime nowUtc) =>
        NormalizeUtc(nowUtc) <= GetDeadlineUtc(deliveredAt);

    public static bool ReservesQuantity(ReturnRequestStatus status) =>
        QuantityReservingStatuses.Contains(status);

    public static Shipment? GetDeliveredOutboundShipment(Order order) =>
        order.Shipments
            .Where(item =>
                item.Direction == ShipmentDirection.Outbound
                && item.Status == ShipmentStatus.Delivered
                && item.DeliveredAt.HasValue)
            .OrderByDescending(item => item.DeliveredAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
