namespace WebApplication2.Services.Commerce.Returns;

public static class ReturnPolicy
{
    public const int ReturnWindowDays = 7;

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
        nowUtc <= GetDeadlineUtc(deliveredAt);
}
