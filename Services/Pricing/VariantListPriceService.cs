using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public interface IVariantListPriceService
{
    Task<VariantListPriceChangeResult> ChangeAsync(
        VariantListPriceChangeCommand command,
        CancellationToken cancellationToken);
}

public sealed record VariantListPriceChangeCommand(
    int VariantId,
    decimal NewListPrice,
    string? ExpectedRowVersion,
    string ChangedBy,
    string Reason,
    string CorrelationId);

public sealed record VariantListPriceSnapshot(
    int VariantId,
    string Sku,
    decimal ListPrice,
    decimal CurrentPrice,
    byte[] RowVersion);

public sealed record VariantListPriceChangeResult(
    bool Success,
    string Message,
    string? ErrorCode,
    VariantListPriceSnapshot? Snapshot)
{
    public static VariantListPriceChangeResult Failure(
        string message,
        string errorCode)
    {
        return new(false, message, errorCode, null);
    }

    public static VariantListPriceChangeResult Succeeded(
        string message,
        VariantListPriceSnapshot snapshot)
    {
        return new(true, message, null, snapshot);
    }
}

/// <summary>
/// Cổng ghi duy nhất cho thay đổi giá niêm yết của biến thể.
/// Giá niêm yết, projection CurrentPrice và PriceHistory được xử lý
/// trong cùng transaction để không tồn tại trạng thái cập nhật dở dang.
/// </summary>
public sealed class VariantListPriceService
    : IVariantListPriceService
{
    private const int ChangedByMaxLength = 100;
    private const int ReasonMaxLength = 500;
    private const int NoteMaxLength = 255;
    private const int CorrelationIdMaxLength = 64;
    private const string DefaultCurrency = "VND";

    private static readonly PriceCampaignStatus[]
        PriceGuardStatuses =
    [
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

    private readonly ApplicationDbContext _context;
    private readonly IEffectivePriceService _effectivePriceService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VariantListPriceService> _logger;

    public VariantListPriceService(
        ApplicationDbContext context,
        IEffectivePriceService effectivePriceService,
        TimeProvider timeProvider,
        ILogger<VariantListPriceService> logger)
    {
        _context = context;
        _effectivePriceService = effectivePriceService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<VariantListPriceChangeResult> ChangeAsync(
        VariantListPriceChangeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.VariantId <= 0)
        {
            return VariantListPriceChangeResult.Failure(
                "ID biến thể không hợp lệ.",
                "INVALID_VARIANT_ID");
        }

        if (command.NewListPrice <= 0)
        {
            return VariantListPriceChangeResult.Failure(
                "Giá niêm yết phải lớn hơn 0.",
                "INVALID_LIST_PRICE");
        }

        if (!TryDecodeRowVersion(
                command.ExpectedRowVersion,
                out var expectedRowVersion))
        {
            return VariantListPriceChangeResult.Failure(
                "Phiên bản dữ liệu không hợp lệ. "
                + "Vui lòng tải lại trang.",
                "INVALID_ROW_VERSION");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var changedBy = Normalize(
            command.ChangedBy,
            ChangedByMaxLength,
            "Admin");
        var reason = Normalize(
            command.Reason,
            ReasonMaxLength,
            "Cập nhật giá niêm yết");
        var correlationId = Normalize(
            command.CorrelationId,
            CorrelationIdMaxLength,
            Guid.NewGuid().ToString("N"));

        IDbContextTransaction? localTransaction = null;
        var ownsTransaction =
            _context.Database.CurrentTransaction is null;

        try
        {
            if (ownsTransaction)
            {
                localTransaction =
                    await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);
            }

            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(
                    item => item.Id == command.VariantId,
                    cancellationToken);

            if (variant is null)
            {
                return await FailAsync(
                    localTransaction,
                    "Không tìm thấy biến thể.",
                    "VARIANT_NOT_FOUND");
            }

            _context.Entry(variant)
                .Property(item => item.RowVersion)
                .OriginalValue = expectedRowVersion;

            if (variant.Price == command.NewListPrice)
            {
                return await FailAsync(
                    localTransaction,
                    "Giá niêm yết không thay đổi.",
                    "LIST_PRICE_UNCHANGED");
            }

            var conflict = await _context.PriceCampaignItems
                .AsNoTracking()
                .Where(item =>
                    item.VariantId == variant.Id
                    && PriceGuardStatuses.Contains(
                        item.Campaign.Status)
                    && (!item.Campaign.EndDate.HasValue
                        || item.Campaign.EndDate.Value > nowUtc)
                    && item.NewPrice >= command.NewListPrice)
                .OrderBy(item => item.Campaign.StartDate)
                .ThenBy(item => item.CampaignId)
                .Select(item => new
                {
                    item.CampaignId,
                    item.Campaign.Name,
                    item.NewPrice
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (conflict is not null)
            {
                return await FailAsync(
                    localTransaction,
                    $"Giá niêm yết phải lớn hơn giá "
                    + $"{conflict.NewPrice:N0} của kế hoạch "
                    + $"“{conflict.Name}” "
                    + $"(#{conflict.CampaignId}).",
                    "LIST_PRICE_NOT_ABOVE_CAMPAIGN_PRICE");
            }

            var oldListPrice = variant.Price;
            variant.Price = command.NewListPrice;
            variant.UpdatedAt = nowUtc;

            await _context.SaveChangesAsync(cancellationToken);

            _context.PriceHistories.Add(
                new PriceHistory
                {
                    ProductVariantId = variant.Id,
                    PriceKind = PriceHistoryKind.ListPrice,
                    Currency = DefaultCurrency,
                    OldPrice = oldListPrice,
                    NewPrice = command.NewListPrice,
                    EventType =
                        PriceHistoryEventType.ListPriceChanged,
                    SourceType = PriceChangeSourceType.Manual,
                    SourceId = null,
                    CorrelationId = correlationId,
                    Reason = reason,
                    EffectiveFrom = nowUtc,
                    EffectiveTo = null,
                    ChangedBy = changedBy,
                    Note = Normalize(
                        "Cập nhật giá niêm yết của biến thể.",
                        NoteMaxLength,
                        "Cập nhật giá niêm yết"),
                    CreatedAt = nowUtc
                });

            await _context.SaveChangesAsync(cancellationToken);

            await _effectivePriceService
                .RecalculateVariantsAsync(
                    [variant.Id],
                    changedBy,
                    reason,
                    correlationId,
                    new PriceHistoryWriteContext(
                        PriceHistoryEventType
                            .EffectivePriceChanged,
                        PriceChangeSourceType.Manual),
                    cancellationToken);

            var snapshot = await _context.ProductVariants
                .AsNoTracking()
                .Where(item => item.Id == variant.Id)
                .Select(item => new VariantListPriceSnapshot(
                    item.Id,
                    item.SKU,
                    item.Price,
                    item.CurrentPrice,
                    item.RowVersion))
                .SingleAsync(cancellationToken);

            if (localTransaction is not null)
            {
                await localTransaction.CommitAsync(
                    cancellationToken);
            }

            _logger.LogInformation(
                "Variant list price changed. "
                + "VariantId={VariantId}, "
                + "OldListPrice={OldListPrice}, "
                + "NewListPrice={NewListPrice}, "
                + "CurrentPrice={CurrentPrice}, "
                + "ChangedBy={ChangedBy}, "
                + "CorrelationId={CorrelationId}.",
                snapshot.VariantId,
                oldListPrice,
                snapshot.ListPrice,
                snapshot.CurrentPrice,
                changedBy,
                correlationId);

            return VariantListPriceChangeResult.Succeeded(
                $"Đã cập nhật giá niêm yết cho SKU "
                + $"{snapshot.Sku}.",
                snapshot);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAsync(localTransaction);

            _logger.LogWarning(
                exception,
                "Concurrent variant list-price update. "
                + "VariantId={VariantId}, "
                + "CorrelationId={CorrelationId}.",
                command.VariantId,
                correlationId);

            return VariantListPriceChangeResult.Failure(
                "Dữ liệu vừa được thay đổi ở nơi khác. "
                + "Vui lòng tải lại trang và thử lại.",
                "VARIANT_CONCURRENCY_CONFLICT");
        }
        catch
        {
            await RollbackAsync(localTransaction);
            throw;
        }
        finally
        {
            if (localTransaction is not null)
            {
                await localTransaction.DisposeAsync();
            }
        }
    }

    private static bool TryDecodeRowVersion(
        string? encodedRowVersion,
        out byte[] rowVersion)
    {
        rowVersion = [];

        if (string.IsNullOrWhiteSpace(encodedRowVersion))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(
                encodedRowVersion);
            return rowVersion.Length == 8;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Normalize(
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

    private static async Task<VariantListPriceChangeResult>
        FailAsync(
            IDbContextTransaction? localTransaction,
            string message,
            string errorCode)
    {
        await RollbackAsync(localTransaction);
        return VariantListPriceChangeResult.Failure(
            message,
            errorCode);
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? localTransaction)
    {
        if (localTransaction is null)
        {
            return;
        }

        try
        {
            await localTransaction.RollbackAsync(
                CancellationToken.None);
        }
        catch
        {
            // Giữ lỗi nghiệp vụ hoặc lỗi gốc quan trọng hơn lỗi rollback.
        }
    }
}
