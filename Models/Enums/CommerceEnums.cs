namespace WebApplication2.Models.Enums;

public enum OrderStatus
{
    PendingPayment,
    Placed,
    Confirmed,
    Processing,
    Completed,
    Cancelled,
    Closed
}

public enum PaymentStatus
{
    Pending,
    CodPending,
    Paid,
    Failed,
    Cancelled,
    PartiallyRefunded,
    Refunded
}

public enum FulfillmentStatus
{
    Unfulfilled,
    Preparing,
    ReadyToShip,
    Shipped,
    Delivered,
    DeliveryFailed,
    Returning,
    Returned,
    Cancelled
}

public enum ShipmentStatus
{
    Draft,
    PendingCreation,
    Created,
    Picking,
    InTransit,
    Delivered,
    DeliveryFailed,
    Returning,
    Returned,
    Cancelled,
    Exception
}

public enum StockReservationStatus
{
    Reserved,
    Committed,
    Released,
    Expired
}

public enum InventoryMovementType
{
    ReservationCreated,
    ReservationReleased,
    ReservationExpired,
    ManualIncrease,
    ManualDecrease,
    ReturnRestocked,
    ReturnWriteOff
}

public enum OrderHistoryCategory
{
    Order,
    Payment,
    Fulfillment,
    Inventory,
    Integration
}

public enum IntegrationEventStatus
{
    Received,
    Processing,
    Processed,
    Ignored,
    Failed
}

public enum IntegrationOutboxStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
