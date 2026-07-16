using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Identity;

public sealed class CustomerAccountService : ICustomerAccountService
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public CustomerAccountService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public Task<CustomerProfileSnapshot?> GetProfileAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return _context.Customers
            .AsNoTracking()
            .Where(item => item.Id == customerId && item.Account.IsActive)
            .Select(item => new CustomerProfileSnapshot(
                item.AccountId,
                item.Id,
                item.Account.Username,
                item.Account.Email,
                item.FullName,
                item.PhoneNumber,
                item.Account.Avt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> UpdateProfileAsync(
        int customerId,
        string fullName,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken);

        if (customer is null)
        {
            return false;
        }

        customer.FullName = fullName.Trim();
        customer.PhoneNumber = phoneNumber.Trim();
        customer.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CustomerAddressSnapshot>> GetAddressesAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return await _context.Addresses
            .AsNoTracking()
            .Where(item => item.CustomerId == customerId)
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .Select(item => new CustomerAddressSnapshot(
                item.Id,
                item.RecipientName ?? string.Empty,
                item.PhoneNumber,
                item.Street,
                item.ProvinceId,
                item.City,
                item.DistrictId,
                item.District,
                item.WardCode,
                item.Ward,
                item.IsDefault,
                item.ValidatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task SaveAddressAsync(
        SaveCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var addresses = await _context.Addresses
            .Where(item => item.CustomerId == command.CustomerId)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        Address address;
        if (command.AddressId.HasValue)
        {
            address = addresses.SingleOrDefault(
                    item => item.Id == command.AddressId.Value)
                ?? throw new InvalidOperationException(
                    "Địa chỉ không thuộc tài khoản hiện tại.");
        }
        else
        {
            address = new Address
            {
                CustomerId = command.CustomerId,
                CreatedAt = nowUtc
            };
            _context.Addresses.Add(address);
            addresses.Add(address);
        }

        var shouldBeDefault = command.IsDefault
            || address.IsDefault
            || addresses.Count == 1
            || addresses.All(item => !item.IsDefault);

        if (shouldBeDefault)
        {
            foreach (var item in addresses.Where(item => item != address))
            {
                item.IsDefault = false;
                item.UpdatedAt = nowUtc;
            }
        }

        address.RecipientName = command.RecipientName.Trim();
        address.PhoneNumber = command.PhoneNumber.Trim();
        address.Street = command.Street.Trim();
        address.ProvinceId = command.ProvinceId;
        address.City = command.ProvinceName.Trim();
        address.DistrictId = command.DistrictId;
        address.District = command.DistrictName.Trim();
        address.WardCode = command.WardCode.Trim();
        address.Ward = command.WardName.Trim();
        address.ValidatedAt = nowUtc;
        address.IsDefault = shouldBeDefault;
        address.UpdatedAt = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> SetDefaultAddressAsync(
        int customerId,
        int addressId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var addresses = await _context.Addresses
            .Where(item => item.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var target = addresses.SingleOrDefault(item => item.Id == addressId);
        if (target is null)
        {
            return false;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var address in addresses)
        {
            address.IsDefault = address.Id == addressId;
            address.UpdatedAt = nowUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAddressAsync(
        int customerId,
        int addressId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var addresses = await _context.Addresses
            .Where(item => item.CustomerId == customerId)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var target = addresses.SingleOrDefault(item => item.Id == addressId);
        if (target is null)
        {
            return false;
        }

        var wasDefault = target.IsDefault;
        _context.Addresses.Remove(target);

        if (wasDefault)
        {
            var replacement = addresses.FirstOrDefault(item => item.Id != addressId);
            if (replacement is not null)
            {
                replacement.IsDefault = true;
                replacement.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
