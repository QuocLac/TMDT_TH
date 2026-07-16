using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WebApplication2.Services.Payments.VnPay;

public sealed class VnPayGateway : IVnPayGateway
{
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    private readonly VnPayOptions _options;

    public VnPayGateway(IOptions<VnPayOptions> options)
    {
        _options = options.Value;
    }

    public VnPayPaymentUrlResult CreatePaymentUrl(VnPayPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsConfigured)
        {
            return VnPayPaymentUrlResult.Failure(
                "VNPAY_NOT_CONFIGURED",
                "VNPay chưa được cấu hình đầy đủ trên server.");
        }

        if (string.IsNullOrWhiteSpace(request.MerchantReference)
            || request.MerchantReference.Length > 100)
        {
            return VnPayPaymentUrlResult.Failure(
                "VNPAY_INVALID_REFERENCE",
                "Mã tham chiếu thanh toán VNPay không hợp lệ.");
        }

        if (request.Amount <= 0m
            || request.Amount > long.MaxValue / 100m)
        {
            return VnPayPaymentUrlResult.Failure(
                "VNPAY_INVALID_AMOUNT",
                "Số tiền thanh toán VNPay không hợp lệ hoặc vượt giới hạn hỗ trợ.");
        }

        var createdAtUtc = DateTime.SpecifyKind(
            request.CreatedAtUtc,
            DateTimeKind.Utc);
        var expiresAtUtc = createdAtUtc.AddMinutes(
            _options.PaymentTimeoutMinutes);

        var createdAtLocal = TimeZoneInfo.ConvertTimeFromUtc(
            createdAtUtc,
            VietnamTimeZone);
        var expiresAtLocal = TimeZoneInfo.ConvertTimeFromUtc(
            expiresAtUtc,
            VietnamTimeZone);

        var parameters = new SortedDictionary<string, string>(
            StringComparer.Ordinal)
        {
            ["vnp_Version"] = _options.Version,
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = _options.TmnCode.Trim(),
            ["vnp_Amount"] = ToVnPayAmount(request.Amount),
            ["vnp_CreateDate"] = createdAtLocal.ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = NormalizeIpAddress(request.ClientIpAddress),
            ["vnp_Locale"] = _options.Locale,
            ["vnp_OrderInfo"] = Limit(
                request.OrderDescription,
                255),
            ["vnp_OrderType"] = Limit(_options.OrderType, 100),
            ["vnp_ReturnUrl"] = _options.ReturnUrl,
            ["vnp_TxnRef"] = request.MerchantReference.Trim(),
            ["vnp_ExpireDate"] = expiresAtLocal.ToString(
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture)
        };

        var signData = BuildEncodedQuery(parameters);
        var secureHash = ComputeHash(signData);

        var paymentUri = new Uri(
            new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            _options.PaymentPath.TrimStart('/'));

        var paymentUrl =
            paymentUri
            + "?"
            + signData
            + "&vnp_SecureHash="
            + secureHash;

        return new VnPayPaymentUrlResult(
            true,
            paymentUrl,
            JsonSerializer.Serialize(parameters),
            expiresAtUtc,
            null,
            "Đã tạo URL thanh toán VNPay.");
    }

    public VnPayCallbackData ParseCallback(
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var providerValues = parameters
            .Where(item =>
                item.Key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                item => item.Key,
                item => item.Value ?? string.Empty,
                StringComparer.Ordinal);

        var rawPayload = JsonSerializer.Serialize(providerValues);

        if (!providerValues.TryGetValue(
                "vnp_SecureHash",
                out var providedHash)
            || string.IsNullOrWhiteSpace(providedHash))
        {
            return InvalidCallback(
                rawPayload,
                "VNPAY_SIGNATURE_MISSING",
                "Phản hồi VNPay không có chữ ký.");
        }

        var signedValues = new SortedDictionary<string, string>(
            providerValues
                .Where(item =>
                    !string.Equals(
                        item.Key,
                        "vnp_SecureHash",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        item.Key,
                        "vnp_SecureHashType",
                        StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal),
            StringComparer.Ordinal);

        var expectedHash = ComputeHash(
            BuildEncodedQuery(signedValues));

        if (!FixedTimeEqualsHex(expectedHash, providedHash))
        {
            return InvalidCallback(
                rawPayload,
                "VNPAY_SIGNATURE_INVALID",
                "Chữ ký phản hồi VNPay không hợp lệ.");
        }

        if (!providerValues.TryGetValue("vnp_TmnCode", out var tmnCode)
            || !string.Equals(
                tmnCode,
                _options.TmnCode,
                StringComparison.Ordinal))
        {
            return InvalidCallback(
                rawPayload,
                "VNPAY_TMN_CODE_MISMATCH",
                "Mã website VNPay không khớp cấu hình.");
        }

        var merchantReference = Get(providerValues, "vnp_TxnRef");
        var responseCode = Get(providerValues, "vnp_ResponseCode");
        var transactionStatus = Get(providerValues, "vnp_TransactionStatus");

        if (string.IsNullOrWhiteSpace(merchantReference)
            || string.IsNullOrWhiteSpace(responseCode)
            || string.IsNullOrWhiteSpace(transactionStatus)
            || !long.TryParse(
                Get(providerValues, "vnp_Amount"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var providerAmount)
            || providerAmount < 0)
        {
            return InvalidCallback(
                rawPayload,
                "VNPAY_CALLBACK_INVALID",
                "Phản hồi VNPay thiếu dữ liệu bắt buộc.");
        }

        return new VnPayCallbackData(
            true,
            merchantReference,
            providerAmount / 100m,
            responseCode,
            transactionStatus,
            NullIfEmpty(Get(providerValues, "vnp_TransactionNo")),
            NullIfEmpty(Get(providerValues, "vnp_BankCode")),
            NullIfEmpty(Get(providerValues, "vnp_CardType")),
            ParsePayDate(Get(providerValues, "vnp_PayDate")),
            rawPayload,
            null,
            responseCode == "00" && transactionStatus == "00"
                ? "Giao dịch VNPay thành công."
                : "Giao dịch VNPay chưa thành công.");
    }

    private VnPayCallbackData InvalidCallback(
        string rawPayload,
        string errorCode,
        string message)
    {
        return new VnPayCallbackData(
            false,
            string.Empty,
            0m,
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            null,
            rawPayload,
            errorCode,
            message);
    }

    private string ComputeHash(string value)
    {
        using var hmac = new HMACSHA512(
            Encoding.UTF8.GetBytes(_options.HashSecret.Trim()));

        return Convert
            .ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static string BuildEncodedQuery(
        IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return string.Join(
            "&",
            parameters.Select(item =>
                WebUtility.UrlEncode(item.Key)
                + "="
                + WebUtility.UrlEncode(item.Value)));
    }

    private static string ToVnPayAmount(decimal amount)
    {
        var multiplied = decimal.Round(
            amount * 100m,
            0,
            MidpointRounding.AwayFromZero);

        if (multiplied <= 0m || multiplied > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Số tiền vượt giới hạn VNPay.");
        }

        return decimal.ToInt64(multiplied)
            .ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeIpAddress(string? value)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 45)
        {
            return "127.0.0.1";
        }

        return normalized;
    }

    private static string Limit(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "Thanh toan don hang FastBuy"
            : value.Trim();

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static string Get(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        return values.TryGetValue(key, out var value)
            ? value
            : string.Empty;
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static DateTime? ParsePayDate(string value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTime))
        {
            return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            VietnamTimeZone);
    }

    private static bool FixedTimeEqualsHex(
        string expected,
        string provided)
    {
        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var providedBytes = Convert.FromHexString(provided);

            return expectedBytes.Length == providedBytes.Length
                && CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    providedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        foreach (var id in new[]
                 {
                     "Asia/Ho_Chi_Minh",
                     "SE Asia Standard Time"
                 })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
