namespace WebApplication2.Models.Enums;

public enum ReturnRequestStatus
{
    Requested,
    UnderReview,
    Approved,
    Rejected,
    AwaitingReturnShipment,
    AwaitingPickup,
    ReturnInTransit,
    ReceivedAtWarehouse,
    Inspecting,
    RefundPending,
    RejectedAfterInspection,
    Refunded,
    Closed,
    Cancelled
}

public enum ReturnInspectionResult
{
    Accepted,
    PartiallyAccepted,
    Rejected
}

public enum ReturnItemCondition
{
    Restockable,
    Damaged,
    MissingParts,
    WrongItem,
    Mixed,
    WriteOff
}

public enum ReturnEvidenceType
{
    Image,
    Video
}
