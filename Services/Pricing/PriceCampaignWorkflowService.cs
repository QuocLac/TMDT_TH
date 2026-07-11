using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public sealed class PriceCampaignWorkflowService : IPriceCampaignWorkflowService
{
    private static readonly PriceCampaignStatus[] BlockingStatuses =
    [
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

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
                    Code = CreateCampaignCode("PC", nowUtc),
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
                        "Chỉ bản nháp mới có thể được chỉnh sửa.",
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
            campaign.Reason = NormalizeReason(command.Reason);
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
                "Pricing draft saved. CampaignId={CampaignId}, CorrelationId={CorrelationId}, VariantCount={VariantCount}, ConflictPolicy={ConflictPolicy}.",
                campaign.Id,
                command.CorrelationId,
                campaign.CampaignItems.Count,
                campaign.ConflictPolicy);

            return Success(
                campaign,
                preview.CanConfirm
                    ? preview.ErrorMessage
                        ?? "Đã lưu bản nháp. Kế hoạch sẵn sàng để xác nhận."
                    : preview.ErrorMessage
                        ?? "Đã lưu bản nháp nhưng còn xung đột cần xử lý trước khi xác nhận.",
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
            var campaign = await LoadCampaignForWorkflowAsync(
                command.CampaignId,
                cancellationToken);

            if (campaign is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
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
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Kế hoạch đã kết thúc, bị hủy hoặc bị thay thế nên không thể xác nhận.",
                    "CAMPAIGN_NOT_CONFIRMABLE",
                    campaignId: command.CampaignId);
            }

            ApplyExpectedRowVersion(
                campaign,
                command.ExpectedCampaignRowVersion);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var effectiveStartUtc = campaign.ConflictPolicy == PriceConflictPolicy.SupersedeNow
                ? nowUtc
                : campaign.StartDate;

            if (campaign.EndDate.HasValue
                && campaign.EndDate.Value <= nowUtc)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Kế hoạch đã hết thời gian hiệu lực. Hãy cập nhật thời gian trước khi xác nhận.",
                    "CAMPAIGN_EXPIRED",
                    campaignId: command.CampaignId);
            }

            var preview = await _effectivePriceService.PreviewCampaignAsync(
                campaign.Id,
                campaign.Mode,
                effectiveStartUtc,
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
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    preview.ErrorMessage
                        ?? "Kế hoạch chưa đủ điều kiện để xác nhận.",
                    preview.ErrorCode
                        ?? "CAMPAIGN_NOT_CONFIRMABLE",
                    preview,
                    command.CampaignId);
            }

            var snapshotReview = ReviewDraftSnapshots(
                (IReadOnlyCollection<PriceCampaignItem>)campaign.CampaignItems,
                preview.Items);

            if (!snapshotReview.IsCurrent)
            {
                await transaction.RollbackAsync(CancellationToken.None);

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

            campaign.StartDate = effectiveStartUtc;
            var conflictResolution = await ApplyConflictPolicyAsync(
                campaign,
                nowUtc,
                NormalizeActor(command.Actor),
                cancellationToken);

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
                conflictResolution.AffectedVariantIds,
                NormalizeActor(command.Actor),
                BuildConfirmationReason(campaign),
                command.CorrelationId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Pricing draft confirmed. CampaignId={CampaignId}, Status={Status}, ConflictPolicy={ConflictPolicy}, CorrelationId={CorrelationId}.",
                campaign.Id,
                campaign.Status,
                campaign.ConflictPolicy,
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

            var databaseFailure = MapConfirmationDatabaseFailure(exception);
            var sqlException = FindSqlException(exception);

            _logger.LogError(
                exception,
                "Pricing confirmation database failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}, SqlNumber={SqlNumber}, ErrorCode={ErrorCode}.",
                command.CampaignId,
                command.CorrelationId,
                sqlException?.Number,
                databaseFailure.ErrorCode);

            return PriceCampaignWorkflowResult.Failure(
                databaseFailure.Message,
                databaseFailure.ErrorCode,
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

    public async Task<PriceCampaignWorkflowResult> ActivateNowAsync(
        ActivatePriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var campaign = await LoadCampaignForWorkflowAsync(
                command.CampaignId,
                cancellationToken);

            if (campaign is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Không tìm thấy kế hoạch giá.",
                    "CAMPAIGN_NOT_FOUND",
                    campaignId: command.CampaignId);
            }

            if (campaign.Status == PriceCampaignStatus.Active)
            {
                await transaction.CommitAsync(cancellationToken);
                return Success(campaign, "Kế hoạch đã đang hiệu lực.", null);
            }

            if (campaign.Status is not PriceCampaignStatus.Confirmed
                and not PriceCampaignStatus.Scheduled)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Chỉ kế hoạch đã xác nhận hoặc đã lên lịch mới có thể kích hoạt ngay.",
                    "CAMPAIGN_NOT_ACTIVATABLE",
                    campaignId: command.CampaignId);
            }

            ApplyExpectedRowVersion(
                campaign,
                command.ExpectedCampaignRowVersion);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (campaign.EndDate.HasValue && campaign.EndDate.Value <= nowUtc)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Kế hoạch đã hết hạn. Hãy tạo kế hoạch thay thế.",
                    "CAMPAIGN_EXPIRED",
                    campaignId: command.CampaignId);
            }

            var preview = await _effectivePriceService.PreviewCampaignAsync(
                campaign.Id,
                campaign.Mode,
                nowUtc,
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
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    preview.ErrorMessage ?? "Không thể kích hoạt kế hoạch.",
                    preview.ErrorCode ?? "CAMPAIGN_NOT_ACTIVATABLE",
                    preview,
                    campaign.Id);
            }

            var snapshotReview = ReviewDraftSnapshots(
                (IReadOnlyCollection<PriceCampaignItem>)campaign.CampaignItems,
                preview.Items);
            if (!snapshotReview.IsCurrent)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    $"Có {snapshotReview.ChangedItemCount} biến thể đã đổi giá kể từ khi kế hoạch được xác nhận. Hãy tạo kế hoạch thay thế để tiếp tục.",
                    "DRAFT_REVIEW_REQUIRED",
                    preview,
                    campaign.Id);
            }

            campaign.StartDate = nowUtc;
            var conflictResolution = await ApplyConflictPolicyAsync(
                campaign,
                nowUtc,
                NormalizeActor(command.Actor),
                cancellationToken);

            campaign.Status = PriceCampaignStatus.Active;
            campaign.IsActive = true;
            campaign.ConfirmedAt ??= nowUtc;
            campaign.ConfirmedBy ??= NormalizeActor(command.Actor);
            campaign.UpdatedAt = nowUtc;

            await _context.SaveChangesAsync(cancellationToken);

            await _effectivePriceService.RecalculateVariantsAsync(
                conflictResolution.AffectedVariantIds,
                NormalizeActor(command.Actor),
                "Kích hoạt kế hoạch giá ngay",
                command.CorrelationId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return Success(
                campaign,
                "Đã kích hoạt kế hoạch giá.",
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
                "Pricing activation concurrency conflict. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Kế hoạch vừa được thay đổi ở nơi khác. Vui lòng tải lại.",
                "CONCURRENCY_CONFLICT",
                campaignId: command.CampaignId);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Pricing activation database failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể kích hoạt kế hoạch giá.",
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
                "Unexpected pricing activation failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể kích hoạt kế hoạch giá.",
                "UNEXPECTED_ERROR",
                campaignId: command.CampaignId);
        }
    }

    public async Task<PriceCampaignWorkflowResult> CancelAsync(
        CancelPriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var campaign = await LoadCampaignForWorkflowAsync(
                command.CampaignId,
                cancellationToken);

            if (campaign is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Không tìm thấy kế hoạch giá.",
                    "CAMPAIGN_NOT_FOUND",
                    campaignId: command.CampaignId);
            }

            if (campaign.Status == PriceCampaignStatus.Cancelled)
            {
                await transaction.CommitAsync(cancellationToken);
                return Success(campaign, "Kế hoạch đã được hủy trước đó.", null);
            }

            if (campaign.Status is PriceCampaignStatus.Completed
                or PriceCampaignStatus.Superseded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Kế hoạch đã kết thúc hoặc đã bị thay thế nên không thể hủy.",
                    "CAMPAIGN_NOT_CANCELLABLE",
                    campaignId: command.CampaignId);
            }

            ApplyExpectedRowVersion(
                campaign,
                command.ExpectedCampaignRowVersion);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var actor = NormalizeActor(command.Actor);
            var reason = NormalizeReason(command.Reason);
            var variantIds = campaign.CampaignItems
                .Select(item => item.VariantId)
                .Distinct()
                .ToArray();

            campaign.Status = PriceCampaignStatus.Cancelled;
            campaign.IsActive = false;
            campaign.CancelledAt = nowUtc;
            campaign.CancelledBy = actor;
            campaign.UpdatedAt = nowUtc;

            await _context.SaveChangesAsync(cancellationToken);

            var recalculation = await _effectivePriceService.RecalculateVariantsAsync(
                variantIds,
                actor,
                reason,
                command.CorrelationId,
                new PriceHistoryWriteContext(
                    PriceHistoryEventType.Cancelled,
                    campaign.SourceType,
                    campaign.Id),
                cancellationToken);

            if (recalculation.ChangedCount == 0)
            {
                await AddNoChangeCancellationAuditAsync(
                    campaign,
                    actor,
                    reason,
                    command.CorrelationId,
                    nowUtc,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Pricing campaign cancelled. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                campaign.Id,
                command.CorrelationId);

            return Success(
                campaign,
                "Đã hủy kế hoạch và đồng bộ lại giá hiệu lực.",
                null);
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
                "Pricing cancellation concurrency conflict. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Kế hoạch vừa được thay đổi ở nơi khác. Vui lòng tải lại.",
                "CONCURRENCY_CONFLICT",
                campaignId: command.CampaignId);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Pricing cancellation database failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể hủy kế hoạch giá.",
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
                "Unexpected pricing cancellation failure. CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.CampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể hủy kế hoạch giá.",
                "UNEXPECTED_ERROR",
                campaignId: command.CampaignId);
        }
    }

    public async Task<PriceCampaignWorkflowResult> RecoverAsync(
        RecoverPriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var sourceCampaign = await LoadCampaignForWorkflowAsync(
                command.SourceCampaignId,
                cancellationToken);

            if (sourceCampaign is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Không tìm thấy kế hoạch nguồn để phục hồi.",
                    "CAMPAIGN_NOT_FOUND",
                    campaignId: command.SourceCampaignId);
            }

            if (sourceCampaign.Status == PriceCampaignStatus.Draft)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Bản nháp chưa từng tác động đến giá nên không thể dùng để phục hồi.",
                    "DRAFT_CANNOT_RECOVER",
                    campaignId: sourceCampaign.Id);
            }

            var marker = CreateRecoveryMarker(sourceCampaign);
            var existingByRequest = await _context.PriceCampaigns
                .Include(item => item.CampaignItems)
                .FirstOrDefaultAsync(
                    item => item.ClientRequestId == command.ClientRequestId,
                    cancellationToken);

            if (existingByRequest is not null)
            {
                if (existingByRequest.SourceType == PriceChangeSourceType.Recovery
                    && string.Equals(
                        existingByRequest.Description,
                        marker,
                        StringComparison.Ordinal)
                    && string.Equals(
                        existingByRequest.Reason,
                        NormalizeReason(command.Reason),
                        StringComparison.Ordinal))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Success(
                        existingByRequest,
                        "Yêu cầu phục hồi này đã được xử lý trước đó.",
                        null);
                }

                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Khóa yêu cầu phục hồi đã được dùng cho thao tác khác. Vui lòng tải lại trang.",
                    "IDEMPOTENCY_KEY_REUSED",
                    campaignId: existingByRequest.Id);
            }

            ApplyExpectedRowVersion(
                sourceCampaign,
                command.ExpectedSourceCampaignRowVersion);

            var recoveryInputs = sourceCampaign.CampaignItems
                .Select(item => new PricePlanPreviewInput(
                    item.VariantId,
                    PriceAdjustmentType.FixedPrice,
                    ResolveRecoveryPrice(item)))
                .ToArray();

            if (recoveryInputs.Length == 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    "Kế hoạch nguồn không có biến thể để phục hồi.",
                    "EMPTY_ITEMS",
                    campaignId: sourceCampaign.Id);
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var preview = await _effectivePriceService.PreviewCampaignAsync(
                0,
                PriceCampaignMode.OpenEnded,
                nowUtc,
                null,
                PriceConflictPolicy.SupersedeNow,
                recoveryInputs,
                cancellationToken);

            if (!preview.IsValid || !preview.CanConfirm)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return PriceCampaignWorkflowResult.Failure(
                    preview.ErrorMessage ?? "Không thể tạo kế hoạch phục hồi.",
                    preview.ErrorCode ?? "RECOVERY_NOT_AVAILABLE",
                    preview,
                    sourceCampaign.Id);
            }

            var actor = NormalizeActor(command.Actor);
            var recoveryCampaign = new PriceCampaign
            {
                Code = CreateCampaignCode("RC", nowUtc),
                Name = Truncate(
                    $"Phục hồi từ {sourceCampaign.Code}",
                    255),
                Description = marker,
                Mode = PriceCampaignMode.OpenEnded,
                Status = PriceCampaignStatus.Active,
                StartDate = nowUtc,
                EndDate = null,
                Reason = NormalizeReason(command.Reason),
                SourceType = PriceChangeSourceType.Recovery,
                ConflictPolicy = PriceConflictPolicy.SupersedeNow,
                ClientRequestId = command.ClientRequestId,
                ConfirmedAt = nowUtc,
                ConfirmedBy = actor,
                CreatedBy = actor,
                CreatedAt = nowUtc,
                IsActive = true,
                CampaignItems = []
            };

            _context.PriceCampaigns.Add(recoveryCampaign);
            SynchronizeItems(recoveryCampaign, preview.Items);
            await _context.SaveChangesAsync(cancellationToken);

            var conflictResolution = await ApplyConflictPolicyAsync(
                recoveryCampaign,
                nowUtc,
                actor,
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await _effectivePriceService.RecalculateVariantsAsync(
                conflictResolution.AffectedVariantIds,
                actor,
                recoveryCampaign.Reason,
                command.CorrelationId,
                new PriceHistoryWriteContext(
                    PriceHistoryEventType.Restored,
                    PriceChangeSourceType.Recovery,
                    recoveryCampaign.Id),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Pricing recovery campaign created. SourceCampaignId={SourceCampaignId}, RecoveryCampaignId={RecoveryCampaignId}, CorrelationId={CorrelationId}.",
                sourceCampaign.Id,
                recoveryCampaign.Id,
                command.CorrelationId);

            return Success(
                recoveryCampaign,
                "Đã tạo và áp dụng kế hoạch phục hồi giá cũ.",
                preview);
        }
        catch (PriceCampaignWorkflowException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return PriceCampaignWorkflowResult.Failure(
                exception.Message,
                exception.ErrorCode,
                campaignId: command.SourceCampaignId);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Pricing recovery concurrency conflict. SourceCampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.SourceCampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Kế hoạch nguồn vừa được thay đổi ở nơi khác. Vui lòng tải lại.",
                "CONCURRENCY_CONFLICT",
                campaignId: command.SourceCampaignId);
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Pricing recovery database failure. SourceCampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.SourceCampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể tạo kế hoạch phục hồi.",
                "DATABASE_WRITE_FAILED",
                campaignId: command.SourceCampaignId);
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
                "Unexpected pricing recovery failure. SourceCampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                command.SourceCampaignId,
                command.CorrelationId);
            return PriceCampaignWorkflowResult.Failure(
                "Không thể tạo kế hoạch phục hồi.",
                "UNEXPECTED_ERROR",
                campaignId: command.SourceCampaignId);
        }
    }

    private async Task AddNoChangeCancellationAuditAsync(
        PriceCampaign campaign,
        string actor,
        string reason,
        string correlationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var variantId = campaign.CampaignItems
            .Select(item => item.VariantId)
            .FirstOrDefault();

        if (variantId <= 0)
        {
            return;
        }

        var currentPrice = await _context.ProductVariants
            .AsNoTracking()
            .Where(item => item.Id == variantId)
            .Select(item => (decimal?)item.CurrentPrice)
            .FirstOrDefaultAsync(cancellationToken);

        if (!currentPrice.HasValue || currentPrice.Value <= 0)
        {
            return;
        }

        _context.PriceHistories.Add(new PriceHistory
        {
            ProductVariantId = variantId,
            OldPrice = currentPrice.Value,
            NewPrice = currentPrice.Value,
            EventType = PriceHistoryEventType.Cancelled,
            SourceType = campaign.SourceType,
            SourceId = campaign.Id,
            CorrelationId = Truncate(correlationId, 64),
            Reason = reason,
            ChangedBy = actor,
            Note = Truncate($"{reason}. Kế hoạch bị hủy nhưng giá hiệu lực không đổi.", 255),
            CreatedAt = nowUtc
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<PriceCampaign?> LoadCampaignForWorkflowAsync(
        int campaignId,
        CancellationToken cancellationToken)
    {
        return await _context.PriceCampaigns
            .Include(item => item.CampaignItems)
            .FirstOrDefaultAsync(
                item => item.Id == campaignId,
                cancellationToken);
    }

    private async Task<ConflictResolutionResult> ApplyConflictPolicyAsync(
        PriceCampaign campaign,
        DateTime nowUtc,
        string actor,
        CancellationToken cancellationToken)
    {
        var variantIds = campaign.CampaignItems
            .Select(item => item.VariantId)
            .Distinct()
            .ToArray();
        var affectedVariantIds = variantIds.ToHashSet();
        var rangeStartUtc = campaign.ConflictPolicy == PriceConflictPolicy.SupersedeNow
            ? nowUtc
            : campaign.StartDate;
        var rangeEndUtc = campaign.EndDate ?? DateTime.MaxValue;

        var conflicts = await _context.PriceCampaigns
            .Include(item => item.CampaignItems)
            .Where(item =>
                item.Id != campaign.Id
                && BlockingStatuses.Contains(item.Status)
                && item.StartDate < rangeEndUtc
                && (!item.EndDate.HasValue
                    || item.EndDate.Value > rangeStartUtc)
                && item.CampaignItems.Any(campaignItem =>
                    variantIds.Contains(campaignItem.VariantId)))
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        if (conflicts.Count == 0)
        {
            return new ConflictResolutionResult(affectedVariantIds.ToArray());
        }

        if (campaign.ConflictPolicy == PriceConflictPolicy.Reject)
        {
            throw new PriceCampaignWorkflowException(
                $"Có {conflicts.Count} kế hoạch giá chồng lấn. Hãy chọn chính sách thay thế hoặc điều chỉnh thời gian.",
                "PRICE_WINDOW_CONFLICT");
        }

        var selectedVariantIds = variantIds.ToHashSet();
        var partialCampaign = conflicts.FirstOrDefault(conflict =>
            conflict.CampaignItems.Any(item =>
                !selectedVariantIds.Contains(item.VariantId)));

        if (campaign.ConflictPolicy == PriceConflictPolicy.ReplaceFromStart
            && partialCampaign is not null)
        {
            throw new PriceCampaignWorkflowException(
                $"Kế hoạch {partialCampaign.Code} còn chứa biến thể ngoài phạm vi đang chọn. Chính sách thay từ thời điểm bắt đầu yêu cầu chọn đủ toàn bộ biến thể của kế hoạch đó.",
                "PARTIAL_CAMPAIGN_REPLACEMENT");
        }

        foreach (var conflict in conflicts)
        {
            foreach (var item in conflict.CampaignItems)
            {
                affectedVariantIds.Add(item.VariantId);
            }

            conflict.UpdatedAt = nowUtc;

            if (campaign.ConflictPolicy == PriceConflictPolicy.SupersedeNow)
            {
                var unaffectedItems = conflict.CampaignItems
                    .Where(item => !selectedVariantIds.Contains(item.VariantId))
                    .ToArray();

                if (unaffectedItems.Length > 0)
                {
                    var continuation = CreateContinuationCampaign(
                        conflict,
                        unaffectedItems,
                        actor,
                        nowUtc);
                    _context.PriceCampaigns.Add(continuation);
                }

                conflict.SupersededByCampaignId = campaign.Id;
                conflict.Status = PriceCampaignStatus.Superseded;
                conflict.IsActive = false;
                continue;
            }

            conflict.SupersededByCampaignId = campaign.Id;

            if (campaign.StartDate <= nowUtc
                || conflict.StartDate >= campaign.StartDate)
            {
                conflict.Status = PriceCampaignStatus.Superseded;
                conflict.IsActive = false;
                continue;
            }

            conflict.Mode = PriceCampaignMode.FixedWindow;
            conflict.EndDate = campaign.StartDate;
            conflict.Status = PriceCampaignLifecycle.ResolveConfirmedStatus(
                conflict.StartDate,
                conflict.EndDate,
                nowUtc);
            conflict.IsActive = PriceCampaignLifecycle.IsCompatibilityActive(
                conflict.Status);
        }

        return new ConflictResolutionResult(affectedVariantIds.ToArray());
    }

    private static PriceCampaign CreateContinuationCampaign(
        PriceCampaign source,
        IReadOnlyCollection<PriceCampaignItem> unaffectedItems,
        string actor,
        DateTime nowUtc)
    {
        var status = PriceCampaignLifecycle.ResolveConfirmedStatus(
            source.StartDate,
            source.EndDate,
            nowUtc);

        var continuation = new PriceCampaign
        {
            Code = CreateCampaignCode("CT", nowUtc),
            Name = Truncate($"{source.Name} - tiếp tục", 255),
            Description = $"Tách tự động các biến thể không bị thay thế từ {source.Code} (#{source.Id}).",
            Mode = source.Mode,
            Status = status,
            StartDate = source.StartDate,
            EndDate = source.EndDate,
            Reason = source.Reason,
            SourceType = source.SourceType,
            ConflictPolicy = PriceConflictPolicy.Reject,
            ClientRequestId = null,
            ConfirmedAt = source.ConfirmedAt ?? nowUtc,
            ConfirmedBy = source.ConfirmedBy ?? actor,
            CreatedBy = actor,
            CreatedAt = nowUtc,
            IsActive = PriceCampaignLifecycle.IsCompatibilityActive(status),
            CampaignItems = unaffectedItems
                .Select(item => new PriceCampaignItem
                {
                    VariantId = item.VariantId,
                    ListPriceSnapshot = item.ListPriceSnapshot,
                    EffectivePriceSnapshot = item.EffectivePriceSnapshot,
                    PreviousEffectivePriceSnapshot = item.PreviousEffectivePriceSnapshot,
                    AdjustmentType = item.AdjustmentType,
                    AdjustmentValue = item.AdjustmentValue,
                    NewPrice = item.NewPrice,
                    Currency = item.Currency
                })
                .ToList()
        };

        return continuation;
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
            return new IdempotencyReview(false, "CAMPAIGN_METADATA");
        }

        if (campaign.CampaignItems.Count != command.Items.Count)
        {
            return new IdempotencyReview(false, "ITEM_COUNT");
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

            if (campaignItem.AdjustmentType != requestedItem.AdjustmentType
                || campaignItem.AdjustmentValue != requestedItem.AdjustmentValue)
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

        if (!campaign.RowVersion.SequenceEqual(expectedRowVersion))
        {
            throw new PriceCampaignWorkflowException(
                "Dữ liệu vừa được thay đổi ở nơi khác. Vui lòng tải lại trang.",
                "CONCURRENCY_CONFLICT");
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


    private static DatabaseWriteFailure MapConfirmationDatabaseFailure(
        DbUpdateException exception)
    {
        var sqlException = FindSqlException(exception);

        if (sqlException is null)
        {
            return new DatabaseWriteFailure(
                "Không thể ghi trạng thái xác nhận vào cơ sở dữ liệu.",
                "DATABASE_WRITE_FAILED");
        }

        var sqlMessage = sqlException.Message;

        if (sqlException.Number == 547)
        {
            if (sqlMessage.Contains(
                    "CK_PriceCampaign_Status",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseWriteFailure(
                    "Ràng buộc trạng thái kế hoạch trong cơ sở dữ liệu chưa đồng bộ với Pricing V2. "
                    + "Kế hoạch bắt đầu trong tương lai cần trạng thái Scheduled. "
                    + "Hãy chạy migration RepairPricingLifecycleStatusConstraint rồi xác nhận lại.",
                    "PRICING_STATUS_CONSTRAINT_OUTDATED");
            }

            if (sqlMessage.Contains(
                    "CK_PriceCampaign_Duration",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseWriteFailure(
                    "Khoảng thời gian kế hoạch không thỏa ràng buộc của cơ sở dữ liệu. "
                    + "Kế hoạch có ngày kết thúc phải kết thúc sau khi bắt đầu; "
                    + "kế hoạch không thời hạn phải để trống ngày kết thúc.",
                    "PRICING_DURATION_CONSTRAINT");
            }

            if (sqlMessage.Contains(
                    "CK_PriceCampaign_SourceType",
                    StringComparison.OrdinalIgnoreCase)
                || sqlMessage.Contains(
                    "CK_PriceCampaign_ConflictPolicy",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseWriteFailure(
                    "Nguồn thay đổi hoặc chính sách xử lý xung đột chưa được database hiện tại hỗ trợ.",
                    "PRICING_ENUM_CONSTRAINT_OUTDATED");
            }

            if (sqlMessage.Contains(
                    "CK_PriceCampaignItem",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseWriteFailure(
                    "Một biến thể có snapshot giá hoặc cấu hình điều chỉnh không hợp lệ.",
                    "PRICING_ITEM_CONSTRAINT");
            }

            if (sqlMessage.Contains(
                    "CK_PriceHistory",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseWriteFailure(
                    "Không thể ghi lịch sử giá vì ràng buộc PriceHistory trong database chưa đồng bộ.",
                    "PRICE_HISTORY_CONSTRAINT_OUTDATED");
            }

            return new DatabaseWriteFailure(
                "Dữ liệu xác nhận vi phạm một ràng buộc của cơ sở dữ liệu.",
                "DATABASE_CONSTRAINT_VIOLATION");
        }

        if (sqlException.Number is 2601 or 2627)
        {
            return new DatabaseWriteFailure(
                "Dữ liệu xác nhận bị trùng khóa hoặc trùng mã kế hoạch.",
                "UNIQUE_CONSTRAINT");
        }

        if (sqlException.Number == 1205)
        {
            return new DatabaseWriteFailure(
                "Một thao tác giá khác đang khóa dữ liệu. Vui lòng thử xác nhận lại.",
                "DATABASE_DEADLOCK");
        }

        return new DatabaseWriteFailure(
            $"Không thể xác nhận kế hoạch giá do lỗi cơ sở dữ liệu SQL {sqlException.Number}.",
            "DATABASE_WRITE_FAILED");
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException;
            }
        }

        return null;
    }

    private static decimal ResolveRecoveryPrice(PriceCampaignItem item)
    {
        if (item.PreviousEffectivePriceSnapshot > 0)
        {
            return item.PreviousEffectivePriceSnapshot;
        }

        if (item.EffectivePriceSnapshot > 0)
        {
            return item.EffectivePriceSnapshot;
        }

        return item.ListPriceSnapshot;
    }

    private static string BuildConfirmationReason(PriceCampaign campaign)
    {
        return campaign.ConflictPolicy switch
        {
            PriceConflictPolicy.ReplaceFromStart =>
                "Xác nhận kế hoạch và thay thế giá từ thời điểm bắt đầu",
            PriceConflictPolicy.SupersedeNow =>
                "Xác nhận kế hoạch và thay thế giá đang chạy ngay",
            _ => "Xác nhận kế hoạch giá từ danh sách chờ"
        };
    }

    private static string CreateRecoveryMarker(PriceCampaign sourceCampaign)
    {
        return $"Phục hồi giá trước kế hoạch {sourceCampaign.Code} (#{sourceCampaign.Id}).";
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

    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new PriceCampaignWorkflowException(
                "Vui lòng nhập lý do thao tác giá.",
                "REASON_REQUIRED");
        }

        return Truncate(reason.Trim(), 500);
    }

    private static string? CleanNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private static string CreateCampaignCode(
        string prefix,
        DateTime nowUtc)
    {
        var suffix = Guid.NewGuid()
            .ToString("N")[..10]
            .ToUpperInvariant();
        return $"{prefix}-{nowUtc:yyyyMMdd}-{suffix}";
    }

    private sealed record DatabaseWriteFailure(
        string Message,
        string ErrorCode);

    private sealed record ConflictResolutionResult(
        IReadOnlyCollection<int> AffectedVariantIds);

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
