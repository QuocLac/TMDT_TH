using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApplication2.Services.Shipping;

namespace WebApplication2.Services.Shipping.Ghn;

public sealed class GhnShippingClient : IGhnShippingClient
{
    private const string AvailableServicesEndpoint =
        "shiip/public-api/v2/shipping-order/available-services";
    private const string FeeEndpoint =
        "shiip/public-api/v2/shipping-order/fee";
    private const string CreateEndpoint =
        "shiip/public-api/v2/shipping-order/create";
    private const string CancelEndpoint =
        "shiip/public-api/v2/switch-status/cancel";
    private const string DetailEndpoint =
        "shiip/public-api/v2/shipping-order/detail";

    private readonly HttpClient _httpClient;
    private readonly GhnShippingOptions _options;
    private readonly ILogger<GhnShippingClient> _logger;

    public GhnShippingClient(
        HttpClient httpClient,
        IOptions<GhnShippingOptions> options,
        ILogger<GhnShippingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetAvailableServicesAsync(
        int toDistrictId,
        CancellationToken cancellationToken)
    {
        if (toDistrictId <= 0)
        {
            return Task.FromResult(
                ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>.Failure(
                    "INVALID_DISTRICT",
                    "Mã quận/huyện nhận hàng không hợp lệ."));
        }

        return SendAsync(
            AvailableServicesEndpoint,
            new
            {
                shop_id = _options.ShopId,
                from_district = _options.FromDistrictId,
                to_district = toDistrictId
            },
            data => (IReadOnlyList<ShippingServiceOption>)(data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray()
                    .Select(item => new ShippingServiceOption(
                        ReadInt(item, "service_id"),
                        ReadInt(item, "service_type_id"),
                        ReadString(item, "short_name", "GHN")))
                    .Where(item => item.ServiceId > 0 && item.ServiceTypeId > 0)
                    .ToArray()
                : Array.Empty<ShippingServiceOption>()),
            cancellationToken);
    }

    public Task<ShippingOperationResult<ShippingQuote>> QuoteAsync(
        ShippingQuoteRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateParcel(
            request.ToDistrictId,
            request.ToWardCode,
            request.WeightGram,
            request.LengthCm,
            request.WidthCm,
            request.HeightCm);
        if (validation is not null)
        {
            return Task.FromResult(
                ShippingOperationResult<ShippingQuote>.Failure(
                    "INVALID_QUOTE",
                    validation));
        }

        return SendAsync(
            FeeEndpoint,
            new
            {
                service_id = request.ServiceId > 0 ? request.ServiceId : (int?)null,
                service_type_id = request.ServiceTypeId,
                from_district_id = request.FromDistrictId,
                from_ward_code = request.FromWardCode,
                to_district_id = request.ToDistrictId,
                to_ward_code = request.ToWardCode,
                length = request.LengthCm,
                width = request.WidthCm,
                height = request.HeightCm,
                weight = request.WeightGram,
                insurance_value = Math.Clamp(request.InsuranceValue, 0, 5_000_000),
                cod_value = Math.Clamp(request.CodAmount, 0, 10_000_000),
                items = request.Items.Select(ToProviderItem).ToArray()
            },
            data => new ShippingQuote(
                ReadDecimal(data, "total"),
                ReadDecimal(data, "service_fee"),
                ReadDecimal(data, "insurance_fee"),
                ReadDecimal(data, "cod_fee"),
                data.GetRawText()),
            cancellationToken);
    }

    public Task<ShippingOperationResult<ShippingCreateResult>> CreateAsync(
        ShippingCreateRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateParcel(
            request.ToDistrictId,
            request.ToWardCode,
            request.WeightGram,
            request.LengthCm,
            request.WidthCm,
            request.HeightCm);
        if (validation is not null)
        {
            return Task.FromResult(
                ShippingOperationResult<ShippingCreateResult>.Failure(
                    "INVALID_SHIPMENT",
                    validation));
        }

        if (string.IsNullOrWhiteSpace(request.ClientOrderCode)
            || string.IsNullOrWhiteSpace(request.RecipientName)
            || string.IsNullOrWhiteSpace(request.RecipientPhone)
            || string.IsNullOrWhiteSpace(request.RecipientAddress))
        {
            return Task.FromResult(
                ShippingOperationResult<ShippingCreateResult>.Failure(
                    "INVALID_RECIPIENT",
                    "Thông tin người nhận và mã đơn hàng là bắt buộc."));
        }

        return SendAsync(
            CreateEndpoint,
            new
            {
                payment_type_id = request.PaymentTypeId,
                note = request.Note,
                required_note = request.RequiredNote,
                from_name = _options.SenderName,
                from_phone = _options.SenderPhone,
                from_address = _options.SenderAddress,
                from_ward_code = _options.FromWardCode,
                from_district_id = _options.FromDistrictId,
                return_name = _options.SenderName,
                return_phone = _options.SenderPhone,
                return_address = _options.SenderAddress,
                return_ward_code = _options.FromWardCode,
                return_district_id = _options.FromDistrictId,
                client_order_code = request.ClientOrderCode,
                to_name = request.RecipientName,
                to_phone = request.RecipientPhone,
                to_address = request.RecipientAddress,
                to_ward_code = request.ToWardCode,
                to_district_id = request.ToDistrictId,
                cod_amount = Math.Clamp(request.CodAmount, 0, 50_000_000),
                content = $"Đơn hàng {request.ClientOrderCode}",
                weight = request.WeightGram,
                length = request.LengthCm,
                width = request.WidthCm,
                height = request.HeightCm,
                insurance_value = Math.Clamp(request.InsuranceValue, 0, 5_000_000),
                service_id = request.ServiceId,
                service_type_id = request.ServiceTypeId,
                items = request.Items.Select(ToProviderItem).ToArray()
            },
            data =>
            {
                var orderCode = ReadString(data, "order_code");
                return new ShippingCreateResult(
                    orderCode,
                    orderCode,
                    ReadDecimal(data, "total_fee"),
                    ReadOptionalDateTime(data, "expected_delivery_time"),
                    data.GetRawText());
            },
            cancellationToken);
    }

    public Task<ShippingOperationResult<bool>> CancelAsync(
        string externalOrderCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalOrderCode))
        {
            return Task.FromResult(
                ShippingOperationResult<bool>.Failure(
                    "INVALID_TRACKING_CODE",
                    "Mã vận đơn GHN là bắt buộc."));
        }

        return SendAsync(
            CancelEndpoint,
            new { order_codes = new[] { externalOrderCode.Trim() } },
            data => data.ValueKind == JsonValueKind.Array
                && data.EnumerateArray().Any(item =>
                    string.Equals(
                        ReadString(item, "order_code", string.Empty),
                        externalOrderCode,
                        StringComparison.OrdinalIgnoreCase)
                    && ReadBool(item, "result")),
            cancellationToken);
    }

    public Task<ShippingOperationResult<ShippingDetail>> GetDetailAsync(
        string externalOrderCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalOrderCode))
        {
            return Task.FromResult(
                ShippingOperationResult<ShippingDetail>.Failure(
                    "INVALID_TRACKING_CODE",
                    "Mã vận đơn GHN là bắt buộc."));
        }

        return SendAsync(
            DetailEndpoint,
            new { order_code = externalOrderCode.Trim() },
            data =>
            {
                if (data.ValueKind == JsonValueKind.Array)
                {
                    data = data.EnumerateArray().FirstOrDefault();
                }

                var providerStatus = ReadString(data, "status", "exception");
                var code = ReadString(data, "order_code", externalOrderCode);
                var totalFee = ReadOptionalDecimal(data, "total_fee")
                    ?? ReadOptionalDecimal(data, "service_fee")
                    ?? 0m;

                return new ShippingDetail(
                    code,
                    providerStatus,
                    GhnShipmentStatusMapper.Map(providerStatus),
                    totalFee,
                    ReadOptionalDecimal(data, "cod_amount") ?? 0m,
                    ReadOptionalInt(data, "weight") ?? 0,
                    ReadOptionalInt(data, "length") ?? 0,
                    ReadOptionalInt(data, "width") ?? 0,
                    ReadOptionalInt(data, "height") ?? 0,
                    ReadOptionalDateTime(data, "leadtime"),
                    ReadOptionalDateTime(data, "updated_date"),
                    ReadOptionalString(data, "shipper_name"),
                    ReadOptionalString(data, "shipper_phone"),
                    ReadOptionalString(data, "current_warehouse_name")
                        ?? ReadOptionalString(data, "warehouse"),
                    ReadOptionalString(data, "reason")
                        ?? ReadOptionalString(data, "reason_code"),
                    data.GetRawText());
            },
            cancellationToken);
    }

    private async Task<ShippingOperationResult<T>> SendAsync<T>(
        string endpoint,
        object payload,
        Func<JsonElement, T> parser,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return ShippingOperationResult<T>.Failure(
                "GHN_NOT_CONFIGURED",
                "GHN Shipping chưa được cấu hình đầy đủ trên server.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("Token", _options.Token);
        request.Headers.TryAddWithoutValidation("ShopId", _options.ShopId.ToString(CultureInfo.InvariantCulture));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(raw);
            }
            catch (JsonException exception)
            {
                _logger.LogError(exception, "GHN endpoint {Endpoint} returned invalid JSON.", endpoint);
                return ShippingOperationResult<T>.Failure(
                    "GHN_INVALID_RESPONSE",
                    "GHN trả về dữ liệu không hợp lệ.",
                    retryable: (int)response.StatusCode >= 500);
            }

            using (document)
            {
                var root = document.RootElement;
                var providerCode = ReadOptionalInt(root, "code") ?? (int)response.StatusCode;
                var providerMessage = ReadOptionalString(root, "message") ?? "GHN từ chối yêu cầu.";

                if (!response.IsSuccessStatusCode || providerCode != 200)
                {
                    var retryable = response.StatusCode is HttpStatusCode.RequestTimeout
                        or HttpStatusCode.TooManyRequests
                        || (int)response.StatusCode >= 500;
                    return ShippingOperationResult<T>.Failure(
                        $"GHN_{providerCode}",
                        providerMessage,
                        retryable);
                }

                if (!root.TryGetProperty("data", out var data)
                    || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return ShippingOperationResult<T>.Failure(
                        "GHN_EMPTY_DATA",
                        "GHN không trả về dữ liệu cần thiết.");
                }

                try
                {
                    return ShippingOperationResult<T>.Ok(parser(data));
                }
                catch (Exception exception) when (exception is FormatException or InvalidOperationException)
                {
                    _logger.LogError(exception, "GHN endpoint {Endpoint} returned unexpected data.", endpoint);
                    return ShippingOperationResult<T>.Failure(
                        "GHN_INVALID_DATA",
                        "Không thể đọc dữ liệu do GHN trả về.");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ShippingOperationResult<T>.Failure(
                "GHN_TIMEOUT",
                "GHN phản hồi quá chậm.",
                retryable: true);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "GHN endpoint {Endpoint} is unavailable.", endpoint);
            return ShippingOperationResult<T>.Failure(
                "GHN_UNAVAILABLE",
                "Không thể kết nối tới GHN.",
                retryable: true);
        }
    }

    private static object ToProviderItem(ShippingParcelItem item) => new
    {
        name = item.Name,
        code = item.Code,
        quantity = item.Quantity,
        price = item.Price,
        weight = item.WeightGram,
        length = item.LengthCm,
        width = item.WidthCm,
        height = item.HeightCm
    };

    private static string? ValidateParcel(
        int districtId,
        string wardCode,
        int weight,
        int length,
        int width,
        int height)
    {
        if (districtId <= 0 || string.IsNullOrWhiteSpace(wardCode))
        {
            return "Mã quận/huyện và phường/xã nhận hàng là bắt buộc.";
        }

        if (weight is <= 0 or > 50_000)
        {
            return "Khối lượng vận đơn phải từ 1 đến 50.000 gram.";
        }

        if (length is <= 0 or > 200
            || width is <= 0 or > 200
            || height is <= 0 or > 200)
        {
            return "Mỗi kích thước kiện hàng phải từ 1 đến 200 cm.";
        }

        return null;
    }

    private static string ReadString(JsonElement element, string name, string? fallback = null)
    {
        var value = ReadOptionalString(element, name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return fallback ?? throw new FormatException($"GHN field {name} is missing.");
    }

    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int ReadInt(JsonElement element, string name) =>
        ReadOptionalInt(element, name)
        ?? throw new FormatException($"GHN field {name} is missing.");

    private static int? ReadOptionalInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static decimal ReadDecimal(JsonElement element, string name) =>
        ReadOptionalDecimal(element, name) ?? 0m;

    private static decimal? ReadOptionalDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var result) && result,
            _ => false
        };
    }

    private static DateTime? ReadOptionalDateTime(JsonElement element, string name)
    {
        var raw = ReadOptionalString(element, name);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }
}
