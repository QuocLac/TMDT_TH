using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

/// <summary>
/// Điều phối workflow giá.
///
/// Các thao tác ổn định tiếp tục dùng PriceCampaignWorkflowService gốc.
/// Riêng ConfirmDraft được thực hiện ở đây để:
/// - không ép ICollection sang IReadOnlyCollection tại runtime;
/// - cô lập rõ từng stage trong transaction;
/// - bắt cả SqlException phát sinh từ truy vấn, không chỉ DbUpdateException;
/// - giữ đầy đủ Reject, ReplaceFromStart và SupersedeNow;
/// - luôn rollback an toàn mà không che mất exception ban đầu.
/// </summary>
public sealed class ReliablePriceCampaignWorkflowService
    : IPriceCampaignWorkflowService
{
    private static readonly PriceCampaignStatus[] BlockingStatuses =
    [
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

    private readonly PriceCampaignWorkflowService _inner;
    private readonly ApplicationDbContext _context;
    private readonly IEffectivePriceService _effectivePriceService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReliablePriceCampaignWorkflowService> _logger;

    public ReliablePriceCampaignWorkflowService(
        PriceCampaignWorkflowService inner,
        ApplicationDbContext context,
        IEffectivePriceService effectivePriceService,
        TimeProvider timeProvider,
        ILogger<ReliablePriceCampaignWorkflowService> logger)
    {
        _inner = inner;
        _context = context;
        _effectivePriceService = effectivePriceService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<PriceCampaignWorkflowResult> SaveDraftAsync(
        PriceCampaignDraftCommand command,
        CancellationToken cancellationToken)
    {
        return _inner.SaveDraftAsync(command, cancellationToken);
    }

    public Task<PriceCampaignWorkflowResult> ActivateNowAsync(
        ActivatePriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        return _inner.ActivateNowAsync(command, cancellationToken);
    }

    public Task<PriceCampaignWorkflowResult> CancelAsync(
        CancelPriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        return _inner.CancelAsync(command, cancellationToken);
    }

    public Task<PriceCampaignWorkflowResult> RecoverAsync(
        RecoverPriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        return _inner.RecoverAsync(command, cancellationToken);
    }

    public async Task<PriceCampaignWorkflowResult> ConfirmDraftAsync(
        ConfirmPriceCampaignCommand command,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        var stage = ConfirmationStage.BeginTransaction;
        PricePlanPreviewResult? preview = null;

        try
        {
            transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            stage = ConfirmationStage.LoadCampaign;
            var campaign = await _context.PriceCampaigns
                .Include(item => item.CampaignItems)
                .FirstOrDefaultAsync(
                    item => item.Id == command.CampaignId,
                    cancellationToken);

            if (campaign is null)
            {
                await SafeRollbackAsync(transaction, command, stage);
                return Failure(
                    "Không tìm thấy bản nháp kế hoạch giá.",
                    "CAMPAIGN_NOT_FOUND",
                    command.CampaignId);
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
                await SafeRollbackAsync(transaction, command, stage);
                return Failure(
                    "Kế hoạch đã kết thúc, bị hủy hoặc bị thay thế nên không thể xác nhận.",
                    "CAMPAIGN_NOT_CONFIRMABLE",
                    campaign.Id);
            }

            stage = ConfirmationStage.ValidateRowVersion;
            ApplyExpectedRowVersion(
                campaign,
                command.ExpectedCampaignRowVersion);

            // Materialize thành array một lần. Không ép ICollection sang
            // IReadOnlyCollection nên không còn InvalidCastException.
            var draftItems = campaign.CampaignItems.ToArray();
            if (draftItems.Length == 0)
            {
                await SafeRollbackAsync(transaction, command, stage);
                return Failure(
                    "Bản nháp không có biến thể để xác nhận.",
                    "EMPTY_CAMPAIGN_ITEMS",
                    campaign.Id);
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var effectiveStartUtc =
                campaign.ConflictPolicy == PriceConflictPolicy.SupersedeNow
                    ? nowUtc
                    : campaign.StartDate;

            if (campaign.EndDate.HasValue
                && campaign.EndDate.Value <= nowUtc)
            {
                await SafeRollbackAsync(transaction, command, stage);
                return Failure(
                    "Kế hoạch đã hết thời gian hiệu lực. Hãy cập nhật thời gian trước khi xác nhận.",
                    "CAMPAIGN_EXPIRED",
                    campaign.Id);
            }

            stage = ConfirmationStage.Preview;
            preview = await _effectivePriceService.PreviewCampaignAsync(
                campaign.Id,
                campaign.Mode,
                effectiveStartUtc,
                campaign.EndDate,
                campaign.ConflictPolicy,
                draftItems
                    .Select(item => new PricePlanPreviewInput(
                        item.VariantId,
                        item.AdjustmentType,
                        item.AdjustmentValue))
                    .ToArray(),
                cancellationToken);

            if (!preview.IsValid || !preview.CanConfirm)
            {
                await SafeRollbackAsync(transaction, command, stage);
                return PriceCampaignWorkflowResult.Failure(
                    preview.ErrorMessage
                        ?? "Kế hoạch chưa đủ điều kiện để xác nhận.",
                    preview.ErrorCode
                        ?? "CAMPAIGN_NOT_CONFIRMABLE",
                    preview,
                    campaign.Id);
            }

            stage = ConfirmationStage.ReviewSnapshots;
            var changedItemCount = CountChangedSnapshots(
                draftItems,
                preview.Items);

            if (changedItemCount > 0)
            {
                await SafeRollbackAsync(transaction, command, stage);

                _logger.LogWarning(
                    "Pricing draft requires review. CampaignId={CampaignId}, CorrelationId={CorrelationId}, ChangedItemCount={ChangedItemCount}.",
                    campaign.Id,
                    command.CorrelationId,
                    changedItemCount);

                return PriceCampaignWorkflowResult.Failure(
                    $"Có {changedItemCount} biến thể đã thay đổi giá hoặc cấu hình kể từ lần lưu bản nháp. "
                    + "Hãy kiểm tra preview mới, lưu lại bản nháp rồi xác nhận lại.",
                    "DRAFT_REVIEW_REQUIRED",
                    preview,
                    campaign.Id);
            }

            stage = ConfirmationStage.ResolveConflicts;
            campaign.StartDate = effectiveStartUtc;

            var actor = NormalizeActor(command.Actor);
            var affectedVariantIds = await ApplyConflictPolicyAsync(
                campaign,
                draftItems,
                nowUtc,
                actor,
                cancellationToken);

            stage = ConfirmationStage.PersistConfirmation;
            campaign.Status = PriceCampaignLifecycle.ResolveConfirmedStatus(
                campaign.StartDate,
                campaign.EndDate,
                nowUtc);
            campaign.IsActive =
                PriceCampaignLifecycle.IsCompatibilityActive(campaign.Status);
            campaign.ConfirmedAt = nowUtc;
            campaign.ConfirmedBy = actor;
            campaign.UpdatedAt = nowUtc;

            await _context.SaveChangesAsync(cancellationToken);

            stage = ConfirmationStage.RecalculatePrices;
            await _effectivePriceService.RecalculateVariantsAsync(
                affectedVariantIds,
                actor,
                BuildConfirmationReason(campaign),
                command.CorrelationId,
                cancellationToken);

            stage = ConfirmationStage.Commit;
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Reliable pricing confirmation completed. CampaignId={CampaignId}, Status={Status}, ConflictPolicy={ConflictPolicy}, CorrelationId={CorrelationId}.",
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
        catch (ReliableWorkflowException exception)
        {
            await SafeRollbackAsync(transaction, command, stage);
            return Failure(
                exception.Message,
                exception.ErrorCode,
                command.CampaignId,
                preview);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await SafeRollbackAsync(transaction, command, stage);

            _logger.LogWarning(
                exception,
                "Pricing confirmation concurrency conflict. Stage={Stage}, CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                stage,
                command.CampaignId,
                command.CorrelationId);

            return Failure(
                "Bản nháp hoặc biến thể vừa được thay đổi ở nơi khác. Vui lòng tải lại trước khi xác nhận.",
                $"CONCURRENCY_CONFLICT_{stage.ToCode()}",
                command.CampaignId,
                preview);
        }
        catch (DbUpdateException exception)
        {
            await SafeRollbackAsync(transaction, command, stage);
            return MapDatabaseFailure(
                exception,
                command,
                stage,
                preview);
        }
        catch (SqlException exception)
        {
            // SqlException có thể phát sinh từ ToListAsync/FirstOrDefaultAsync,
            // không được bọc trong DbUpdateException.
            await SafeRollbackAsync(transaction, command, stage);
            return MapSqlFailure(
                exception,
                command,
                stage,
                preview);
        }
        catch (InvalidCastException exception)
        {
            await SafeRollbackAsync(transaction, command, stage);

            _logger.LogError(
                exception,
                "Pricing confirmation collection conversion failure. Stage={Stage}, CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                stage,
                command.CampaignId,
                command.CorrelationId);

            return Failure(
                "Kiểu dữ liệu collection của kế hoạch không tương thích. "
                + "Pipeline mới đã bỏ ép kiểu; hãy rebuild sạch ứng dụng rồi thử lại.",
                $"COLLECTION_TYPE_MISMATCH_{stage.ToCode()}",
                command.CampaignId,
                preview);
        }
        catch (InvalidOperationException exception)
        {
            await SafeRollbackAsync(transaction, command, stage);

            var code = ClassifyInvalidOperation(exception, stage);
            _logger.LogError(
                exception,
                "Pricing confirmation invalid operation. Stage={Stage}, CampaignId={CampaignId}, CorrelationId={CorrelationId}, ErrorCode={ErrorCode}.",
                stage,
                command.CampaignId,
                command.CorrelationId,
                code);

            return Failure(
                BuildInvalidOperationMessage(code),
                code,
                command.CampaignId,
                preview);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await SafeRollbackAsync(
                transaction,
                command,
                stage);
            throw;
        }
        catch (Exception exception)
        {
            await SafeRollbackAsync(transaction, command, stage);

            var errorCode = $"CONFIRMATION_{stage.ToCode()}_FAILED";
            _logger.LogError(
                exception,
                "Unexpected pricing confirmation failure. Stage={Stage}, ExceptionType={ExceptionType}, CampaignId={CampaignId}, CorrelationId={CorrelationId}, ErrorCode={ErrorCode}.",
                stage,
                exception.GetType().FullName,
                command.CampaignId,
                command.CorrelationId,
                errorCode);

            return Failure(
                $"Không thể xác nhận kế hoạch tại bước “{stage.ToDisplayName()}”. "
                + "Không có thay đổi nào được ghi vì transaction đã rollback.",
                errorCode,
                command.CampaignId,
                preview);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<IReadOnlyCollection<int>> ApplyConflictPolicyAsync(
        PriceCampaign campaign,
        IReadOnlyCollection<PriceCampaignItem> draftItems,
        DateTime nowUtc,
        string actor,
        CancellationToken cancellationToken)
    {
        var variantIds = draftItems
            .Select(item => item.VariantId)
            .Distinct()
            .ToArray();

        var affectedVariantIds = variantIds.ToHashSet();
        var rangeStartUtc =
            campaign.ConflictPolicy == PriceConflictPolicy.SupersedeNow
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
            return affectedVariantIds.ToArray();
        }

        if (campaign.ConflictPolicy == PriceConflictPolicy.Reject)
        {
            throw new ReliableWorkflowException(
                $"Có {conflicts.Count} kế hoạch giá chồng lấn. "
                + "Hãy chọn chính sách thay thế hoặc điều chỉnh thời gian.",
                "PRICE_WINDOW_CONFLICT");
        }

        var selectedVariantIds = variantIds.ToHashSet();
        var partialCampaign = conflicts.FirstOrDefault(conflict =>
            conflict.CampaignItems.Any(item =>
                !selectedVariantIds.Contains(item.VariantId)));

        if (campaign.ConflictPolicy == PriceConflictPolicy.ReplaceFromStart
            && partialCampaign is not null)
        {
            throw new ReliableWorkflowException(
                $"Kế hoạch {partialCampaign.Code} còn chứa biến thể ngoài phạm vi đang chọn. "
                + "Chính sách thay từ thời điểm bắt đầu yêu cầu chọn đủ toàn bộ biến thể.",
                "PARTIAL_CAMPAIGN_REPLACEMENT");
        }

        foreach (var conflict in conflicts)
        {
            var conflictItems = conflict.CampaignItems.ToArray();
            foreach (var item in conflictItems)
            {
                affectedVariantIds.Add(item.VariantId);
            }

            conflict.UpdatedAt = nowUtc;

            if (campaign.ConflictPolicy == PriceConflictPolicy.SupersedeNow)
            {
                var unaffectedItems = conflictItems
                    .Where(item =>
                        !selectedVariantIds.Contains(item.VariantId))
                    .ToArray();

                if (unaffectedItems.Length > 0)
                {
                    _context.PriceCampaigns.Add(
                        CreateContinuationCampaign(
                            conflict,
                            unaffectedItems,
                            actor,
                            nowUtc));
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
            conflict.Status =
                PriceCampaignLifecycle.ResolveConfirmedStatus(
                    conflict.StartDate,
                    conflict.EndDate,
                    nowUtc);
            conflict.IsActive =
                PriceCampaignLifecycle.IsCompatibilityActive(
                    conflict.Status);
        }

        return affectedVariantIds.ToArray();
    }

    private static int CountChangedSnapshots(
        IReadOnlyCollection<PriceCampaignItem> draftItems,
        IReadOnlyList<PricePlanPreviewItemResult> previewItems)
    {
        if (draftItems.Count != previewItems.Count)
        {
            return Math.Max(draftItems.Count, previewItems.Count);
        }

        var previewByVariantId = previewItems.ToDictionary(
            item => item.VariantId);
        var changedCount = 0;

        foreach (var draftItem in draftItems)
        {
            if (!previewByVariantId.TryGetValue(
                    draftItem.VariantId,
                    out var previewItem))
            {
                changedCount++;
                continue;
            }

            if (draftItem.ListPriceSnapshot != previewItem.ListPrice
                || draftItem.EffectivePriceSnapshot
                    != previewItem.CurrentPrice
                || draftItem.PreviousEffectivePriceSnapshot
                    != previewItem.CurrentPrice
                || draftItem.NewPrice != previewItem.NewPrice
                || draftItem.AdjustmentType
                    != previewItem.AdjustmentType
                || draftItem.AdjustmentValue
                    != previewItem.AdjustmentValue)
            {
                changedCount++;
            }
        }

        return changedCount;
    }

    private void ApplyExpectedRowVersion(
        PriceCampaign campaign,
        byte[]? expectedRowVersion)
    {
        if (expectedRowVersion is not { Length: 8 })
        {
            throw new ReliableWorkflowException(
                "Thiếu hoặc sai phiên bản dữ liệu. Vui lòng tải lại trang.",
                "INVALID_ROW_VERSION");
        }

        if (!campaign.RowVersion.SequenceEqual(expectedRowVersion))
        {
            throw new ReliableWorkflowException(
                "Dữ liệu vừa được thay đổi ở nơi khác. Vui lòng tải lại trang.",
                "CONCURRENCY_CONFLICT");
        }

        _context.Entry(campaign)
            .Property(item => item.RowVersion)
            .OriginalValue = expectedRowVersion;
    }

    private PriceCampaignWorkflowResult MapDatabaseFailure(
        DbUpdateException exception,
        ConfirmPriceCampaignCommand command,
        ConfirmationStage stage,
        PricePlanPreviewResult? preview)
    {
        var sqlException = FindSqlException(exception);
        if (sqlException is not null)
        {
            return MapSqlFailure(
                sqlException,
                command,
                stage,
                preview,
                exception);
        }

        var code = $"DATABASE_WRITE_FAILED_{stage.ToCode()}";
        _logger.LogError(
            exception,
            "Pricing confirmation database update failure without SqlException. Stage={Stage}, CampaignId={CampaignId}, CorrelationId={CorrelationId}, ErrorCode={ErrorCode}.",
            stage,
            command.CampaignId,
            command.CorrelationId,
            code);

        return Failure(
            $"Không thể ghi dữ liệu ở bước “{stage.ToDisplayName()}”. "
            + "Transaction đã rollback.",
            code,
            command.CampaignId,
            preview);
    }

    private PriceCampaignWorkflowResult MapSqlFailure(
        SqlException sqlException,
        ConfirmPriceCampaignCommand command,
        ConfirmationStage stage,
        PricePlanPreviewResult? preview,
        Exception? outerException = null)
    {
        var code = $"SQL_{sqlException.Number}_{stage.ToCode()}";
        var message = sqlException.Number switch
        {
            547 => ClassifyConstraintMessage(
                sqlException.Message,
                stage,
                out code),
            2601 or 2627 =>
                "Dữ liệu xác nhận bị trùng khóa hoặc trùng mã kế hoạch.",
            1205 =>
                "Một thao tác khác đang khóa dữ liệu giá. "
                + "Transaction hiện tại đã rollback; hãy thử xác nhận lại.",
            -2 =>
                "Truy vấn xác nhận vượt quá thời gian chờ. "
                + "Không có thay đổi nào được ghi.",
            208 =>
                "Database đang thiếu một bảng cần cho Pricing. "
                + "Hãy kiểm tra lại Update-Database.",
            207 =>
                "Database đang thiếu một cột cần cho Pricing. "
                + "Hãy kiểm tra migration hiện tại.",
            515 =>
                "Một trường bắt buộc đang nhận NULL khi xác nhận.",
            8115 =>
                "Một giá trị số vượt quá giới hạn lưu trữ của database.",
            _ =>
                $"SQL Server từ chối thao tác tại bước “{stage.ToDisplayName()}” "
                + $"(SQL {sqlException.Number})."
        };

        if (sqlException.Number is 2601 or 2627)
        {
            code = $"UNIQUE_CONSTRAINT_{stage.ToCode()}";
        }
        else if (sqlException.Number == 1205)
        {
            code = $"DATABASE_DEADLOCK_{stage.ToCode()}";
        }
        else if (sqlException.Number == -2)
        {
            code = $"DATABASE_TIMEOUT_{stage.ToCode()}";
        }

        _logger.LogError(
            outerException ?? sqlException,
            "Pricing confirmation SQL failure. Stage={Stage}, SqlNumber={SqlNumber}, CampaignId={CampaignId}, CorrelationId={CorrelationId}, ErrorCode={ErrorCode}.",
            stage,
            sqlException.Number,
            command.CampaignId,
            command.CorrelationId,
            code);

        return Failure(
            message,
            code,
            command.CampaignId,
            preview);
    }

    private static string ClassifyConstraintMessage(
        string sqlMessage,
        ConfirmationStage stage,
        out string errorCode)
    {
        if (sqlMessage.Contains(
                "CK_PriceCampaign_Status",
                StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "PRICING_STATUS_CONSTRAINT_OUTDATED";
            return "Ràng buộc trạng thái kế hoạch trong database chưa đồng bộ. "
                + "Trạng thái Scheduled/Active phải được hỗ trợ.";
        }

        if (sqlMessage.Contains(
                "CK_PriceCampaign_Duration",
                StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "PRICING_DURATION_CONSTRAINT";
            return "Khoảng thời gian kế hoạch không thỏa ràng buộc database.";
        }

        if (sqlMessage.Contains(
                "CK_PriceCampaignItem",
                StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "PRICING_ITEM_CONSTRAINT";
            return "Snapshot giá hoặc cấu hình điều chỉnh của biến thể không hợp lệ.";
        }

        if (sqlMessage.Contains(
                "CK_PriceHistory",
                StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "PRICE_HISTORY_CONSTRAINT_OUTDATED";
            return "Không thể ghi lịch sử giá vì constraint PriceHistory chưa đồng bộ.";
        }

        if (sqlMessage.Contains(
                "CK_ProductVariant",
                StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "PRODUCT_VARIANT_CONSTRAINT";
            return "Giá hiện tại hoặc tồn kho của biến thể vi phạm constraint.";
        }

        errorCode = $"DATABASE_CONSTRAINT_{stage.ToCode()}";
        return "Dữ liệu xác nhận vi phạm một ràng buộc của database.";
    }

    private static string ClassifyInvalidOperation(
        InvalidOperationException exception,
        ConfirmationStage stage)
    {
        var message = exception.Message;

        if (message.Contains(
                "cannot be tracked",
                StringComparison.OrdinalIgnoreCase)
            || message.Contains(
                "already being tracked",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"EF_TRACKING_CONFLICT_{stage.ToCode()}";
        }

        if (message.Contains(
                "transaction",
                StringComparison.OrdinalIgnoreCase)
            || message.Contains(
                "execution strategy",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"TRANSACTION_STATE_ERROR_{stage.ToCode()}";
        }

        if (message.Contains(
                "Sequence contains",
                StringComparison.OrdinalIgnoreCase)
            || message.Contains(
                "same key",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"DUPLICATE_RUNTIME_DATA_{stage.ToCode()}";
        }

        return $"INVALID_OPERATION_{stage.ToCode()}";
    }

    private static string BuildInvalidOperationMessage(string code)
    {
        if (code.StartsWith(
                "EF_TRACKING_CONFLICT",
                StringComparison.Ordinal))
        {
            return "EF Core phát hiện hai instance cùng khóa trong một request. "
                + "Transaction đã rollback; hãy tải lại trang.";
        }

        if (code.StartsWith(
                "TRANSACTION_STATE_ERROR",
                StringComparison.Ordinal))
        {
            return "Trạng thái transaction hoặc execution strategy không hợp lệ. "
                + "Không có dữ liệu nào được ghi.";
        }

        if (code.StartsWith(
                "DUPLICATE_RUNTIME_DATA",
                StringComparison.Ordinal))
        {
            return "Dữ liệu runtime có khóa trùng trong pipeline xác nhận.";
        }

        return "Pipeline xác nhận gặp trạng thái xử lý không hợp lệ. "
            + "Transaction đã rollback.";
    }

    private async Task SafeRollbackAsync(
        IDbContextTransaction? transaction,
        ConfirmPriceCampaignCommand command,
        ConfirmationStage stage)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            _logger.LogCritical(
                rollbackException,
                "Pricing confirmation rollback failed. Stage={Stage}, CampaignId={CampaignId}, CorrelationId={CorrelationId}.",
                stage,
                command.CampaignId,
                command.CorrelationId);
        }
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

        return new PriceCampaign
        {
            Code = CreateCampaignCode("CT", nowUtc),
            Name = Truncate($"{source.Name} - tiếp tục", 255),
            Description =
                $"Tách tự động các biến thể không bị thay thế từ "
                + $"{source.Code} (#{source.Id}).",
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
            IsActive =
                PriceCampaignLifecycle.IsCompatibilityActive(status),
            CampaignItems = unaffectedItems
                .Select(item => new PriceCampaignItem
                {
                    VariantId = item.VariantId,
                    ListPriceSnapshot = item.ListPriceSnapshot,
                    EffectivePriceSnapshot =
                        item.EffectivePriceSnapshot,
                    PreviousEffectivePriceSnapshot =
                        item.PreviousEffectivePriceSnapshot,
                    AdjustmentType = item.AdjustmentType,
                    AdjustmentValue = item.AdjustmentValue,
                    NewPrice = item.NewPrice,
                    Currency = item.Currency
                })
                .ToList()
        };
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

    private static PriceCampaignWorkflowResult Failure(
        string message,
        string errorCode,
        int campaignId,
        PricePlanPreviewResult? preview = null)
    {
        return PriceCampaignWorkflowResult.Failure(
            message,
            errorCode,
            preview,
            campaignId);
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

    private static string NormalizeActor(string? actor)
    {
        var normalized = string.IsNullOrWhiteSpace(actor)
            ? "Admin UI"
            : actor.Trim();

        return normalized.Length <= 100
            ? normalized
            : normalized[..100];
    }

    private static string BuildConfirmationReason(
        PriceCampaign campaign)
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

    private static string CreateCampaignCode(
        string prefix,
        DateTime nowUtc)
    {
        var suffix = Guid.NewGuid()
            .ToString("N")[..10]
            .ToUpperInvariant();

        return $"{prefix}-{nowUtc:yyyyMMdd}-{suffix}";
    }

    private static string Truncate(
        string value,
        int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    internal enum ConfirmationStage
    {
        BeginTransaction,
        LoadCampaign,
        ValidateRowVersion,
        Preview,
        ReviewSnapshots,
        ResolveConflicts,
        PersistConfirmation,
        RecalculatePrices,
        Commit
    }

    private sealed class ReliableWorkflowException : Exception
    {
        public ReliableWorkflowException(
            string message,
            string errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}

internal static class ConfirmationStageExtensions
{
    public static string ToCode(
        this ReliablePriceCampaignWorkflowService.ConfirmationStage stage)
    {
        return stage switch
        {
            ReliablePriceCampaignWorkflowService.ConfirmationStage.BeginTransaction =>
                "BEGIN_TRANSACTION",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.LoadCampaign =>
                "LOAD_CAMPAIGN",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.ValidateRowVersion =>
                "VALIDATE_ROW_VERSION",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.Preview =>
                "PREVIEW",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.ReviewSnapshots =>
                "REVIEW_SNAPSHOTS",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.ResolveConflicts =>
                "RESOLVE_CONFLICTS",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.PersistConfirmation =>
                "PERSIST_CONFIRMATION",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.RecalculatePrices =>
                "RECALCULATE_PRICES",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.Commit =>
                "COMMIT",
            _ => "UNKNOWN"
        };
    }

    public static string ToDisplayName(
        this ReliablePriceCampaignWorkflowService.ConfirmationStage stage)
    {
        return stage switch
        {
            ReliablePriceCampaignWorkflowService.ConfirmationStage.BeginTransaction =>
                "mở transaction",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.LoadCampaign =>
                "tải bản nháp",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.ValidateRowVersion =>
                "kiểm tra phiên bản dữ liệu",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.Preview =>
                "preview giá lần cuối",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.ReviewSnapshots =>
                "đối chiếu snapshot",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.ResolveConflicts =>
                "xử lý kế hoạch chồng lấn",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.PersistConfirmation =>
                "lưu trạng thái xác nhận",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.RecalculatePrices =>
                "tính lại giá hiệu lực",
            ReliablePriceCampaignWorkflowService.ConfirmationStage.Commit =>
                "commit transaction",
            _ => "không xác định"
        };
    }
}
