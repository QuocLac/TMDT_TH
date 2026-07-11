using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

/// <summary>
/// Bọc EffectivePriceService gốc:
/// - preview và validation tiếp tục dùng implementation đã ổn định;
/// - recalculation dùng projection scalar, AsNoTracking và ExecuteUpdateAsync;
/// - không thao tác DbConnection/DbCommand/DbTransaction thủ công;
/// - toàn bộ update và PriceHistory vẫn tham gia transaction hiện tại.
/// </summary>
public sealed class ReliableEffectivePriceService : IEffectivePriceService
{
    private const int ChangedByMaxLength = 100;
    private const int ReasonMaxLength = 500;
    private const int NoteMaxLength = 255;
    private const int CorrelationIdMaxLength = 64;

    private static readonly PriceCampaignStatus[] EffectiveStatuses =
    [
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

    private readonly EffectivePriceService _inner;
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReliableEffectivePriceService> _logger;

    public ReliableEffectivePriceService(
        EffectivePriceService inner,
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<ReliableEffectivePriceService> logger)
    {
        _inner = inner;
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        return _inner.ValidateCampaignAsync(
            campaignId,
            startDateUtc,
            endDateUtc,
            items,
            cancellationToken);
    }

    public Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        return _inner.ValidateCampaignAsync(
            campaignId,
            mode,
            startDateUtc,
            endDateUtc,
            conflictPolicy,
            items,
            cancellationToken);
    }

    public Task<PricePlanPreviewResult> PreviewCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<PricePlanPreviewInput> items,
        CancellationToken cancellationToken)
    {
        return _inner.PreviewCampaignAsync(
            campaignId,
            mode,
            startDateUtc,
            endDateUtc,
            conflictPolicy,
            items,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult> RecalculateVariantsAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        return RecalculateVariantsAsync(
            variantIds,
            changedBy,
            reason,
            Guid.NewGuid().ToString("N"),
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult> RecalculateVariantsAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        return RecalculateAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            null,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult> RecalculateVariantsAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        string correlationId,
        PriceHistoryWriteContext historyContext,
        CancellationToken cancellationToken)
    {
        return RecalculateAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            historyContext,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult>
        RecalculateAffectedVariantsAsync(
            string changedBy,
            string reason,
            CancellationToken cancellationToken)
    {
        return RecalculateAffectedVariantsAsync(
            changedBy,
            reason,
            Guid.NewGuid().ToString("N"),
            cancellationToken);
    }

    public async Task<EffectivePriceRecalculationResult>
        RecalculateAffectedVariantsAsync(
            string changedBy,
            string reason,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var variantIds = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.CurrentPrice != variant.Price
                || variant.CurrentPriceSourceType
                    != EffectivePriceSourceType.ListPrice
                || variant.CampaignItems.Any(item =>
                    EffectiveStatuses.Contains(item.Campaign.Status)
                    && item.Campaign.StartDate <= nowUtc
                    && (!item.Campaign.EndDate.HasValue
                        || item.Campaign.EndDate.Value > nowUtc)))
            .Select(variant => variant.Id)
            .ToArrayAsync(cancellationToken);

        return await RecalculateAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            null,
            nowUtc,
            cancellationToken);
    }

    private async Task<EffectivePriceRecalculationResult> RecalculateAtAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        string correlationId,
        PriceHistoryWriteContext? historyContext,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var ids = variantIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new EffectivePriceRecalculationResult(0, 0);
        }

        IDbContextTransaction? localTransaction = null;
        var ownsTransaction = _context.Database.CurrentTransaction is null;
        var substage = "INITIALIZE";

        try
        {
            if (ownsTransaction)
            {
                substage = "BEGIN_TRANSACTION";
                localTransaction =
                    await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);
            }

            substage = "DETACH_VARIANTS";
            DetachAffectedVariants(ids);

            substage = "LOAD_VARIANTS";
            var variantRows = await _context.ProductVariants
                .AsNoTracking()
                .Where(variant => ids.Contains(variant.Id))
                .Select(variant => new
                {
                    variant.Id,
                    variant.ProductId,
                    ProductName = variant.Product.Name,
                    ProductIsActive = variant.Product.IsActive,
                    variant.SKU,
                    ListPrice = variant.Price,
                    variant.CurrentPrice,
                    variant.CurrentPriceSourceType,
                    variant.CurrentPriceSourceId,
                    variant.CurrentPriceEffectiveFrom,
                    variant.CurrentPriceEffectiveTo,
                    variant.StockQuantity,
                    variant.IsActive,
                    variant.RowVersion
                })
                .ToArrayAsync(cancellationToken);

            var missingIds = ids
                .Except(variantRows.Select(item => item.Id))
                .OrderBy(id => id)
                .ToArray();

            if (missingIds.Length > 0)
            {
                throw new EffectivePriceRecalculationException(
                    $"Không tìm thấy biến thể ID: {string.Join(", ", missingIds)}.",
                    "VARIANT_NOT_FOUND_DURING_RECALCULATION");
            }

            foreach (var row in variantRows)
            {
                ValidateVariant(
                    row.Id,
                    row.ProductId,
                    row.ProductName,
                    row.ProductIsActive,
                    row.SKU,
                    row.ListPrice,
                    row.CurrentPrice,
                    row.StockQuantity,
                    row.IsActive,
                    row.RowVersion,
                    row.CurrentPriceSourceType,
                    row.CurrentPriceSourceId);
            }

            substage = "LOAD_EFFECTIVE_CAMPAIGNS";
            var candidateRows = await _context.PriceCampaignItems
                .AsNoTracking()
                .Where(item =>
                    ids.Contains(item.VariantId)
                    && EffectiveStatuses.Contains(item.Campaign.Status)
                    && item.Campaign.StartDate <= nowUtc
                    && (!item.Campaign.EndDate.HasValue
                        || item.Campaign.EndDate.Value > nowUtc)
                    && item.NewPrice > 0)
                .OrderBy(item => item.VariantId)
                .ThenByDescending(item => item.Campaign.StartDate)
                .ThenByDescending(item => item.CampaignId)
                .Select(item => new
                {
                    item.VariantId,
                    item.CampaignId,
                    CampaignName = item.Campaign.Name,
                    SourceType = item.Campaign.SourceType,
                    item.Campaign.StartDate,
                    item.Campaign.EndDate,
                    item.NewPrice
                })
                .ToArrayAsync(cancellationToken);

            var duplicateWinner = candidateRows
                .GroupBy(item => item.VariantId)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicateWinner is not null)
            {
                var sku = variantRows
                    .First(item => item.Id == duplicateWinner.Key)
                    .SKU;

                throw new EffectivePriceRecalculationException(
                    $"Biến thể {sku} (ID {duplicateWinner.Key}) đang có "
                    + $"{duplicateWinner.Count()} kế hoạch cùng hiệu lực.",
                    "MULTIPLE_EFFECTIVE_CAMPAIGNS",
                    duplicateWinner.Key,
                    sku);
            }

            var winnerByVariantId = candidateRows
                .GroupBy(item => item.VariantId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());

            var normalizedChangedBy = Normalize(
                changedBy,
                ChangedByMaxLength,
                "System");
            var normalizedReason = Normalize(
                reason,
                ReasonMaxLength,
                "Price projection");
            var normalizedCorrelationId = Normalize(
                correlationId,
                CorrelationIdMaxLength,
                Guid.NewGuid().ToString("N"));

            var changedCount = 0;

            foreach (var row in variantRows)
            {
                winnerByVariantId.TryGetValue(
                    row.Id,
                    out var winner);

                var desiredPrice =
                    winner?.NewPrice ?? row.ListPrice;
                var desiredSourceType =
                    winner is null
                        ? EffectivePriceSourceType.ListPrice
                        : EffectivePriceSourceType.Campaign;
                var desiredSourceId =
                    winner?.CampaignId;
                var desiredEffectiveFrom =
                    winner?.StartDate;
                var desiredEffectiveTo =
                    winner?.EndDate;

                var changed =
                    row.CurrentPrice != desiredPrice
                    || row.CurrentPriceSourceType
                        != desiredSourceType
                    || row.CurrentPriceSourceId
                        != desiredSourceId
                    || row.CurrentPriceEffectiveFrom
                        != desiredEffectiveFrom
                    || row.CurrentPriceEffectiveTo
                        != desiredEffectiveTo;

                if (!changed)
                {
                    continue;
                }

                substage = $"UPDATE_VARIANT_{row.Id}";

                var updated = await _context.ProductVariants
                    .Where(variant => variant.Id == row.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                variant => variant.CurrentPrice,
                                desiredPrice)
                            .SetProperty(
                                variant => variant.CurrentPriceSourceType,
                                desiredSourceType)
                            .SetProperty(
                                variant => variant.CurrentPriceSourceId,
                                desiredSourceId)
                            .SetProperty(
                                variant => variant.CurrentPriceEffectiveFrom,
                                desiredEffectiveFrom)
                            .SetProperty(
                                variant => variant.CurrentPriceEffectiveTo,
                                desiredEffectiveTo)
                            .SetProperty(
                                variant => variant.UpdatedAt,
                                nowUtc),
                        cancellationToken);

                if (updated != 1)
                {
                    throw new EffectivePriceRecalculationException(
                        $"Không thể cập nhật biến thể {row.SKU} "
                        + $"(ID {row.Id}); số dòng tác động: {updated}.",
                        "VARIANT_UPDATE_COUNT_INVALID",
                        row.Id,
                        row.SKU);
                }

                var eventType =
                    historyContext?.EventTypeOverride
                    ?? ResolveEventType(
                        row.CurrentPriceSourceType,
                        row.CurrentPriceSourceId,
                        desiredSourceId);
                var sourceType =
                    historyContext?.SourceTypeOverride
                    ?? winner?.SourceType
                    ?? PriceChangeSourceType.System;
                var sourceId =
                    historyContext?.SourceIdOverride
                    ?? desiredSourceId;
                var priceSource =
                    winner is null
                        ? "Khôi phục giá niêm yết"
                        : $"Áp dụng kế hoạch {winner.CampaignName} "
                            + $"(#{winner.CampaignId})";

                _context.PriceHistories.Add(
                    new PriceHistory
                    {
                        ProductVariantId = row.Id,
                        OldPrice = row.CurrentPrice,
                        NewPrice = desiredPrice,
                        EventType = eventType,
                        SourceType = sourceType,
                        SourceId = sourceId,
                        CorrelationId =
                            normalizedCorrelationId,
                        Reason = normalizedReason,
                        EffectiveFrom =
                            desiredEffectiveFrom,
                        EffectiveTo =
                            desiredEffectiveTo,
                        ChangedBy =
                            normalizedChangedBy,
                        Note = Normalize(
                            $"{normalizedReason}. {priceSource}.",
                            NoteMaxLength,
                            priceSource),
                        CreatedAt = nowUtc
                    });

                changedCount++;
            }

            if (changedCount > 0)
            {
                substage = "SAVE_PRICE_HISTORY";
                await _context.SaveChangesAsync(
                    cancellationToken);
            }

            if (localTransaction is not null)
            {
                substage = "COMMIT_LOCAL_TRANSACTION";
                await localTransaction.CommitAsync(
                    cancellationToken);
            }

            _logger.LogInformation(
                "Reliable effective-price recalculation completed. Evaluated={Evaluated}, Changed={Changed}, CorrelationId={CorrelationId}.",
                variantRows.Length,
                changedCount,
                normalizedCorrelationId);

            return new EffectivePriceRecalculationResult(
                variantRows.Length,
                changedCount);
        }
        catch (EffectivePriceRecalculationException)
        {
            await RollbackLocalAsync(localTransaction);
            throw;
        }
        catch (InvalidOperationException exception)
        {
            await RollbackLocalAsync(localTransaction);

            _logger.LogError(
                exception,
                "Reliable effective-price invalid operation. Substage={Substage}, VariantIds={VariantIds}.",
                substage,
                string.Join(",", ids));

            throw new EffectivePriceRecalculationException(
                $"EF Core từ chối thao tác tại bước {substage}: "
                + Sanitize(exception.Message),
                $"PRICE_RECALCULATION_INVALID_OPERATION_{substage}",
                ids.Length == 1 ? ids[0] : null);
        }
        catch
        {
            await RollbackLocalAsync(localTransaction);
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

    private void DetachAffectedVariants(
        IReadOnlyCollection<int> variantIds)
    {
        var idSet = variantIds.ToHashSet();

        foreach (var entry in _context.ChangeTracker
                     .Entries<ProductVariant>()
                     .Where(entry =>
                         idSet.Contains(entry.Entity.Id))
                     .ToArray())
        {
            if (entry.State is EntityState.Added
                or EntityState.Modified
                or EntityState.Deleted)
            {
                throw new EffectivePriceRecalculationException(
                    $"Biến thể {entry.Entity.SKU} "
                    + $"(ID {entry.Entity.Id}) đang ở trạng thái "
                    + $"{entry.State} trong ChangeTracker.",
                    "VARIANT_HAS_PENDING_TRACKED_CHANGES",
                    entry.Entity.Id,
                    entry.Entity.SKU);
            }

            entry.State = EntityState.Detached;
        }
    }

    private static void ValidateVariant(
        int variantId,
        int productId,
        string productName,
        bool productIsActive,
        string sku,
        decimal listPrice,
        decimal currentPrice,
        int stockQuantity,
        bool variantIsActive,
        byte[] rowVersion,
        EffectivePriceSourceType currentSourceType,
        int? currentSourceId)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new EffectivePriceRecalculationException(
                $"Sản phẩm ID {productId} không có tên hợp lệ.",
                "PRODUCT_NAME_INVALID",
                variantId,
                sku);
        }

        if (!productIsActive)
        {
            throw new EffectivePriceRecalculationException(
                $"Sản phẩm {productName} (ID {productId}) "
                + "đang ngừng hoạt động.",
                "PRODUCT_INACTIVE_DURING_RECALCULATION",
                variantId,
                sku);
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể ID {variantId} không có SKU hợp lệ.",
                "VARIANT_SKU_INVALID",
                variantId);
        }

        if (!variantIsActive)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {sku} (ID {variantId}) "
                + "đang ngừng hoạt động.",
                "VARIANT_INACTIVE_DURING_RECALCULATION",
                variantId,
                sku);
        }

        if (listPrice <= 0)
        {
            throw new EffectivePriceRecalculationException(
                $"Giá niêm yết của {sku} phải lớn hơn 0.",
                "VARIANT_LIST_PRICE_INVALID",
                variantId,
                sku);
        }

        if (currentPrice <= 0)
        {
            throw new EffectivePriceRecalculationException(
                $"Giá hiện tại của {sku} phải lớn hơn 0.",
                "VARIANT_CURRENT_PRICE_INVALID",
                variantId,
                sku);
        }

        if (stockQuantity < 0)
        {
            throw new EffectivePriceRecalculationException(
                $"Tồn kho của {sku} không được âm.",
                "VARIANT_STOCK_INVALID",
                variantId,
                sku);
        }

        if (rowVersion is not { Length: 8 })
        {
            throw new EffectivePriceRecalculationException(
                $"RowVersion của {sku} không hợp lệ.",
                "VARIANT_ROW_VERSION_INVALID",
                variantId,
                sku);
        }

        if (currentSourceType == EffectivePriceSourceType.Campaign
            && !currentSourceId.HasValue)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {sku} có nguồn Campaign "
                + "nhưng CurrentPriceSourceId đang NULL.",
                "VARIANT_CAMPAIGN_SOURCE_ID_MISSING",
                variantId,
                sku);
        }

        if (currentSourceType == EffectivePriceSourceType.ListPrice
            && currentSourceId.HasValue)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {sku} có nguồn ListPrice "
                + $"nhưng CurrentPriceSourceId={currentSourceId}.",
                "VARIANT_LIST_PRICE_SOURCE_ID_NOT_NULL",
                variantId,
                sku);
        }
    }

    private static PriceHistoryEventType ResolveEventType(
        EffectivePriceSourceType currentSourceType,
        int? currentSourceId,
        int? desiredSourceId)
    {
        if (!desiredSourceId.HasValue)
        {
            return PriceHistoryEventType.Restored;
        }

        if (currentSourceType
                == EffectivePriceSourceType.Campaign
            && currentSourceId.HasValue
            && currentSourceId.Value
                != desiredSourceId.Value)
        {
            return PriceHistoryEventType.Replaced;
        }

        return PriceHistoryEventType.Applied;
    }

    private static async Task RollbackLocalAsync(
        IDbContextTransaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(
                CancellationToken.None);
        }
        catch
        {
            // Giữ nguyên lỗi nghiệp vụ ban đầu.
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

    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Không có thông báo kỹ thuật.";
        }

        return message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
