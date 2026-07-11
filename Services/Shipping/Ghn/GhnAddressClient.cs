using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace WebApplication2.Services.Shipping.Ghn;

public sealed class GhnAddressClient : IGhnAddressClient
{
    private const string ProvinceEndpoint = "shiip/public-api/master-data/province";
    private const string DistrictEndpoint = "shiip/public-api/master-data/district";
    private const string WardEndpoint = "shiip/public-api/master-data/ward";

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly GhnAddressOptions _options;
    private readonly ILogger<GhnAddressClient> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new();

    public GhnAddressClient(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<GhnAddressOptions> options,
        ILogger<GhnAddressClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public Task<GhnLookupResult<IReadOnlyList<GhnProvinceOption>>> GetProvincesAsync(
        CancellationToken cancellationToken)
    {
        return GetCachedListAsync(
            "ghn:address:provinces:v1",
            _options.ProvinceCacheMinutes,
            () => FetchListAsync(
                ProvinceEndpoint,
                HttpMethod.Get,
                payload: null,
                ParseProvince,
                cancellationToken),
            cancellationToken);
    }

    public Task<GhnLookupResult<IReadOnlyList<GhnDistrictOption>>> GetDistrictsAsync(
        int provinceId,
        CancellationToken cancellationToken)
    {
        if (provinceId <= 0)
        {
            return Task.FromResult(
                GhnLookupResult<IReadOnlyList<GhnDistrictOption>>.Failure(
                    "INVALID_PROVINCE_ID",
                    "Mã tỉnh/thành phố không hợp lệ."));
        }

        return GetCachedListAsync(
            $"ghn:address:districts:v1:{provinceId}",
            _options.DistrictCacheMinutes,
            () => FetchListAsync(
                DistrictEndpoint,
                HttpMethod.Get,
                new { province_id = provinceId },
                ParseDistrict,
                cancellationToken),
            cancellationToken);
    }

    public Task<GhnLookupResult<IReadOnlyList<GhnWardOption>>> GetWardsAsync(
        int districtId,
        CancellationToken cancellationToken)
    {
        if (districtId <= 0)
        {
            return Task.FromResult(
                GhnLookupResult<IReadOnlyList<GhnWardOption>>.Failure(
                    "INVALID_DISTRICT_ID",
                    "Mã quận/huyện không hợp lệ."));
        }

        return GetCachedListAsync(
            $"ghn:address:wards:v1:{districtId}",
            _options.WardCacheMinutes,
            () => FetchListAsync(
                WardEndpoint,
                HttpMethod.Post,
                new { district_id = districtId },
                ParseWard,
                cancellationToken),
            cancellationToken);
    }

    public async Task<GhnAddressValidationResult> ValidateAddressAsync(
        int provinceId,
        int districtId,
        string wardCode,
        CancellationToken cancellationToken)
    {
        if (provinceId <= 0 || districtId <= 0 || string.IsNullOrWhiteSpace(wardCode))
        {
            return GhnAddressValidationResult.Invalid(
                "INVALID_ADDRESS_CODE",
                "Tỉnh/thành phố, quận/huyện và phường/xã là bắt buộc.");
        }

        var normalizedWardCode = wardCode.Trim();
        if (normalizedWardCode.Length > 30
            || normalizedWardCode.Any(character => !char.IsLetterOrDigit(character)))
        {
            return GhnAddressValidationResult.Invalid(
                "INVALID_WARD_CODE",
                "Mã phường/xã không hợp lệ.");
        }

        var provinces = await GetProvincesAsync(cancellationToken);
        if (!provinces.Success || provinces.Data is null)
        {
            return GhnAddressValidationResult.Invalid(
                provinces.ErrorCode,
                provinces.Message);
        }

        var province = provinces.Data.SingleOrDefault(item => item.ProvinceId == provinceId);
        if (province is null || !province.IsEnabled)
        {
            return GhnAddressValidationResult.Invalid(
                "PROVINCE_NOT_FOUND",
                "Tỉnh/thành phố không tồn tại hoặc đang tạm ngưng trên GHN.");
        }

        var districts = await GetDistrictsAsync(provinceId, cancellationToken);
        if (!districts.Success || districts.Data is null)
        {
            return GhnAddressValidationResult.Invalid(
                districts.ErrorCode,
                districts.Message);
        }

        var district = districts.Data.SingleOrDefault(item => item.DistrictId == districtId);
        if (district is null
            || district.ProvinceId != provinceId
            || !district.IsEnabled)
        {
            return GhnAddressValidationResult.Invalid(
                "DISTRICT_PROVINCE_MISMATCH",
                "Quận/huyện không thuộc tỉnh/thành phố đã chọn hoặc đang tạm ngưng trên GHN.");
        }

        var wards = await GetWardsAsync(districtId, cancellationToken);
        if (!wards.Success || wards.Data is null)
        {
            return GhnAddressValidationResult.Invalid(
                wards.ErrorCode,
                wards.Message);
        }

        var ward = wards.Data.SingleOrDefault(item =>
            string.Equals(item.WardCode, normalizedWardCode, StringComparison.OrdinalIgnoreCase));

        if (ward is null || ward.DistrictId != districtId || !ward.IsEnabled)
        {
            return GhnAddressValidationResult.Invalid(
                "WARD_DISTRICT_MISMATCH",
                "Phường/xã không thuộc quận/huyện đã chọn hoặc đang tạm ngưng trên GHN.");
        }

        return GhnAddressValidationResult.Valid(province, district, ward);
    }

    private async Task<GhnLookupResult<IReadOnlyList<T>>> GetCachedListAsync<T>(
        string cacheKey,
        int cacheMinutes,
        Func<Task<GhnLookupResult<IReadOnlyList<T>>>> factory,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(
                cacheKey,
                out GhnLookupResult<IReadOnlyList<T>>? cached)
            && cached is not null)
        {
            return cached;
        }

        var gate = _cacheLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(
                    cacheKey,
                    out GhnLookupResult<IReadOnlyList<T>>? cachedAfterWait)
                && cachedAfterWait is not null)
            {
                return cachedAfterWait;
            }

            var result = await factory();
            if (result.Success && result.Data is not null)
            {
                var duration = TimeSpan.FromMinutes(Math.Max(5, cacheMinutes));
                _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = duration
                });
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GhnLookupResult<IReadOnlyList<T>>> FetchListAsync<T>(
        string endpoint,
        HttpMethod method,
        object? payload,
        Func<JsonElement, T> parser,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return GhnLookupResult<IReadOnlyList<T>>.Failure(
                "GHN_NOT_CONFIGURED",
                "Tích hợp địa chỉ GHN chưa được cấu hình token trên server.");
        }

        using var request = new HttpRequestMessage(method, endpoint);
        request.Headers.TryAddWithoutValidation("Token", _options.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GHN address endpoint {Endpoint} returned HTTP status {StatusCode}.",
                    endpoint,
                    (int)response.StatusCode);

                return GhnLookupResult<IReadOnlyList<T>>.Failure(
                    "GHN_HTTP_ERROR",
                    "Không thể tải dữ liệu địa chỉ từ GHN.");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var providerCode = ReadOptionalInt(root, "code") ?? 0;

            if (providerCode != 200)
            {
                _logger.LogWarning(
                    "GHN address endpoint {Endpoint} returned provider code {ProviderCode}.",
                    endpoint,
                    providerCode);

                return GhnLookupResult<IReadOnlyList<T>>.Failure(
                    "GHN_PROVIDER_ERROR",
                    "GHN không trả về dữ liệu địa chỉ hợp lệ.",
                    providerCode);
            }

            if (!root.TryGetProperty("data", out var data)
                || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return GhnLookupResult<IReadOnlyList<T>>.Ok(Array.Empty<T>());
            }

            var items = new List<T>();
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in data.EnumerateArray())
                {
                    items.Add(parser(element));
                }
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                items.Add(parser(data));
            }

            return GhnLookupResult<IReadOnlyList<T>>.Ok(items);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return GhnLookupResult<IReadOnlyList<T>>.Failure(
                "GHN_TIMEOUT",
                "GHN phản hồi quá chậm. Vui lòng thử lại.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "GHN address endpoint {Endpoint} was unavailable.", endpoint);
            return GhnLookupResult<IReadOnlyList<T>>.Failure(
                "GHN_UNAVAILABLE",
                "Không thể kết nối tới GHN lúc này.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "GHN address endpoint {Endpoint} returned invalid JSON.", endpoint);
            return GhnLookupResult<IReadOnlyList<T>>.Failure(
                "GHN_INVALID_RESPONSE",
                "GHN trả về dữ liệu địa chỉ không hợp lệ.");
        }
        catch (FormatException exception)
        {
            _logger.LogError(exception, "GHN address endpoint {Endpoint} returned malformed address data.", endpoint);
            return GhnLookupResult<IReadOnlyList<T>>.Failure(
                "GHN_INVALID_RESPONSE",
                "GHN trả về dữ liệu địa chỉ không hợp lệ.");
        }
    }

    private static GhnProvinceOption ParseProvince(JsonElement element)
    {
        return new GhnProvinceOption(
            ReadInt(element, "ProvinceID"),
            ReadString(element, "ProvinceName"),
            ReadOptionalString(element, "Code"),
            ReadIsEnabled(element));
    }

    private static GhnDistrictOption ParseDistrict(JsonElement element)
    {
        return new GhnDistrictOption(
            ReadInt(element, "DistrictID"),
            ReadInt(element, "ProvinceID"),
            ReadString(element, "DistrictName"),
            ReadOptionalInt(element, "SupportType") ?? 0,
            ReadIsEnabled(element));
    }

    private static GhnWardOption ParseWard(JsonElement element)
    {
        return new GhnWardOption(
            ReadString(element, "WardCode"),
            ReadInt(element, "DistrictID"),
            ReadString(element, "WardName"),
            ReadIsEnabled(element));
    }

    private static bool ReadIsEnabled(JsonElement element)
    {
        var status = ReadOptionalInt(element, "Status");
        var isEnable = ReadOptionalInt(element, "IsEnable");
        return status != 2 && isEnable != 0;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            throw new FormatException($"GHN field {propertyName} is missing.");
        }

        var result = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(result))
        {
            throw new FormatException($"GHN field {propertyName} is empty.");
        }

        return result!.Trim();
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        var value = ReadOptionalInt(element, propertyName);
        return value ?? throw new FormatException($"GHN field {propertyName} is invalid.");
    }

    private static int? ReadOptionalInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return null;
    }
}
