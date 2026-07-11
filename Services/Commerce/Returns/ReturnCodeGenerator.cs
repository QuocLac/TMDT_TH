using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Commerce.Returns;

public sealed class ReturnCodeGenerator : IReturnCodeGenerator
{
    private const int MaximumAttempts = 10;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ReturnCodeGenerator(
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
            var randomPart = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var code = $"RTN{datePart}{randomPart}";

            var exists = await _context.ReturnRequests
                .AsNoTracking()
                .AnyAsync(item => item.Code == code, cancellationToken);
            if (!exists)
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "Không thể tạo mã hoàn trả duy nhất sau nhiều lần thử.");
    }
}
