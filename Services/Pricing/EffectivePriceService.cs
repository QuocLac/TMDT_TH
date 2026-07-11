using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public sealed class EffectivePriceService : IEffectivePriceService
{
    private const decimal MaximumMoney = 9999999999999999m;
    private const int ChangedByMaxLength = 100;
    private const int NoteMaxLength = 255;
    private const int ReasonMaxLength = 500;
    private const int CorrelationIdMaxLength = 64;

    private static readonly TimeSpan SupersedeNowClockTolerance =
        TimeSpan.FromMinutes(5);

    private static readonly PriceCampaignStatus[] BlockingStatuses =
    [
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public EffectivePriceService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        return ValidateCampaignAsync(
            campaignId,
            PriceCampaignMode.FixedWindow,
            startDateUtc,
            endDateUtc,
            PriceConflictPolicy.Reject,
            items,
            cancellationToken);
    }

    public async Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewCampaignAsync(
            campaignId,
            mode,
            startDateUtc,
            endDateUtc,
            conflictPolicy,
            items.Select(item => new PricePlanPreviewInput(
                item.VariantId,
                PriceAdjustmentType.FixedPrice,
                item.NewPrice))
                .ToArray(),
            cancellationToken);

        if (!preview.IsValid || !preview.CanConfirm)
        {
            return CampaignPricingValidationResult.Failure(
                preview.ErrorMessage ?? "Dữ liệu giá không hợp lệ.",
                preview.ErrorCode ?? "PRICING_VALIDATION_FAILED",
                preview.Items
                    .Where(item => item.Conflicts.Count > 0 || item.IsStale)
                    .Select(item => item.VariantId)
                    .Distinct()
                    .ToArray());
        }

        return CampaignPricingValidationResult.Success;
    }

    public async Task<PricePlanPreviewResult> PreviewCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<PricePlanPreviewInput> items,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (!Enum.IsDefined(typeof(PriceCampaignMode), mode))
        {
            return PricePlanPreviewResult.Failure(
                "Chế độ thời gian kế hoạch không hợp lệ.",
                "INVALID_CAMPAIGN_MODE");
        }

        if (!Enum.IsDefined(typeof(PriceConflictPolicy), conflictPolicy))
        {
            return PricePlanPreviewResult.Failure(
                "Chính sách xử lý xung đột không hợp lệ.",
                "INVALID_CONFLICT_POLICY");
        }

        if (mode == PriceCampaignMode.FixedWindow
            && (!endDateUtc.HasValue || endDateUtc.Value <= startDateUtc))
        {
            return PricePlanPreviewResult.Failure(
                "Thời gian kết thúc phải sau thời gian bắt đầu.",
                "INVALID_DURATION");
        }

        if (mode == PriceCampaignMode.OpenEnded && endDateUtc.HasValue)
        {
            return PricePlanPreviewResult.Failure(
                "Kế hoạch không thời hạn không được có thời gian kết thúc.",
                "OPEN_ENDED_HAS_END_DATE");
        }

        if (endDateUtc.HasValue && endDateUtc.Value <= nowUtc)
        {
            return PricePlanPreviewResult.Failure(
                "Thời gian kết thúc phải ở tương lai.",
                "CAMPAIGN_EXPIRED");
        }

        if (conflictPolicy == PriceConflictPolicy.ReplaceFromStart
            && startDateUtc < nowUtc.Subtract(SupersedeNowClockTolerance))
        {
            return PricePlanPreviewResult.Failure(
                "Chính sách thay từ lúc bắt đầu không thể áp dụng cho thời điểm đã qua. Hãy chọn thời gian hiện tại/tương lai hoặc dùng chính sách thay thế ngay.",
                "REPLACE_FROM_START_REQUIRES_CURRENT_OR_FUTURE_START");
        }

        if (conflictPolicy == PriceConflictPolicy.SupersedeNow
            && startDateUtc > nowUtc.Add(SupersedeNowClockTolerance))
        {
            return PricePlanPreviewResult.Failure(
                "Chính sách thay thế ngay yêu cầu thời gian bắt đầu là hiện tại. Hãy cập nhật thời gian bắt đầu rồi preview lại.",
                "SUPERSEDE_NOW_REQUIRES_IMMEDIATE_START");
        }

        if (conflictPolicy == PriceConflictPolicy.SupersedeNow
            && endDateUtc.HasValue
            && endDateUtc.Value <= nowUtc)
        {
            return PricePlanPreviewResult.Failure(
                "Kế hoạch thay thế ngay phải có thời gian kết thúc sau hiện tại.",
                "CAMPAIGN_EXPIRED");
        }

        if (items.Count == 0)
        {
            return PricePlanPreviewResult.Failure(
                "Vui lòng chọn ít nhất một biến thể.",
                "EMPTY_ITEMS");
        }

        var duplicateVariantIds = items
            .GroupBy(item => item.VariantId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id)
            .ToArray();

        if (duplicateVariantIds.Length > 0)
        {
            return PricePlanPreviewResult.Failure(
                $"Một biến thể chỉ được xuất hiện một lần. ID trùng: {string.Join(", ", duplicateVariantIds)}.",
                "DUPLICATE_VARIANT");
        }

        if (items.Any(item => item.VariantId <= 0))
        {
            return PricePlanPreviewResult.Failure(
                "Danh sách biến thể chứa ID không hợp lệ.",
                "INVALID_VARIANT_ID");
        }

        var variantIds = items
            .Select(item => item.VariantId)
            .Distinct()
            .ToArray();

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.ProductId,
                ProductName = variant.Product.Name,
                variant.SKU,
                variant.Color,
                variant.Size,
                variant.Price,
                variant.CurrentPrice,
                variant.RowVersion,
                variant.IsActive,
                ProductIsActive = variant.Product.IsActive
            })
            .ToListAsync(cancellationToken);

        var variantsById = variants.ToDictionary(variant => variant.Id);
        var missingVariantIds = variantIds
            .Where(id => !variantsById.ContainsKey(id))
            .OrderBy(id => id)
            .ToArray();

        if (missingVariantIds.Length > 0)
        {
            return PricePlanPreviewResult.Failure(
                $"Không tìm thấy biến thể có ID: {string.Join(", ", missingVariantIds)}.",
                "VARIANT_NOT_FOUND");
        }

        var inactiveVariant = variants
            .FirstOrDefault(variant => !variant.IsActive || !variant.ProductIsActive);

        if (inactiveVariant is not null)
        {
            return PricePlanPreviewResult.Failure(
                $"SKU {inactiveVariant.SKU} đang ngừng hoạt động.",
                "VARIANT_INACTIVE");
        }

        var rangeStartUtc = conflictPolicy == PriceConflictPolicy.SupersedeNow
            ? nowUtc
            : startDateUtc;
        var rangeEndUtc = endDateUtc ?? DateTime.MaxValue;

        var conflictRows = await _context.PriceCampaignItems
            .AsNoTracking()
            .Where(item =>
                variantIds.Contains(item.VariantId)
                && item.CampaignId != campaignId
                && BlockingStatuses.Contains(item.Campaign.Status)
                && item.Campaign.StartDate < rangeEndUtc
                && (!item.Campaign.EndDate.HasValue
                    || item.Campaign.EndDate.Value > rangeStartUtc))
            .Select(item => new
            {
                item.VariantId,
                item.CampaignId,
                item.Campaign.Code,
                item.Campaign.Name,
                item.Campaign.Status,
                item.Campaign.StartDate,
                item.Campaign.EndDate
            })
            .OrderBy(item => item.VariantId)
            .ThenBy(item => item.StartDate)
            .ThenBy(item => item.CampaignId)
            .ToListAsync(cancellationToken);

        var conflictCampaignIds = conflictRows
            .Select(item => item.CampaignId)
            .Distinct()
            .ToArray();

        var totalVariantsByCampaignId = conflictCampaignIds.Length == 0
            ? new Dictionary<int, int>()
            : await _context.PriceCampaignItems
                .AsNoTracking()
                .Where(item => conflictCampaignIds.Contains(item.CampaignId))
                .GroupBy(item => item.CampaignId)
                .Select(group => new
                {
                    CampaignId = group.Key,
                    Count = group.Count()
                })
                .ToDictionaryAsync(
                    item => item.CampaignId,
                    item => item.Count,
                    cancellationToken);

        var coveredVariantsByCampaignId = conflictRows
            .GroupBy(item => item.CampaignId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.VariantId).Distinct().Count());

        var conflictsByVariantId = conflictRows
            .GroupBy(item => item.VariantId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PricePlanConflictResult>)group
                    .Select(item =>
                    {
                        var totalVariantCount = totalVariantsByCampaignId[item.CampaignId];
                        var coveredVariantCount = coveredVariantsByCampaignId[item.CampaignId];
                        return new PricePlanConflictResult(
                            item.CampaignId,
                            item.Code,
                            item.Name,
                            item.Status,
                            item.StartDate,
                            item.EndDate,
                            totalVariantCount,
                            coveredVariantCount,
                            totalVariantCount == coveredVariantCount);
                    })
                    .ToList());

        var previewItems = new List<PricePlanPreviewItemResult>(items.Count);

        try
        {
            foreach (var input in items)
            {
                var variant = variantsById[input.VariantId];
                var isStale = input.ExpectedRowVersion is { Length: > 0 }
                    && !variant.RowVersion.SequenceEqual(input.ExpectedRowVersion);
                var newPrice = CalculateTargetPrice(
                    variant.Price,
                    input.AdjustmentType,
                    input.AdjustmentValue,
                    variant.SKU);
                var deltaAmount = newPrice - variant.CurrentPrice;
                var deltaPercent = variant.CurrentPrice == 0
                    ? 0
                    : decimal.Round(
                        deltaAmount / variant.CurrentPrice * 100m,
                        2,
                        MidpointRounding.AwayFromZero);

                conflictsByVariantId.TryGetValue(
                    variant.Id,
                    out var conflicts);

                previewItems.Add(new PricePlanPreviewItemResult(
                    variant.ProductId,
                    variant.ProductName,
                    variant.Id,
                    variant.SKU,
                    string.Join(
                        " - ",
                        new[] { variant.Color, variant.Size }
                            .Where(value => !string.IsNullOrWhiteSpace(value))),
                    variant.RowVersion,
                    variant.Price,
                    variant.CurrentPrice,
                    newPrice,
                    deltaAmount,
                    deltaPercent,
                    input.AdjustmentType,
                    input.AdjustmentValue,
                    isStale,
                    conflicts ?? []));
            }
        }
        catch (InvalidPriceAdjustmentException exception)
        {
            return PricePlanPreviewResult.Failure(
                exception.Message,
                "INVALID_ADJUSTMENT");
        }

        var staleCount = previewItems.Count(item => item.IsStale);
        var conflictCount = previewItems.Count(item => item.Conflicts.Count > 0);
        var conflictCampaignCount = conflictCampaignIds.Length;
        var partialConflictCampaignCount = conflictCampaignIds.Count(campaignIdValue =>
            totalVariantsByCampaignId[campaignIdValue]
            != coveredVariantsByCampaignId[campaignIdValue]);

        var isValid = staleCount == 0;
        var canConfirm = isValid
            && (conflictPolicy switch
            {
                PriceConflictPolicy.Reject => conflictCampaignCount == 0,
                PriceConflictPolicy.ReplaceFromStart => partialConflictCampaignCount == 0,
                PriceConflictPolicy.SupersedeNow => true,
                _ => false
            });

        string? message = null;
        string? errorCode = null;

        if (staleCount > 0)
        {
            message = $"Có {staleCount} biến thể đã thay đổi sau khi được tải. Vui lòng tải lại dữ liệu.";
            errorCode = "STALE_VARIANT";
        }
        else if (partialConflictCampaignCount > 0
            && conflictPolicy == PriceConflictPolicy.ReplaceFromStart)
        {
            message = $"Có {partialConflictCampaignCount} kế hoạch chồng lấn chứa thêm biến thể ngoài phạm vi đang chọn. Chính sách thay từ thời điểm bắt đầu yêu cầu chọn đủ toàn bộ biến thể của từng kế hoạch.";
            errorCode = "PARTIAL_CAMPAIGN_REPLACEMENT";
        }
        else if (conflictPolicy == PriceConflictPolicy.Reject
            && conflictCampaignCount > 0)
        {
            message = $"Có {conflictCount} biến thể bị chồng lấn với {conflictCampaignCount} kế hoạch giá khác.";
            errorCode = "PRICE_WINDOW_CONFLICT";
        }
        else if (conflictCampaignCount > 0
            && conflictPolicy == PriceConflictPolicy.ReplaceFromStart)
        {
            message = $"Khi xác nhận, {conflictCampaignCount} kế hoạch chồng lấn sẽ được cắt hoặc thay thế từ thời điểm bắt đầu mới.";
        }
        else if (conflictCampaignCount > 0
            && conflictPolicy == PriceConflictPolicy.SupersedeNow)
        {
            message = $"Khi xác nhận, {conflictCampaignCount} kế hoạch chồng lấn sẽ bị thay thế ngay; các kế hoạch còn biến thể ngoài phạm vi sẽ được giữ lại cho những biến thể đó.";
        }

        var summary = new PricePlanPreviewSummary(
            previewItems.Select(item => item.ProductId).Distinct().Count(),
            previewItems.Count,
            previewItems.Count(item => item.DeltaAmount > 0),
            previewItems.Count(item => item.DeltaAmount < 0),
            previewItems.Count(item => item.DeltaAmount == 0),
            conflictCount,
            conflictCampaignCount,
            partialConflictCampaignCount,
            staleCount,
            previewItems.Sum(item => item.CurrentPrice),
            previewItems.Sum(item => item.NewPrice));

        return new PricePlanPreviewResult(
            isValid,
            canConfirm,
            message,
            errorCode,
            previewItems
                .OrderBy(item => item.ProductName)
                .ThenBy(item => item.Sku)
                .ToList(),
            summary);
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
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return RecalculateVariantsAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            null,
            nowUtc,
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
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return RecalculateVariantsAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            historyContext,
            nowUtc,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult> RecalculateAffectedVariantsAsync(
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

    public async Task<EffectivePriceRecalculationResult> RecalculateAffectedVariantsAsync(
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
                || variant.CurrentPriceSourceType != EffectivePriceSourceType.ListPrice
                || variant.CampaignItems.Any(item =>
                    BlockingStatuses.Contains(item.Campaign.Status)
                    && item.Campaign.StartDate <= nowUtc
                    && (!item.Campaign.EndDate.HasValue
                        || item.Campaign.EndDate.Value > nowUtc)))
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken);

        return await RecalculateVariantsAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            null,
            nowUtc,
            cancellationToken);
    }

    private async Task<EffectivePriceRecalculationResult> RecalculateVariantsAtAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        string correlationId,
        PriceHistoryWriteContext? historyContext,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var distinctVariantIds = variantIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (distinctVariantIds.Length == 0)
        {
            return new EffectivePriceRecalculationResult(0, 0);
        }

        EnsureAffectedVariantsAreNotPendingInChangeTracker(
            distinctVariantIds);

        var normalizedChangedBy = TruncateRequired(
            changedBy,
            ChangedByMaxLength,
            "System");
        var normalizedReason = TruncateRequired(
            reason,
            ReasonMaxLength,
            "Price projection");
        var normalizedCorrelationId = TruncateRequired(
            correlationId,
            CorrelationIdMaxLength,
            Guid.NewGuid().ToString("N"));

        var connection = _context.Database.GetDbConnection();
        var connectionWasOpen =
            connection.State == ConnectionState.Open;
        var ownsTransaction =
            _context.Database.CurrentTransaction is null;
        IDbContextTransaction? localTransaction = null;

        try
        {
            if (ownsTransaction)
            {
                localTransaction =
                    await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);
            }
            else if (connection.State != ConnectionState.Open)
            {
                await _context.Database.OpenConnectionAsync(
                    cancellationToken);
            }

            var dbTransaction = _context.Database
                .CurrentTransaction?
                .GetDbTransaction()
                ?? throw new EffectivePriceRecalculationException(
                    "Không thể lấy transaction hiện tại để tính lại giá.",
                    "PRICE_RECALCULATION_TRANSACTION_MISSING");

            var rows = new List<EffectivePriceProjectionRow>(
                distinctVariantIds.Length);

            foreach (var variantId in distinctVariantIds)
            {
                var row = await LoadEffectivePriceProjectionRowAsync(
                    connection,
                    dbTransaction,
                    variantId,
                    nowUtc,
                    cancellationToken);

                if (row is null)
                {
                    throw new EffectivePriceRecalculationException(
                        $"Không tìm thấy biến thể ID {variantId} trong bảng ProductVariants.",
                        "VARIANT_NOT_FOUND_DURING_RECALCULATION",
                        variantId);
                }

                ValidateEffectivePriceProjectionRow(row);
                rows.Add(row);
            }

            var changedCount = 0;

            foreach (var row in rows)
            {
                var hasWinner = row.WinnerCampaignId.HasValue;
                var desiredPrice = hasWinner
                    ? row.WinnerNewPrice!.Value
                    : row.ListPrice;
                var desiredSourceType = hasWinner
                    ? EffectivePriceSourceType.Campaign.ToString()
                    : EffectivePriceSourceType.ListPrice.ToString();
                var desiredSourceId = row.WinnerCampaignId;
                var desiredEffectiveFrom = row.WinnerStartDate;
                var desiredEffectiveTo = row.WinnerEndDate;

                var priceChanged = row.CurrentPrice != desiredPrice;
                var sourceChanged =
                    !string.Equals(
                        row.CurrentPriceSourceType,
                        desiredSourceType,
                        StringComparison.Ordinal)
                    || row.CurrentPriceSourceId != desiredSourceId
                    || row.CurrentPriceEffectiveFrom
                        != desiredEffectiveFrom
                    || row.CurrentPriceEffectiveTo
                        != desiredEffectiveTo;

                if (!priceChanged && !sourceChanged)
                {
                    continue;
                }

                var eventType = historyContext?.EventTypeOverride
                    ?.ToString()
                    ?? ResolveHistoryEventTypeName(
                        row,
                        desiredSourceId);
                var historySourceType =
                    historyContext?.SourceTypeOverride
                        ?.ToString()
                    ?? row.WinnerSourceType
                    ?? PriceChangeSourceType.System.ToString();
                var historySourceId =
                    historyContext?.SourceIdOverride
                    ?? desiredSourceId;
                var priceSource = hasWinner
                    ? $"Áp dụng kế hoạch {row.WinnerCampaignName} (#{row.WinnerCampaignId})"
                    : "Khôi phục giá niêm yết";
                var note = TruncateRequired(
                    $"{normalizedReason}. {priceSource}.",
                    NoteMaxLength,
                    priceSource);

                await InsertPriceHistoryAsync(
                    connection,
                    dbTransaction,
                    row,
                    desiredPrice,
                    eventType,
                    historySourceType,
                    historySourceId,
                    normalizedCorrelationId,
                    normalizedReason,
                    desiredEffectiveFrom,
                    desiredEffectiveTo,
                    normalizedChangedBy,
                    note,
                    nowUtc,
                    cancellationToken);

                var updated = await UpdateVariantProjectionAsync(
                    connection,
                    dbTransaction,
                    row,
                    desiredPrice,
                    desiredSourceType,
                    desiredSourceId,
                    desiredEffectiveFrom,
                    desiredEffectiveTo,
                    nowUtc,
                    cancellationToken);

                if (!updated)
                {
                    throw new EffectivePriceRecalculationException(
                        $"Biến thể {row.Sku} (ID {row.VariantId}) đã thay đổi trong lúc xác nhận. "
                        + "Transaction đã rollback; hãy tải lại dữ liệu.",
                        "VARIANT_CONCURRENCY_CONFLICT_DURING_RECALCULATION",
                        row.VariantId,
                        row.Sku);
                }

                changedCount++;
            }

            if (localTransaction is not null)
            {
                await localTransaction.CommitAsync(
                    cancellationToken);
            }

            return new EffectivePriceRecalculationResult(
                rows.Count,
                changedCount);
        }
        catch
        {
            if (localTransaction is not null)
            {
                try
                {
                    await localTransaction.RollbackAsync(
                        CancellationToken.None);
                }
                catch
                {
                    // Lỗi gốc quan trọng hơn lỗi rollback cục bộ.
                }
            }

            throw;
        }
        finally
        {
            if (localTransaction is not null)
            {
                await localTransaction.DisposeAsync();
            }

            if (!connectionWasOpen
                && _context.Database.CurrentTransaction is null
                && connection.State == ConnectionState.Open)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }

    private void EnsureAffectedVariantsAreNotPendingInChangeTracker(
        IReadOnlyCollection<int> variantIds)
    {
        var affectedIds = variantIds.ToHashSet();
        var trackedEntries = _context.ChangeTracker
            .Entries<ProductVariant>()
            .Where(entry => affectedIds.Contains(entry.Entity.Id))
            .ToArray();

        foreach (var entry in trackedEntries)
        {
            if (entry.State is EntityState.Added
                or EntityState.Modified
                or EntityState.Deleted)
            {
                throw new EffectivePriceRecalculationException(
                    $"Biến thể {entry.Entity.SKU} (ID {entry.Entity.Id}) "
                    + $"đang ở trạng thái EF {entry.State} trước khi tính lại giá.",
                    "VARIANT_HAS_PENDING_TRACKED_CHANGES",
                    entry.Entity.Id,
                    entry.Entity.SKU);
            }

            entry.State = EntityState.Detached;
        }
    }

    private static async Task<EffectivePriceProjectionRow?>
        LoadEffectivePriceProjectionRowAsync(
            DbConnection connection,
            DbTransaction transaction,
            int variantId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = """
            SELECT
                v.[Id] AS [VariantId],
                v.[ProductId],
                p.[Id] AS [JoinedProductId],
                p.[Name] AS [ProductName],
                p.[IsActive] AS [ProductIsActive],
                v.[SKU],
                v.[Price] AS [ListPrice],
                v.[CurrentPrice],
                v.[CurrentPriceSourceType],
                v.[CurrentPriceSourceId],
                v.[CurrentPriceEffectiveFrom],
                v.[CurrentPriceEffectiveTo],
                v.[IsActive] AS [VariantIsActive],
                v.[StockQuantity],
                v.[RowVersion],
                winner.[CampaignId] AS [WinnerCampaignId],
                winner.[CampaignName] AS [WinnerCampaignName],
                winner.[SourceType] AS [WinnerSourceType],
                winner.[StartDate] AS [WinnerStartDate],
                winner.[EndDate] AS [WinnerEndDate],
                winner.[NewPrice] AS [WinnerNewPrice],
                winner.[CandidateCount]
            FROM dbo.[ProductVariants] AS v
            LEFT JOIN dbo.[Products] AS p
                ON p.[Id] = v.[ProductId]
            OUTER APPLY
            (
                SELECT TOP (1)
                    pci.[CampaignId],
                    pc.[Name] AS [CampaignName],
                    pc.[SourceType],
                    pc.[StartDate],
                    pc.[EndDate],
                    pci.[NewPrice],
                    COUNT_BIG(*) OVER () AS [CandidateCount]
                FROM dbo.[PriceCampaignItems] AS pci
                INNER JOIN dbo.[PriceCampaigns] AS pc
                    ON pc.[Id] = pci.[CampaignId]
                WHERE pci.[VariantId] = v.[Id]
                  AND pc.[Status] IN
                      ('Confirmed', 'Scheduled', 'Active')
                  AND pc.[StartDate] <= @nowUtc
                  AND
                  (
                      pc.[EndDate] IS NULL
                      OR pc.[EndDate] > @nowUtc
                  )
                  AND pci.[NewPrice] > 0
                ORDER BY
                    pc.[StartDate] DESC,
                    pci.[CampaignId] DESC
            ) AS winner
            WHERE v.[Id] = @variantId;
            """;

        AddParameter(
            command,
            "@variantId",
            variantId,
            DbType.Int32);
        AddParameter(
            command,
            "@nowUtc",
            nowUtc,
            DbType.DateTime2);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EffectivePriceProjectionRow(
            reader.GetInt32(reader.GetOrdinal("VariantId")),
            reader.GetInt32(reader.GetOrdinal("ProductId")),
            GetNullableInt32(reader, "JoinedProductId"),
            GetNullableString(reader, "ProductName"),
            GetNullableBoolean(reader, "ProductIsActive"),
            GetNullableString(reader, "SKU"),
            reader.GetDecimal(reader.GetOrdinal("ListPrice")),
            reader.GetDecimal(reader.GetOrdinal("CurrentPrice")),
            GetNullableString(reader, "CurrentPriceSourceType"),
            GetNullableInt32(reader, "CurrentPriceSourceId"),
            GetNullableDateTime(
                reader,
                "CurrentPriceEffectiveFrom"),
            GetNullableDateTime(
                reader,
                "CurrentPriceEffectiveTo"),
            reader.GetBoolean(
                reader.GetOrdinal("VariantIsActive")),
            reader.GetInt32(
                reader.GetOrdinal("StockQuantity")),
            GetNullableBytes(reader, "RowVersion"),
            GetNullableInt32(reader, "WinnerCampaignId"),
            GetNullableString(reader, "WinnerCampaignName"),
            GetNullableString(reader, "WinnerSourceType"),
            GetNullableDateTime(reader, "WinnerStartDate"),
            GetNullableDateTime(reader, "WinnerEndDate"),
            GetNullableDecimal(reader, "WinnerNewPrice"),
            GetNullableInt64(reader, "CandidateCount") ?? 0);
    }

    private static void ValidateEffectivePriceProjectionRow(
        EffectivePriceProjectionRow row)
    {
        if (!row.JoinedProductId.HasValue)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {row.Sku ?? $"ID {row.VariantId}"} "
                + $"tham chiếu ProductId {row.ProductId} không tồn tại.",
                "PRODUCT_REFERENCE_MISSING",
                row.VariantId,
                row.Sku);
        }

        if (string.IsNullOrWhiteSpace(row.ProductName))
        {
            throw new EffectivePriceRecalculationException(
                $"Sản phẩm ID {row.ProductId} của biến thể "
                + $"{row.Sku ?? row.VariantId.ToString()} không có tên hợp lệ.",
                "PRODUCT_NAME_INVALID",
                row.VariantId,
                row.Sku);
        }

        if (row.ProductIsActive != true)
        {
            throw new EffectivePriceRecalculationException(
                $"Sản phẩm {row.ProductName} (ID {row.ProductId}) "
                + "đang ngừng hoạt động.",
                "PRODUCT_INACTIVE_DURING_RECALCULATION",
                row.VariantId,
                row.Sku);
        }

        if (string.IsNullOrWhiteSpace(row.Sku))
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể ID {row.VariantId} không có SKU hợp lệ.",
                "VARIANT_SKU_INVALID",
                row.VariantId);
        }

        if (!row.VariantIsActive)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {row.Sku} (ID {row.VariantId}) "
                + "đang ngừng hoạt động.",
                "VARIANT_INACTIVE_DURING_RECALCULATION",
                row.VariantId,
                row.Sku);
        }

        if (row.ListPrice <= 0)
        {
            throw new EffectivePriceRecalculationException(
                $"Giá niêm yết của {row.Sku} phải lớn hơn 0 "
                + $"nhưng đang là {row.ListPrice}.",
                "VARIANT_LIST_PRICE_INVALID",
                row.VariantId,
                row.Sku);
        }

        if (row.CurrentPrice <= 0)
        {
            throw new EffectivePriceRecalculationException(
                $"Giá hiện tại của {row.Sku} phải lớn hơn 0 "
                + $"nhưng đang là {row.CurrentPrice}.",
                "VARIANT_CURRENT_PRICE_INVALID",
                row.VariantId,
                row.Sku);
        }

        if (row.StockQuantity < 0)
        {
            throw new EffectivePriceRecalculationException(
                $"Tồn kho của {row.Sku} không được âm "
                + $"nhưng đang là {row.StockQuantity}.",
                "VARIANT_STOCK_INVALID",
                row.VariantId,
                row.Sku);
        }

        if (row.RowVersion is not { Length: 8 })
        {
            throw new EffectivePriceRecalculationException(
                $"RowVersion của {row.Sku} không hợp lệ.",
                "VARIANT_ROW_VERSION_INVALID",
                row.VariantId,
                row.Sku);
        }

        var listPriceSource =
            EffectivePriceSourceType.ListPrice.ToString();
        var campaignSource =
            EffectivePriceSourceType.Campaign.ToString();

        if (!string.Equals(
                row.CurrentPriceSourceType,
                listPriceSource,
                StringComparison.Ordinal)
            && !string.Equals(
                row.CurrentPriceSourceType,
                campaignSource,
                StringComparison.Ordinal))
        {
            throw new EffectivePriceRecalculationException(
                $"CurrentPriceSourceType của {row.Sku} đang là "
                + $"“{row.CurrentPriceSourceType ?? "NULL"}”; "
                + "chỉ chấp nhận ListPrice hoặc Campaign.",
                "VARIANT_CURRENT_PRICE_SOURCE_INVALID",
                row.VariantId,
                row.Sku);
        }

        if (string.Equals(
                row.CurrentPriceSourceType,
                campaignSource,
                StringComparison.Ordinal)
            && !row.CurrentPriceSourceId.HasValue)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {row.Sku} có nguồn Campaign "
                + "nhưng CurrentPriceSourceId đang NULL.",
                "VARIANT_CAMPAIGN_SOURCE_ID_MISSING",
                row.VariantId,
                row.Sku);
        }

        if (string.Equals(
                row.CurrentPriceSourceType,
                listPriceSource,
                StringComparison.Ordinal)
            && row.CurrentPriceSourceId.HasValue)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {row.Sku} có nguồn ListPrice "
                + $"nhưng CurrentPriceSourceId đang là "
                + $"{row.CurrentPriceSourceId}.",
                "VARIANT_LIST_PRICE_SOURCE_ID_NOT_NULL",
                row.VariantId,
                row.Sku);
        }

        if (row.CandidateCount > 1)
        {
            throw new EffectivePriceRecalculationException(
                $"Biến thể {row.Sku} đang có "
                + $"{row.CandidateCount} kế hoạch cùng hiệu lực. "
                + "Dữ liệu chồng lấn phải được xử lý trước khi áp giá.",
                "MULTIPLE_EFFECTIVE_CAMPAIGNS",
                row.VariantId,
                row.Sku);
        }

        if (!row.WinnerCampaignId.HasValue)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                row.WinnerCampaignName))
        {
            throw new EffectivePriceRecalculationException(
                $"Kế hoạch thắng ID {row.WinnerCampaignId} "
                + $"của {row.Sku} không có tên hợp lệ.",
                "WINNER_CAMPAIGN_NAME_INVALID",
                row.VariantId,
                row.Sku);
        }

        if (!row.WinnerNewPrice.HasValue
            || row.WinnerNewPrice.Value <= 0)
        {
            throw new EffectivePriceRecalculationException(
                $"Giá kế hoạch thắng của {row.Sku} không hợp lệ.",
                "WINNER_CAMPAIGN_PRICE_INVALID",
                row.VariantId,
                row.Sku);
        }

        if (!row.WinnerStartDate.HasValue)
        {
            throw new EffectivePriceRecalculationException(
                $"Kế hoạch thắng của {row.Sku} thiếu StartDate.",
                "WINNER_CAMPAIGN_START_DATE_MISSING",
                row.VariantId,
                row.Sku);
        }

        var validSourceTypes = Enum.GetNames<
            PriceChangeSourceType>();

        if (string.IsNullOrWhiteSpace(
                row.WinnerSourceType)
            || !validSourceTypes.Contains(
                row.WinnerSourceType,
                StringComparer.Ordinal))
        {
            throw new EffectivePriceRecalculationException(
                $"SourceType “{row.WinnerSourceType ?? "NULL"}” "
                + $"của kế hoạch thắng cho {row.Sku} không hợp lệ.",
                "WINNER_CAMPAIGN_SOURCE_INVALID",
                row.VariantId,
                row.Sku);
        }
    }

    private static async Task InsertPriceHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        EffectivePriceProjectionRow row,
        decimal desiredPrice,
        string eventType,
        string sourceType,
        int? sourceId,
        string correlationId,
        string reason,
        DateTime? effectiveFrom,
        DateTime? effectiveTo,
        string changedBy,
        string note,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = """
            INSERT INTO dbo.[PriceHistories]
            (
                [ProductVariantId],
                [OldPrice],
                [NewPrice],
                [EventType],
                [SourceType],
                [SourceId],
                [CorrelationId],
                [Reason],
                [EffectiveFrom],
                [EffectiveTo],
                [ChangedBy],
                [Note],
                [CreatedAt],
                [UpdatedAt]
            )
            VALUES
            (
                @productVariantId,
                @oldPrice,
                @newPrice,
                @eventType,
                @sourceType,
                @sourceId,
                @correlationId,
                @reason,
                @effectiveFrom,
                @effectiveTo,
                @changedBy,
                @note,
                @createdAt,
                NULL
            );
            """;

        AddParameter(
            command,
            "@productVariantId",
            row.VariantId,
            DbType.Int32);
        AddMoneyParameter(
            command,
            "@oldPrice",
            row.CurrentPrice);
        AddMoneyParameter(
            command,
            "@newPrice",
            desiredPrice);
        AddParameter(
            command,
            "@eventType",
            eventType,
            DbType.String,
            30);
        AddParameter(
            command,
            "@sourceType",
            sourceType,
            DbType.String,
            30);
        AddParameter(
            command,
            "@sourceId",
            sourceId,
            DbType.Int32);
        AddParameter(
            command,
            "@correlationId",
            correlationId,
            DbType.String,
            CorrelationIdMaxLength);
        AddParameter(
            command,
            "@reason",
            reason,
            DbType.String,
            ReasonMaxLength);
        AddParameter(
            command,
            "@effectiveFrom",
            effectiveFrom,
            DbType.DateTime2);
        AddParameter(
            command,
            "@effectiveTo",
            effectiveTo,
            DbType.DateTime2);
        AddParameter(
            command,
            "@changedBy",
            changedBy,
            DbType.String,
            ChangedByMaxLength);
        AddParameter(
            command,
            "@note",
            note,
            DbType.String,
            NoteMaxLength);
        AddParameter(
            command,
            "@createdAt",
            nowUtc,
            DbType.DateTime2);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> UpdateVariantProjectionAsync(
        DbConnection connection,
        DbTransaction transaction,
        EffectivePriceProjectionRow row,
        decimal desiredPrice,
        string desiredSourceType,
        int? desiredSourceId,
        DateTime? desiredEffectiveFrom,
        DateTime? desiredEffectiveTo,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = """
            UPDATE dbo.[ProductVariants]
            SET
                [CurrentPrice] = @currentPrice,
                [CurrentPriceSourceType] = @sourceType,
                [CurrentPriceSourceId] = @sourceId,
                [CurrentPriceEffectiveFrom] = @effectiveFrom,
                [CurrentPriceEffectiveTo] = @effectiveTo,
                [UpdatedAt] = @updatedAt
            WHERE [Id] = @variantId
              AND [RowVersion] = @expectedRowVersion;

            SELECT @@ROWCOUNT;
            """;

        AddMoneyParameter(
            command,
            "@currentPrice",
            desiredPrice);
        AddParameter(
            command,
            "@sourceType",
            desiredSourceType,
            DbType.String,
            30);
        AddParameter(
            command,
            "@sourceId",
            desiredSourceId,
            DbType.Int32);
        AddParameter(
            command,
            "@effectiveFrom",
            desiredEffectiveFrom,
            DbType.DateTime2);
        AddParameter(
            command,
            "@effectiveTo",
            desiredEffectiveTo,
            DbType.DateTime2);
        AddParameter(
            command,
            "@updatedAt",
            nowUtc,
            DbType.DateTime2);
        AddParameter(
            command,
            "@variantId",
            row.VariantId,
            DbType.Int32);
        AddParameter(
            command,
            "@expectedRowVersion",
            row.RowVersion,
            DbType.Binary,
            8);

        var scalar = await command.ExecuteScalarAsync(
            cancellationToken);
        return Convert.ToInt32(scalar) == 1;
    }

    private static string ResolveHistoryEventTypeName(
        EffectivePriceProjectionRow row,
        int? desiredSourceId)
    {
        if (!desiredSourceId.HasValue)
        {
            return PriceHistoryEventType.Restored.ToString();
        }

        if (string.Equals(
                row.CurrentPriceSourceType,
                EffectivePriceSourceType.Campaign.ToString(),
                StringComparison.Ordinal)
            && row.CurrentPriceSourceId.HasValue
            && row.CurrentPriceSourceId.Value
                != desiredSourceId.Value)
        {
            return PriceHistoryEventType.Replaced.ToString();
        }

        return PriceHistoryEventType.Applied.ToString();
    }

    private static DbParameter AddParameter(
        DbCommand command,
        string name,
        object? value,
        DbType dbType,
        int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;

        if (size.HasValue)
        {
            parameter.Size = size.Value;
        }

        command.Parameters.Add(parameter);
        return parameter;
    }

    private static DbParameter AddMoneyParameter(
        DbCommand command,
        string name,
        decimal value)
    {
        var parameter = AddParameter(
            command,
            name,
            value,
            DbType.Decimal);
        parameter.Precision = 18;
        parameter.Scale = 2;
        return parameter;
    }

    private static string? GetNullableString(
        DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(
        DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);
    }

    private static long? GetNullableInt64(
        DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt64(ordinal);
    }

    private static bool? GetNullableBoolean(
        DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetBoolean(ordinal);
    }

    private static DateTime? GetNullableDateTime(
        DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetDateTime(ordinal);
    }

    private static decimal? GetNullableDecimal(
        DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetDecimal(ordinal);
    }

    private static byte[] GetNullableBytes(
        DbDataReader reader,
        string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? []
            : (byte[])reader.GetValue(ordinal);
    }

    private sealed record EffectivePriceProjectionRow(
        int VariantId,
        int ProductId,
        int? JoinedProductId,
        string? ProductName,
        bool? ProductIsActive,
        string? Sku,
        decimal ListPrice,
        decimal CurrentPrice,
        string? CurrentPriceSourceType,
        int? CurrentPriceSourceId,
        DateTime? CurrentPriceEffectiveFrom,
        DateTime? CurrentPriceEffectiveTo,
        bool VariantIsActive,
        int StockQuantity,
        byte[] RowVersion,
        int? WinnerCampaignId,
        string? WinnerCampaignName,
        string? WinnerSourceType,
        DateTime? WinnerStartDate,
        DateTime? WinnerEndDate,
        decimal? WinnerNewPrice,
        long CandidateCount);

    private static PriceHistoryEventType ResolveHistoryEventType(
        ProductVariant variant,
        EffectiveCampaignCandidate? winner)
    {
        if (winner is null)
        {
            return PriceHistoryEventType.Restored;
        }

        if (variant.CurrentPriceSourceType == EffectivePriceSourceType.Campaign
            && variant.CurrentPriceSourceId.HasValue
            && variant.CurrentPriceSourceId.Value != winner.CampaignId)
        {
            return PriceHistoryEventType.Replaced;
        }

        return PriceHistoryEventType.Applied;
    }

    private static decimal CalculateTargetPrice(
        decimal listPrice,
        PriceAdjustmentType adjustmentType,
        decimal adjustmentValue,
        string sku)
    {
        if (adjustmentValue <= 0)
        {
            throw new InvalidPriceAdjustmentException(
                $"Giá trị điều chỉnh của SKU {sku} phải lớn hơn 0.");
        }

        decimal rawPrice = adjustmentType switch
        {
            PriceAdjustmentType.FixedPrice => adjustmentValue,
            PriceAdjustmentType.PercentOff
                when adjustmentValue < 100m
                => listPrice * (1m - adjustmentValue / 100m),
            PriceAdjustmentType.PercentOff
                => throw new InvalidPriceAdjustmentException(
                    $"Phần trăm giảm của SKU {sku} phải nhỏ hơn 100%."),
            PriceAdjustmentType.AmountOff
                when adjustmentValue < listPrice
                => listPrice - adjustmentValue,
            PriceAdjustmentType.AmountOff
                => throw new InvalidPriceAdjustmentException(
                    $"Số tiền giảm của SKU {sku} phải nhỏ hơn giá niêm yết."),
            _ => throw new InvalidPriceAdjustmentException(
                $"Kiểu điều chỉnh giá của SKU {sku} không hợp lệ.")
        };

        var roundedPrice = decimal.Round(
            rawPrice,
            0,
            MidpointRounding.AwayFromZero);

        if (roundedPrice <= 0 || roundedPrice > MaximumMoney)
        {
            throw new InvalidPriceAdjustmentException(
                $"Giá tính được của SKU {sku} không hợp lệ.");
        }

        return roundedPrice;
    }

    private static string TruncateRequired(
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

    private sealed class InvalidPriceAdjustmentException : Exception
    {
        public InvalidPriceAdjustmentException(string message)
            : base(message)
        {
        }
    }

    private sealed record EffectiveCampaignCandidate(
        int VariantId,
        int CampaignId,
        string CampaignName,
        PriceChangeSourceType SourceType,
        DateTime StartDate,
        DateTime? EndDate,
        decimal NewPrice);
}

public sealed class EffectivePriceRecalculationException : Exception
{
    public EffectivePriceRecalculationException(
        string message,
        string errorCode,
        int? variantId = null,
        string? sku = null)
        : base(message)
    {
        ErrorCode = errorCode;
        VariantId = variantId;
        Sku = sku;
    }

    public string ErrorCode { get; }

    public int? VariantId { get; }

    public string? Sku { get; }
}

