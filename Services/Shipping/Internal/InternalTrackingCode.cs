using WebApplication2.Models;

namespace WebApplication2.Services.Shipping.Internal;

public static class InternalTrackingCode
{
    public static string ForOutbound(Shipment shipment, DateTime fallbackUtc)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (shipment.Id <= 0)
        {
            throw new InvalidOperationException(
                "Hồ sơ giao hàng chưa được lưu nên chưa thể cấp mã theo dõi.");
        }

        var date = shipment.CreatedAt == default
            ? fallbackUtc
            : shipment.CreatedAt;

        return $"FB{date:yyMMdd}{shipment.Id:D8}";
    }

    public static string ForReturn(ReturnRequest request, DateTime fallbackUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Id <= 0)
        {
            throw new InvalidOperationException(
                "Yêu cầu hoàn trả chưa được lưu nên chưa thể cấp mã tiếp nhận.");
        }

        var date = request.RequestedAt == default
            ? fallbackUtc
            : request.RequestedAt;

        return $"FBR{date:yyMMdd}{request.Id:D8}";
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var digits = normalized.StartsWith(
            "FBR",
            StringComparison.OrdinalIgnoreCase)
                ? normalized[3..]
                : normalized.StartsWith(
                    "FB",
                    StringComparison.OrdinalIgnoreCase)
                    ? normalized[2..]
                    : string.Empty;

        return digits.Length == 14
            && digits.All(char.IsDigit);
    }
}
