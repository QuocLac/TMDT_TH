using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Commerce.Orders;

public sealed class OrderNumberGenerator : IOrderNumberGenerator
{
    private const int MaximumAttempts = 10;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public OrderNumberGenerator(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<string> GenerateAsync(CancellationToken cancellationToken)
    {
        var datePart = _timeProvider.GetUtcNow()
            .UtcDateTime
            .ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var randomPart = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            var code = $"FB{datePart}{randomPart}";

            var exists = await _context.Orders
                .AsNoTracking()
                .AnyAsync(item => item.Code == code, cancellationToken);
            if (!exists)
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "Không thể tạo mã đơn hàng duy nhất sau nhiều lần thử.");
    }
}
