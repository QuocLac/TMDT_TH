using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebApplication2.Models;

namespace WebApplication2.Services.Pricing;

/// <summary>
/// Bảo vệ PriceHistory như một append-only ledger.
/// Sự kiện đã ghi không được sửa hoặc xóa bằng EF Core.
/// </summary>
public sealed class PriceHistoryAppendOnlyInterceptor
    : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    public PriceHistoryAppendOnlyInterceptor(
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Validate(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>>
        SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
    {
        Validate(eventData.Context);
        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private void Validate(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker
                     .Entries<PriceHistory>())
        {
            if (entry.State is EntityState.Modified
                or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Lịch sử giá là sổ bất biến; không được sửa hoặc xóa "
                    + $"sự kiện PriceHistory #{entry.Entity.Id}.");
            }

            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var history = entry.Entity;

            if (history.ProductVariantId <= 0)
            {
                throw new InvalidOperationException(
                    "Lịch sử giá phải gắn với một biến thể hợp lệ.");
            }

            if (history.OldPrice <= 0
                || history.NewPrice <= 0)
            {
                throw new InvalidOperationException(
                    "Giá trước và giá sau trong lịch sử phải lớn hơn 0.");
            }

            history.Currency = NormalizeCurrency(history.Currency);

            if (history.CreatedAt == default)
            {
                history.CreatedAt = nowUtc;
            }

            history.EffectiveFrom ??= history.CreatedAt;

            if (history.EffectiveTo.HasValue
                && history.EffectiveTo.Value
                    <= history.EffectiveFrom.Value)
            {
                throw new InvalidOperationException(
                    "Thời điểm kết thúc hiệu lực phải sau thời điểm bắt đầu.");
            }

            history.ChangedBy = NormalizeRequired(
                history.ChangedBy,
                100,
                "System");
            history.Note = NormalizeRequired(
                history.Note,
                255,
                "Ghi nhận thay đổi giá");
            history.Reason = NormalizeOptional(
                history.Reason,
                500);
            history.CorrelationId = NormalizeOptional(
                history.CorrelationId,
                64);
            history.UpdatedAt = null;
        }
    }

    private static string NormalizeCurrency(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "VND"
            : value.Trim().ToUpperInvariant();

        if (normalized.Length != 3
            || normalized.Any(character =>
                character is < 'A' or > 'Z'))
        {
            throw new InvalidOperationException(
                "Mã tiền tệ phải gồm đúng 3 chữ cái ISO viết hoa.");
        }

        return normalized;
    }

    private static string NormalizeRequired(
        string? value,
        int maxLength,
        string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
