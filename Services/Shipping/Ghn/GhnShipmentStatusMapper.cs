using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Shipping.Ghn;

public static class GhnShipmentStatusMapper
{
    public static ShipmentStatus Map(string? providerStatus)
    {
        return providerStatus?.Trim().ToLowerInvariant() switch
        {
            "ready_to_pick" => ShipmentStatus.Created,
            "picking" or "money_collect_picking" => ShipmentStatus.Picking,
            "picked" or "storing" or "transporting" or "sorting"
                or "delivering" or "money_collect_delivering" => ShipmentStatus.InTransit,
            "delivered" => ShipmentStatus.Delivered,
            "delivery_fail" => ShipmentStatus.DeliveryFailed,
            "waiting_to_return" or "return" or "return_transporting"
                or "return_sorting" or "returning" => ShipmentStatus.Returning,
            "returned" => ShipmentStatus.Returned,
            "cancel" => ShipmentStatus.Cancelled,
            "return_fail" or "exception" or "damage" or "lost" => ShipmentStatus.Exception,
            _ => ShipmentStatus.Exception
        };
    }
}
