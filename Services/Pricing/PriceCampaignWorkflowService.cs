using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public sealed class PriceCampaignWorkflowService : IPriceCampaignWorkflowService
{
    private readonly ApplicationDbContext _context;
    private readonly IEffectivePriceService _effectivePriceService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PriceCampaignWorkflowService> _logger;

    public PriceCampaignWorkflowService(
        ApplicationDbContext context,
        IEffectivePriceService effectivePriceService,
        TimeProvider timeProvider,
        ILogger<PriceCampaignWorkflowService> logger)
    {
        _context = context;
        _effectivePriceService = effectivePriceService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PriceCampaignWorkflowResult> SaveDraftAsync(
        PriceCampaignDraftCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        PricePlanPreviewResult? preview = null;

        try
        {
            preview = await _effectivePriceService.PreviewCampaignAsync(
                command.CampaignId,
                command.Mode,
                command.StartDateUtc,
                command.EndDateUtc,
                command.ConflictPolicy,
                command.Items,
                cancellationToken);

            if (!preview.IsValid)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    preview.ErrorMessage ?? "Dữ liệu cấu hình giá không hợp lệ.",
                    preview.ErrorCode ?? "PREVIEW_FAILED",
                    preview,
                    command.CampaignId);
            }

            PriceCampaign campaign;
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

            if (command.CampaignId == 0)
            {
                var existingByRequest = await _context.PriceCampaigns
                    .Include(item => item.CampaignItems)
                    .FirstOrDefaultAsync(
                        item => item.ClientRequestId == command.ClientRequestId,
                        cancellationToken);

                if (existingByRequest is not null)
                {
                    var idempotencyReview = ReviewIdempotentDraft(
                        existingByRequest,
                        command);

                    if (!idempotencyReview.IsMatch)
                    {
                        await transaction.RollbackAsync(CancellationToken.None);

                        _logger.LogWarning(
                            "Pricing idempotency key was reused with a different payload. ExistingCampaignId={CampaignId}, CorrelationId={CorrelationId}, Mismatch={Mismatch}.",
                            existingByRequest.Id,
                            command.CorrelationId,
                            idempotencyReview.Mismatch);

                        return PriceCampaignWorkflowResult.Failure(
                            "Khóa yêu cầu này đã được sử dụng cho một bản nháp có nội dung khác. Vui lòng tải lại trang để tạo khóa yêu cầu mới.",
                            "IDEMPOTENCY_KEY_REUSED",
                            campaignId: existingByRequest.Id);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return Success(
                        existingByRequest,
                        "Bản nháp này đã được lưu trước đó.",
                        null);
                }

                campaign = new PriceCampaign
                {
                    Code = CreateCampaignCode(nowUtc),
                    Status = PriceCampaignStatus.Draft,
                    IsActive = false,
                    ClientRequestId = command.ClientRequestId,
                    CreatedBy = NormalizeActor(command.Actor),
                    CreatedAt = nowUtc,
                    CampaignItems = []
                };
                _context.PriceCampaigns.Add(campaign);
            }
            else
            {
                campaign = await _context.PriceCampaigns
                    .Include(item => item.CampaignItems)
                    .FirstOrDefaultAsync(
                        item => item.Id == command.CampaignId,
                        cancellationToken)
                    ?? throw new PriceCampaignWorkflowException(
                        "Không tìm thấy bản nháp kế hoạch giá.",
                        "CAMPAIGN_NOT_FOUND");

                if (campaign.Status != PriceCampaignStatus.Draft)
                {
                    throw new PriceCampaignWorkflowException(
                        "Chỉ bản nháp mới có thể được lưu bằng thao tác này.",
                        "CAMPAIGN_NOT_DRAFT");
                }

                ApplyExpectedRowVersion(
                    campaign,
                    command.ExpectedCampaignRowVersion);
                campaign.UpdatedAt = nowUtc;
            }

            campaign.Name = command.Name.Trim();
            campaign.Description = CleanNullable(command.Description);
            campaign.Mode = command.Mode;
            campaign.StartDate = command.StartDateUtc;
            campaign.EndDate = command.EndDateUtc;
            campaign.Reason = command.Reason.Trim();
            campaign.SourceType = command.SourceType;
            campaign.ConflictPolicy = command.ConflictPolicy;
            campaign.Status = PriceCampaignStatus.Draft;
            campaign.IsActive = false;
            campaign.ConfirmedAt = null;
            campaign.ConfirmedBy = null;

            SynchronizeItems(campaign, preview.Items);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Pricing draft saved. CampaignId={CampaignId}, CorrelationId={CorrelationId}, VariantCount={VariantCount}.",
                campaign.Id,
                command.CorrelationId,
                campaign.CampaignItems.Count);

            return Success(
                campaign,
                preview.CanConfirm
                    ? "Đã lưu bản nháp. Kế hoạch sẵn sàng để xác nhận."
                    : "Đã lưu bản nháp nhưng còn xung đột cần xử lý trước khi xác nhận.",
                preview);
        }
        catch (PriceCampaignWorkflowException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return PriceCampaignWorkflowResult.Failure(
                exception.Message,
                exception.ErrorCode,
                preview,
                command.CampaignId);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Pricing draft concurrency conflict. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Dữ liệu vừa được thay đổi ở nơi khác. Vui lòng tải lại trước khi lưu.",
                "CONCURRENCY_CONFLICT",
                preview,
                command.CampaignId);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Pricing draft database failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể ghi bản nháp kế hoạch giá.",
                "DATABASE_WRITE_FAILED",
                preview,
                command.CampaignId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Unexpected pricing draft failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể lưu bản nháp kế hoạch giá.",
                "UNEXPECTED_ERROR",
                preview,
                command.CampaignId);
        }
    }

    public async Task<PriceCampaignWorkflowResult> ConfirmDraftAsync(
        ConfirmPriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var campaign = await _context.PriceCampaigns
                .Include(item => item.CampaignItems)
                .FirstOrDefaultAsync(
                    item => item.Id == command.CampaignId,
                    cancellationToken);

            if (campaign is null)
            {
                return PriceCampaignWorkflowResult.Failure(
                    "Không tìm thấy bản nháp kế hoạch giá.",
                    "CAMPAIGN_NOT_FOUND",
                    campaignId: command.CampaignId);
            }

            if (campaign.Status is PriceCampaignStatus.Confirmed
                or PriceCampaignStatus.Scheduled
                or PriceCampaignStatus.Active)
            {
                await transaction.CommitAsync(cancellationToken);
                return Success(
                    campaign,
                    "Kế hoạch này đã được xác nhận trước đó.",
                    null);
            }

            if (campaign.Status != PriceCampaignStatus.Draft)
            {
                return PriceCampaignWorkflowResult.Failure(
                    "Kế hoạch đã kết thúc, bị hủy hoặc bị thay thế nên không thể xác nhận.",
                    "CAMPAIGN_NOT_CONFIRMABLE",
                    campaignId: command.CampaignId);
            }

            ApplyExpectedRowVersion(
                campaign,
                command.ExpectedCampaignRowVersion);

            var preview = await _effectivePriceService.PreviewCampaignAsync(
                campaign.Id,
                campaign.Mode,
                campaign.StartDate,
                campaign.EndDate,
                campaign.ConflictPolicy,
                campaign.CampaignItems
                    .Select(item => new PricePlanPreviewInput(
                        item.VariantId,
                        item.AdjustmentType,
                        item.AdjustmentValue))
                    .ToArray(),
                cancellationToken);

            if (!preview.IsValid || !preview.CanConfirm)
            {
                return PriceCampaignWorkflowResult.Failure(
                    preview.ErrorMessage
                        ?? "Kế hoạch chưa đủ điều kiện để xác nhận.",
                    preview.ErrorCode
                        ?? "CAMPAIGN_NOT_CONFIRMABLE",
                    preview,
                    command.CampaignId);
            }

            var snapshotReview = ReviewDraftSnapshots(
                campaign.CampaignItems,
                preview.Items);

            if (!snapshotReview.IsCurrent)
            {
                _logger.LogWarning(
                    "Pricing draft requires review before confirmation. CampaignId={CampaignId}, CorrelationId={CorrelationId}, ChangedItemCount={ChangedItemCount}.",
                    campaign.Id,
                    command.CorrelationId,
                    snapshotReview.ChangedItemCount);

                return PriceCampaignWorkflowResult.Failure(
                    $"Có {snapshotReview.ChangedItemCount} biến thể đã thay đổi giá hoặc dữ liệu cấu hình kể từ lần lưu bản nháp. Vui lòng mở lại bản nháp, kiểm tra preview mới và lưu lại trước khi xác nhận.",
                    "DRAFT_REVIEW_REQUIRED",
                    preview,
                    command.CampaignId);
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            campaign.Status = PriceCampaignLifecycle.ResolveConfirmedStatus(
                campaign.StartDate,
                campaign.EndDate,
                nowUtc);
            campaign.IsActive = PriceCampaignLifecycle.IsCompatibilityActive(
                campaign.Status);
            campaign.ConfirmedAt = nowUtc;
            campaign.ConfirmedBy = NormalizeActor(command.Actor);
            campaign.UpdatedAt = nowUtc;

            await _context.SaveChangesAsync(cancellationToken);

            await _effectivePriceService.RecalculateVariantsAsync(
                campaign.CampaignItems
                    .Select(item => item.VariantId)
                    .Distinct()
                    .ToArray(),
                NormalizeActor(command.Actor),
                "Xác nhận kế hoạch giá từ danh sách chờ",
                command.CorrelationId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Pricing draft confirmed. CampaignId={CampaignId}, Status={Status}, CorrelationId={CorrelationId}.",
                campaign.Id,
                campaign.Status,
                command.CorrelationId);

            return Success(
                campaign,
                campaign.Status == PriceCampaignStatus.Active
                    ? "Kế hoạch đã được xác nhận và giá đã có hiệu lực."
                    : "Kế hoạch đã được xác nhận và đưa vào lịch chờ.",
                preview);
        }
        catch (PriceCampaignWorkflowException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return PriceCampaignWorkflowResult.Failure(
                exception.Message,
                exception.ErrorCode,
                campaignId: command.CampaignId);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Pricing confirmation concurrency conflict. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Bản nháp vừa được thay đổi ở nơi khác. Vui lòng tải lại trước khi xác nhận.",
                "CONCURRENCY_CONFLICT",
                campaignId: command.CampaignId);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Pricing confirmation database failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể xác nhận kế hoạch giá.",
                "DATABASE_WRITE_FAILED",
                campaignId: command.CampaignId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Unexpected pricing confirmation failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể xác nhận kế hoạch giá.",
                "UNEXPECTED_ERROR",
                campaignId: command.CampaignId);
        }
    }

    private static IdempotencyReview ReviewIdempotentDraft(
        PriceCampaign campaign,
        PriceCampaignDraftCommand command)
    {
        if (!string.Equals(
                campaign.Name,
                command.Name.Trim(),
                StringComparison.Ordinal)
            || !string.Equals(
                CleanNullable(campaign.Description),
                CleanNullable(command.Description),
                StringComparison.Ordinal)
            || campaign.Mode != command.Mode
            || campaign.StartDate != command.StartDateUtc
            || campaign.EndDate != command.EndDateUtc
            || !string.Equals(
                campaign.Reason,
                command.Reason.Trim(),
                StringComparison.Ordinal)
            || campaign.SourceType != command.SourceType
            || campaign.ConflictPolicy != command.ConflictPolicy)
        {
            return new IdempotencyReview(
                false,
                "CAMPAIGN_METADATA");
        }

        if (campaign.CampaignItems.Count != command.Items.Count)
        {
            return new IdempotencyReview(
                false,
                "ITEM_COUNT");
        }

        var requestedByVariantId = command.Items.ToDictionary(
            item => item.VariantId);

        foreach (var campaignItem in campaign.CampaignItems)
        {
            if (!requestedByVariantId.TryGetValue(
                    campaignItem.VariantId,
                    out var requestedItem))
            {
                return new IdempotencyReview(
                    false,
                    $"MISSING_VARIANT:{campaignItem.VariantId}");
            }

            if (campaignItem.AdjustmentType
                    != requestedItem.AdjustmentType
                || campaignItem.AdjustmentValue
                    != requestedItem.AdjustmentValue)
            {
                return new IdempotencyReview(
                    false,
                    $"ITEM_CONFIGURATION:{campaignItem.VariantId}");
            }
        }

        return IdempotencyReview.Match;
    }

    private static DraftSnapshotReview ReviewDraftSnapshots(
        IReadOnlyCollection<PriceCampaignItem> draftItems,
        IReadOnlyList<PricePlanPreviewItemResult> previewItems)
    {
        if (draftItems.Count != previewItems.Count)
        {
            return new DraftSnapshotReview(
                false,
                Math.Max(draftItems.Count, previewItems.Count));
        }

        var previewByVariantId = previewItems.ToDictionary(
            item => item.VariantId);
        var changedItemCount = 0;

        foreach (var draftItem in draftItems)
        {
            if (!previewByVariantId.TryGetValue(
                    draftItem.VariantId,
                    out var previewItem))
            {
                changedItemCount++;
                continue;
            }

            if (draftItem.ListPriceSnapshot != previewItem.ListPrice
                || draftItem.EffectivePriceSnapshot != previewItem.CurrentPrice
                || draftItem.PreviousEffectivePriceSnapshot != previewItem.CurrentPrice
                || draftItem.NewPrice != previewItem.NewPrice
                || draftItem.AdjustmentType != previewItem.AdjustmentType
                || draftItem.AdjustmentValue != previewItem.AdjustmentValue)
            {
                changedItemCount++;
            }
        }

        return new DraftSnapshotReview(
            changedItemCount == 0,
            changedItemCount);
    }

    private void SynchronizeItems(
        PriceCampaign campaign,
        IReadOnlyList<PricePlanPreviewItemResult> previewItems)
    {
        var previewByVariantId = previewItems.ToDictionary(item => item.VariantId);
        var removedItems = campaign.CampaignItems
            .Where(item => !previewByVariantId.ContainsKey(item.VariantId))
            .ToArray();

        foreach (var removedItem in removedItems)
        {
            _context.PriceCampaignItems.Remove(removedItem);
            campaign.CampaignItems.Remove(removedItem);
        }

        var existingByVariantId = campaign.CampaignItems
            .ToDictionary(item => item.VariantId);

        foreach (var previewItem in previewItems)
        {
            if (!existingByVariantId.TryGetValue(
                    previewItem.VariantId,
                    out var item))
            {
                item = new PriceCampaignItem
                {
                    VariantId = previewItem.VariantId
                };
                campaign.CampaignItems.Add(item);
            }

            item.ListPriceSnapshot = previewItem.ListPrice;
            item.EffectivePriceSnapshot = previewItem.CurrentPrice;
            item.PreviousEffectivePriceSnapshot = previewItem.CurrentPrice;
            item.AdjustmentType = previewItem.AdjustmentType;
            item.AdjustmentValue = previewItem.AdjustmentValue;
            item.NewPrice = previewItem.NewPrice;
            item.Currency = "VND";
        }
    }

    private void ApplyExpectedRowVersion(
        PriceCampaign campaign,
        byte[]? expectedRowVersion)
    {
        if (expectedRowVersion is not { Length: 8 })
        {
            throw new PriceCampaignWorkflowException(
                "Thiếu hoặc sai phiên bản dữ liệu. Vui lòng tải lại trang.",
                "INVALID_ROW_VERSION");
        }

        _context.Entry(campaign)
            .Property(item => item.RowVersion)
            .OriginalValue = expectedRowVersion;
    }

    private static PriceCampaignWorkflowResult Success(
        PriceCampaign campaign,
        string message,
        PricePlanPreviewResult? preview)
    {
        return new PriceCampaignWorkflowResult(
            true,
            message,
            null,
            campaign.Id,
            campaign.Code,
            campaign.Status,
            campaign.RowVersion,
            campaign.ClientRequestId ?? string.Empty,
            preview);
    }

    private static string NormalizeActor(string? actor)
    {
        var normalized = string.IsNullOrWhiteSpace(actor)
            ? "Admin UI"
            : actor.Trim();
        return normalized.Length <= 100
            ? normalized
            : normalized[..100];
    }

    private static string? CleanNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string CreateCampaignCode(DateTime nowUtc)
    {
        var suffix = Guid.NewGuid()
            .ToString("N")[..10]
            .ToUpperInvariant();
        return $"PC-{nowUtc:yyyyMMdd}-{suffix}";
    }

    private readonly record struct IdempotencyReview(
        bool IsMatch,
        string? Mismatch)
    {
        public static IdempotencyReview Match { get; }
            = new(true, null);
    }

    private readonly record struct DraftSnapshotReview(
        bool IsCurrent,
        int ChangedItemCount);

    private sealed class PriceCampaignWorkflowException : Exception
    {
        public PriceCampaignWorkflowException(
            string message,
            string errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
