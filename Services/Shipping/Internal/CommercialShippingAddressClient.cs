using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Services.Shipping.Internal;

public sealed class CommercialShippingAddressClient : IGhnAddressClient
{
    private readonly GhnAddressClient _inner;
    private readonly ILogger<CommercialShippingAddressClient> _logger;

    public CommercialShippingAddressClient(
        GhnAddressClient inner,
        ILogger<CommercialShippingAddressClient> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<GhnLookupResult<IReadOnlyList<GhnProvinceOption>>>
        GetProvincesAsync(CancellationToken cancellationToken)
    {
        var result = await _inner.GetProvincesAsync(cancellationToken);
        if (result.Success)
        {
            return result;
        }

        LogFailure("province", result.ErrorCode, result.Message);
        return GhnLookupResult<IReadOnlyList<GhnProvinceOption>>.Failure(
            "ADDRESS_PROVINCES_UNAVAILABLE",
            "Chưa thể tải danh sách tỉnh/thành. Vui lòng thử lại.");
    }

    public async Task<GhnLookupResult<IReadOnlyList<GhnDistrictOption>>>
        GetDistrictsAsync(
            int provinceId,
            CancellationToken cancellationToken)
    {
        var result = await _inner.GetDistrictsAsync(
            provinceId,
            cancellationToken);
        if (result.Success)
        {
            return result;
        }

        LogFailure("district", result.ErrorCode, result.Message);
        return GhnLookupResult<IReadOnlyList<GhnDistrictOption>>.Failure(
            "ADDRESS_DISTRICTS_UNAVAILABLE",
            "Chưa thể tải danh sách quận/huyện. Vui lòng thử lại.");
    }

    public async Task<GhnLookupResult<IReadOnlyList<GhnWardOption>>>
        GetWardsAsync(
            int districtId,
            CancellationToken cancellationToken)
    {
        var result = await _inner.GetWardsAsync(
            districtId,
            cancellationToken);
        if (result.Success)
        {
            return result;
        }

        LogFailure("ward", result.ErrorCode, result.Message);
        return GhnLookupResult<IReadOnlyList<GhnWardOption>>.Failure(
            "ADDRESS_WARDS_UNAVAILABLE",
            "Chưa thể tải danh sách phường/xã. Vui lòng thử lại.");
    }

    public async Task<GhnAddressValidationResult> ValidateAddressAsync(
        int provinceId,
        int districtId,
        string wardCode,
        CancellationToken cancellationToken)
    {
        var result = await _inner.ValidateAddressAsync(
            provinceId,
            districtId,
            wardCode,
            cancellationToken);
        if (result.IsValid)
        {
            return result;
        }

        LogFailure(
            "validation",
            result.ErrorCode,
            result.ErrorMessage);
        return GhnAddressValidationResult.Invalid(
            "ADDRESS_INVALID",
            "Địa chỉ nhận hàng chưa hợp lệ. Vui lòng chọn lại khu vực.");
    }

    private void LogFailure(
        string operation,
        string? errorCode,
        string? message)
    {
        _logger.LogWarning(
            "Automatic shipping address {Operation} failed. ErrorCode={ErrorCode}; Message={Message}",
            operation,
            errorCode,
            message);
    }
}
