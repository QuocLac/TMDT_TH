namespace WebApplication2.Services.Shipping.Ghn;

public sealed record GhnProvinceOption(
    int ProvinceId,
    string ProvinceName,
    string? Code,
    bool IsEnabled);

public sealed record GhnDistrictOption(
    int DistrictId,
    int ProvinceId,
    string DistrictName,
    int SupportType,
    bool IsEnabled);

public sealed record GhnWardOption(
    string WardCode,
    int DistrictId,
    string WardName,
    bool IsEnabled);

public sealed class GhnLookupResult<T>
{
    public bool Success { get; init; }

    public T? Data { get; init; }

    public string ErrorCode { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public int? ProviderCode { get; init; }

    public static GhnLookupResult<T> Ok(T data)
    {
        return new GhnLookupResult<T>
        {
            Success = true,
            Data = data
        };
    }

    public static GhnLookupResult<T> Failure(
        string errorCode,
        string message,
        int? providerCode = null)
    {
        return new GhnLookupResult<T>
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            ProviderCode = providerCode
        };
    }
}

public sealed class GhnAddressValidationResult
{
    public bool IsValid { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public GhnProvinceOption? Province { get; init; }

    public GhnDistrictOption? District { get; init; }

    public GhnWardOption? Ward { get; init; }

    public static GhnAddressValidationResult Valid(
        GhnProvinceOption province,
        GhnDistrictOption district,
        GhnWardOption ward)
    {
        return new GhnAddressValidationResult
        {
            IsValid = true,
            Province = province,
            District = district,
            Ward = ward
        };
    }

    public static GhnAddressValidationResult Invalid(
        string errorCode,
        string errorMessage)
    {
        return new GhnAddressValidationResult
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
