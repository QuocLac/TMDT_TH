namespace WebApplication2.Services.Shipping.Ghn;

public interface IGhnAddressClient
{
    Task<GhnLookupResult<IReadOnlyList<GhnProvinceOption>>> GetProvincesAsync(
        CancellationToken cancellationToken);

    Task<GhnLookupResult<IReadOnlyList<GhnDistrictOption>>> GetDistrictsAsync(
        int provinceId,
        CancellationToken cancellationToken);

    Task<GhnLookupResult<IReadOnlyList<GhnWardOption>>> GetWardsAsync(
        int districtId,
        CancellationToken cancellationToken);

    Task<GhnAddressValidationResult> ValidateAddressAsync(
        int provinceId,
        int districtId,
        string wardCode,
        CancellationToken cancellationToken);
}
