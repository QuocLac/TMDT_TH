namespace WebApplication2.Services.Identity;

public sealed record CustomerProfileSnapshot(
    int AccountId,
    int CustomerId,
    string Username,
    string Email,
    string FullName,
    string PhoneNumber,
    string AvatarUrl);

public sealed record CustomerAddressSnapshot(
    int Id,
    string RecipientName,
    string PhoneNumber,
    string Street,
    int? ProvinceId,
    string City,
    int? DistrictId,
    string District,
    string? WardCode,
    string Ward,
    bool IsDefault,
    DateTime? ValidatedAt);

public sealed record SaveCustomerAddressCommand(
    int? AddressId,
    int CustomerId,
    string RecipientName,
    string PhoneNumber,
    string Street,
    int ProvinceId,
    string ProvinceName,
    int DistrictId,
    string DistrictName,
    string WardCode,
    string WardName,
    bool IsDefault);

public interface ICustomerAccountService
{
    Task<CustomerProfileSnapshot?> GetProfileAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task<bool> UpdateProfileAsync(
        int customerId,
        string fullName,
        string phoneNumber,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerAddressSnapshot>> GetAddressesAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task SaveAddressAsync(
        SaveCustomerAddressCommand command,
        CancellationToken cancellationToken);

    Task<bool> SetDefaultAddressAsync(
        int customerId,
        int addressId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAddressAsync(
        int customerId,
        int addressId,
        CancellationToken cancellationToken);
}
