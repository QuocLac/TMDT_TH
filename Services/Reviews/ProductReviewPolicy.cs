using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Reviews;

public static class ProductReviewPolicy
{
    public const int ReviewWindowDays = 60;
    public const int MinimumContentLength = 20;
    public const int MaximumContentLength = 2000;
    public const int MaximumTitleLength = 120;
    public const int MaximumMediaCount = 5;
    public const int MaximumReplyLength = 1000;

    public static readonly ReturnRequestStatus[] BlockingReturnStatuses =
    [
        ReturnRequestStatus.Requested,
        ReturnRequestStatus.UnderReview,
        ReturnRequestStatus.Approved,
        ReturnRequestStatus.AwaitingReturnShipment,
        ReturnRequestStatus.AwaitingPickup,
        ReturnRequestStatus.ReturnInTransit,
        ReturnRequestStatus.ReceivedAtWarehouse,
        ReturnRequestStatus.Inspecting,
        ReturnRequestStatus.RefundPending
    ];

    public static DateTime GetDeadlineUtc(DateTime deliveredAt)
    {
        var utc = deliveredAt.Kind switch
        {
            DateTimeKind.Utc => deliveredAt,
            DateTimeKind.Local => deliveredAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(deliveredAt, DateTimeKind.Utc)
        };

        return utc.AddDays(ReviewWindowDays);
    }

    public static bool IsWithinWindow(DateTime deliveredAt, DateTime nowUtc) =>
        NormalizeUtc(nowUtc) <= GetDeadlineUtc(deliveredAt);

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
