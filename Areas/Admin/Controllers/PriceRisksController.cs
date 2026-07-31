using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.PriceRisks;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class PriceRisksController : Controller
{
    private static readonly PriceCampaignStatus[] MonitoredStatuses =
    [
        PriceCampaignStatus.Draft,
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

    private static readonly PriceCampaignStatus[] EffectiveStatuses =
    [
        PriceCampaignStatus.Active
    ];

    private static readonly TimeZoneInfo BusinessTimeZone =
        ResolveBusinessTimeZone();

    private readonly ApplicationDbContext _context;
    private readonly IEffectivePriceService _effectivePriceService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PriceRisksController> _logger;

    public PriceRisksController(
        ApplicationDbContext context,
        IEffectivePriceService effectivePriceService,
        TimeProvider timeProvider,
        ILogger<PriceRisksController> logger)
    {
        _context = context;
        _effectivePriceService = effectivePriceService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        PriceRiskLevel? riskLevel,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var thirtyDaysAgoUtc = nowUtc.AddDays(-30);
        var sevenDaysAgoUtc = nowUtc.AddDays(-7);
        var currentActor = GetActor();
        var normalizedSearch = search?.Trim() ?? string.Empty;

        var campaigns = await _context.PriceCampaigns
            .AsNoTracking()
            .AsSplitQuery()
            .Where(campaign =>
                MonitoredStatuses.Contains(campaign.Status))
            .Include(campaign => campaign.CampaignItems)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .OrderByDescending(campaign => campaign.CreatedAt)
            .ToListAsync(cancellationToken);

        var monitoredVariantIds = campaigns
            .SelectMany(campaign => campaign.CampaignItems)
            .Select(item => item.VariantId)
            .Distinct()
            .ToArray();

        List<RecentPriceHistoryRow> recentHistory =
            monitoredVariantIds.Length == 0
                ? []
                : await _context.PriceHistories
                .AsNoTracking()
                .Where(history =>
                    monitoredVariantIds.Contains(
                        history.ProductVariantId)
                    && history.PriceKind
                        == PriceHistoryKind.EffectivePrice
                    && (history.EffectiveFrom ?? history.CreatedAt)
                        >= thirtyDaysAgoUtc)
                .Select(history => new RecentPriceHistoryRow(
                    history.ProductVariantId,
                    history.OldPrice,
                    history.NewPrice,
                    history.EffectiveFrom
                        ?? history.CreatedAt))
                .ToListAsync(cancellationToken);

        var historyByVariant = recentHistory
            .GroupBy(history => history.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        var campaignRows = campaigns
            .Select(campaign =>
            {
                var overlapCount = CountOverlaps(
                    campaign,
                    campaigns);

                var inputs = campaign.CampaignItems
                    .OrderBy(item => item.Variant.SKU)
                    .Select(item =>
                    {
                        historyByVariant.TryGetValue(
                            item.VariantId,
                            out var rows);
                        rows ??= [];

                        var observedPrices = rows
                            .SelectMany(row => new[]
                            {
                                row.OldPrice,
                                row.NewPrice
                            })
                            .Append(item.Variant.CurrentPrice)
                            .Where(price => price > 0)
                            .ToArray();

                        var lowestObserved =
                            observedPrices.Length == 0
                                ? item.Variant.CurrentPrice
                                : observedPrices.Min();

                        var recentChangeCount = rows.Count(row =>
                            row.EffectiveAtUtc >= sevenDaysAgoUtc);

                        var snapshotStale =
                            campaign.Status
                                == PriceCampaignStatus.Draft
                            && (item.ListPriceSnapshot
                                    != item.Variant.Price
                                || item.EffectivePriceSnapshot
                                    != item.Variant.CurrentPrice
                                || item.PreviousEffectivePriceSnapshot
                                    != item.Variant.CurrentPrice);

                        return new PriceRiskItemInput(
                            item.VariantId,
                            item.Variant.SKU,
                            item.Variant.Price,
                            item.Variant.CurrentPrice,
                            item.NewPrice,
                            lowestObserved,
                            recentChangeCount,
                            snapshotStale);
                    })
                    .ToArray();

                var assessment = PriceRiskPolicy.Assess(
                    new PriceRiskCampaignInput(
                        campaign.Id,
                        campaign.Status,
                        campaign.Mode,
                        campaign.SourceType,
                        campaign.ConflictPolicy,
                        campaign.CreatedBy,
                        campaign.ConfirmedBy,
                        currentActor,
                        overlapCount,
                        inputs));

                return MapCampaign(
                    campaign,
                    overlapCount,
                    assessment);
            })
            .Where(row =>
                !riskLevel.HasValue
                || row.RiskLevel == riskLevel.Value)
            .Where(row =>
                string.IsNullOrWhiteSpace(normalizedSearch)
                || row.Code.Contains(
                    normalizedSearch,
                    StringComparison.OrdinalIgnoreCase)
                || row.Name.Contains(
                    normalizedSearch,
                    StringComparison.OrdinalIgnoreCase)
                || row.CreatedBy.Contains(
                    normalizedSearch,
                    StringComparison.OrdinalIgnoreCase)
                || row.Issues.Any(issue =>
                    issue.Title.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase)
                    || issue.Detail.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(issue.Sku)
                        && issue.Sku.Contains(
                            normalizedSearch,
                            StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(row => row.RiskLevel)
            .ThenBy(row => row.Status)
            .ThenByDescending(row => row.StartLocal)
            .ToArray();

        var driftResult = await BuildProjectionDriftsAsync(
            nowUtc,
            cancellationToken);

        return View(new PriceRiskIndexPageViewModel
        {
            GeneratedAtUtc = nowUtc,
            CurrentActor = currentActor,
            Filter = new PriceRiskFilterViewModel
            {
                Search = normalizedSearch,
                RiskLevel = riskLevel
            },
            Summary = new PriceRiskSummaryViewModel
            {
                CampaignCount = campaignRows.Length,
                CriticalCampaignCount = campaignRows.Count(row =>
                    row.RiskLevel == PriceRiskLevel.Critical),
                HighCampaignCount = campaignRows.Count(row =>
                    row.RiskLevel == PriceRiskLevel.High),
                RequiresSecondApproverCount =
                    campaignRows.Count(row =>
                        row.RequiresSecondApprover),
                ProjectionDriftCount =
                    driftResult.TotalCount,
                AmbiguousEffectivePriceCount =
                    driftResult.Rows.Count(row =>
                        row.IsAmbiguous)
            },
            Policy = new PriceRiskPolicyViewModel
            {
                CriticalDiscountPercent =
                    PriceRiskPolicy.CriticalDiscountPercent,
                SecondApproverDiscountPercent =
                    PriceRiskPolicy.SecondApproverDiscountPercent,
                ThirtyDayLowUndercutPercent =
                    PriceRiskPolicy.ThirtyDayLowUndercutPercent,
                FrequentEffectiveChanges7Days =
                    PriceRiskPolicy.FrequentEffectiveChanges7Days,
                LargeScopeVariantCount =
                    PriceRiskPolicy.LargeScopeVariantCount
            },
            Campaigns = campaignRows,
            ProjectionDrifts = driftResult.Rows,
            TotalProjectionDrifts = driftResult.TotalCount
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reconcile(
        [FromBody] PriceProjectionReconcileRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        Response.Headers["X-Correlation-ID"] = correlationId;

        var requestedIds = request?.VariantIds
            ?.Where(id => id > 0)
            .Distinct()
            .Take(101)
            .ToArray()
            ?? [];

        if (requestedIds.Length == 0)
        {
            return Json(new
            {
                success = false,
                message = "Vui lòng chọn ít nhất một SKU cần đối soát.",
                errorCode = "EMPTY_VARIANT_SCOPE",
                correlationId
            });
        }

        if (requestedIds.Length > 100)
        {
            return Json(new
            {
                success = false,
                message = "Mỗi lần chỉ được đối soát tối đa 100 SKU.",
                errorCode = "RECONCILIATION_SCOPE_TOO_LARGE",
                correlationId
            });
        }

        var validIds = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                requestedIds.Contains(variant.Id))
            .Select(variant => variant.Id)
            .ToArrayAsync(cancellationToken);

        if (validIds.Length != requestedIds.Length)
        {
            return Json(new
            {
                success = false,
                message = "Một hoặc nhiều SKU không còn tồn tại.",
                errorCode = "VARIANT_NOT_FOUND",
                correlationId
            });
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var ambiguousIds = await _context.PriceCampaignItems
            .AsNoTracking()
            .Where(item =>
                validIds.Contains(item.VariantId)
                && EffectiveStatuses.Contains(
                    item.Campaign.Status)
                && item.Campaign.StartDate <= nowUtc
                && (!item.Campaign.EndDate.HasValue
                    || item.Campaign.EndDate.Value > nowUtc))
            .GroupBy(item => item.VariantId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArrayAsync(cancellationToken);

        if (ambiguousIds.Length > 0)
        {
            return Json(new
            {
                success = false,
                message =
                    $"Có {ambiguousIds.Length} SKU đang có nhiều kế hoạch "
                    + "cùng hiệu lực. Hãy xử lý chồng lấn trước khi đối soát.",
                errorCode = "MULTIPLE_EFFECTIVE_CAMPAIGNS",
                correlationId,
                variantIds = ambiguousIds
            });
        }

        var actor = GetActor();
        var reason = string.IsNullOrWhiteSpace(request?.Reason)
            ? "Đối soát projection giá từ trung tâm rủi ro"
            : request.Reason.Trim();

        try
        {
            var result = await _effectivePriceService
                .RecalculateVariantsAsync(
                    validIds,
                    actor,
                    reason,
                    correlationId,
                    cancellationToken);

            _logger.LogInformation(
                "Pricing projection reconciled. "
                + "EvaluatedCount={EvaluatedCount}, "
                + "ChangedCount={ChangedCount}, "
                + "Actor={Actor}, "
                + "CorrelationId={CorrelationId}.",
                result.EvaluatedCount,
                result.ChangedCount,
                actor,
                correlationId);

            return Json(new
            {
                success = true,
                message =
                    $"Đã kiểm tra {result.EvaluatedCount} SKU; "
                    + $"{result.ChangedCount} SKU được điều chỉnh.",
                evaluatedCount = result.EvaluatedCount,
                changedCount = result.ChangedCount,
                correlationId
            });
        }
        catch (EffectivePriceRecalculationException exception)
        {
            _logger.LogWarning(
                exception,
                "Pricing projection reconciliation rejected. "
                + "VariantId={VariantId}, ErrorCode={ErrorCode}, "
                + "CorrelationId={CorrelationId}.",
                exception.VariantId,
                exception.ErrorCode,
                correlationId);

            return Json(new
            {
                success = false,
                message = exception.Message,
                errorCode = exception.ErrorCode,
                correlationId
            });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Pricing projection reconciliation failed. "
                + "CorrelationId={CorrelationId}.",
                correlationId);

            return Json(new
            {
                success = false,
                message = "Không thể đối soát projection giá lúc này.",
                errorCode = "PRICE_RECONCILIATION_FAILED",
                correlationId
            });
        }
    }

    private async Task<ProjectionDriftResult>
        BuildProjectionDriftsAsync(
            DateTime nowUtc,
            CancellationToken cancellationToken)
    {
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.IsActive
                && variant.Product.IsActive)
            .OrderBy(variant => variant.Product.Name)
            .ThenBy(variant => variant.SKU)
            .Select(variant => new ActiveVariantRow(
                variant.Id,
                variant.ProductId,
                variant.Product.Name,
                variant.SKU,
                variant.Price,
                variant.CurrentPrice,
                variant.CurrentPriceSourceType,
                variant.CurrentPriceSourceId))
            .ToListAsync(cancellationToken);

        var variantIds = variants
            .Select(variant => variant.Id)
            .ToArray();

        List<EffectiveCampaignItemRow> activeItems =
            variantIds.Length == 0
                ? []
                : await _context.PriceCampaignItems
                .AsNoTracking()
                .Where(item =>
                    variantIds.Contains(item.VariantId)
                    && EffectiveStatuses.Contains(
                        item.Campaign.Status)
                    && item.Campaign.StartDate <= nowUtc
                    && (!item.Campaign.EndDate.HasValue
                        || item.Campaign.EndDate.Value > nowUtc))
                .OrderBy(item => item.Campaign.StartDate)
                .ThenBy(item => item.CampaignId)
                .Select(item => new EffectiveCampaignItemRow(
                    item.VariantId,
                    item.CampaignId,
                    item.Campaign.Code,
                    item.NewPrice))
                .ToListAsync(cancellationToken);

        var activeByVariant = activeItems
            .GroupBy(item => item.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        var rows = new List<PriceProjectionDriftRowViewModel>();

        foreach (var variant in variants)
        {
            activeByVariant.TryGetValue(
                variant.Id,
                out var candidates);
            candidates ??= [];

            decimal? expectedPrice;
            EffectivePriceSourceType? expectedSource;
            int? expectedSourceId;
            var ambiguous = candidates.Length > 1;

            if (candidates.Length == 0)
            {
                expectedPrice = variant.ListPrice;
                expectedSource =
                    EffectivePriceSourceType.ListPrice;
                expectedSourceId = null;
            }
            else if (candidates.Length == 1)
            {
                expectedPrice = candidates[0].NewPrice;
                expectedSource =
                    EffectivePriceSourceType.Campaign;
                expectedSourceId =
                    candidates[0].CampaignId;
            }
            else
            {
                expectedPrice = null;
                expectedSource = null;
                expectedSourceId = null;
            }

            var priceMismatch =
                expectedPrice.HasValue
                && variant.CurrentPrice
                    != expectedPrice.Value;
            var sourceMismatch =
                expectedSource.HasValue
                && (variant.SourceType
                        != expectedSource.Value
                    || variant.SourceId
                        != expectedSourceId);

            if (!ambiguous
                && !priceMismatch
                && !sourceMismatch)
            {
                continue;
            }

            var issue = ambiguous
                ? $"Có {candidates.Length} kế hoạch cùng hiệu lực: "
                    + string.Join(
                        ", ",
                        candidates.Select(item =>
                            item.CampaignCode))
                : priceMismatch && sourceMismatch
                    ? "Giá hiện tại và nguồn giá đều không khớp."
                    : priceMismatch
                        ? "Giá hiện tại không khớp giá được tính."
                        : "Nguồn giá hiện tại không khớp.";

            rows.Add(new PriceProjectionDriftRowViewModel
            {
                VariantId = variant.Id,
                ProductId = variant.ProductId,
                ProductName = variant.ProductName,
                Sku = variant.Sku,
                ListPrice = variant.ListPrice,
                StoredCurrentPrice =
                    variant.CurrentPrice,
                ExpectedCurrentPrice =
                    expectedPrice,
                StoredSourceType =
                    variant.SourceType,
                StoredSourceId =
                    variant.SourceId,
                ExpectedSourceType =
                    expectedSource,
                ExpectedSourceId =
                    expectedSourceId,
                EffectiveCampaignCount =
                    candidates.Length,
                IsAmbiguous = ambiguous,
                Issue = issue,
                Tone = ambiguous
                    ? "danger"
                    : "warning"
            });
        }

        return new ProjectionDriftResult(
            rows.Count,
            rows
                .OrderByDescending(row =>
                    row.IsAmbiguous)
                .ThenBy(row => row.ProductName)
                .ThenBy(row => row.Sku)
                .Take(100)
                .ToArray());
    }

    private static PriceRiskCampaignRowViewModel MapCampaign(
        PriceCampaign campaign,
        int overlapCount,
        PriceRiskAssessment assessment)
    {
        return new PriceRiskCampaignRowViewModel
        {
            CampaignId = campaign.Id,
            Code = campaign.Code,
            Name = campaign.Name,
            Status = campaign.Status,
            StatusLabel =
                StatusLabel(campaign.Status),
            StartLocal =
                ToBusinessTime(campaign.StartDate),
            EndLocal =
                campaign.EndDate.HasValue
                    ? ToBusinessTime(
                        campaign.EndDate.Value)
                    : null,
            VariantCount =
                campaign.CampaignItems.Count,
            CreatedBy = campaign.CreatedBy,
            ConfirmedBy = campaign.ConfirmedBy,
            RiskLevel = assessment.Level,
            RiskLabel =
                RiskLabel(assessment.Level),
            RiskTone =
                RiskTone(assessment.Level),
            RequiresSecondApprover =
                assessment.RequiresSecondApprover,
            CanCurrentActorConfirm =
                assessment.CanCurrentActorConfirm,
            ControlLabel =
                BuildControlLabel(
                    campaign,
                    assessment),
            MaximumDiscountPercent =
                assessment.MaximumDiscountPercent,
            MaximumThirtyDayLowUndercutPercent =
                assessment
                    .MaximumThirtyDayLowUndercutPercent,
            FrequentVariantCount =
                assessment.FrequentVariantCount,
            StaleSnapshotCount =
                assessment.StaleSnapshotCount,
            OverlapCampaignCount =
                overlapCount,
            Issues = assessment.Issues
                .OrderByDescending(issue =>
                    issue.Severity)
                .ThenBy(issue => issue.Code)
                .Select(issue =>
                    new PriceRiskIssueViewModel
                    {
                        Code = issue.Code,
                        Severity = issue.Severity,
                        SeverityLabel =
                            SeverityLabel(
                                issue.Severity),
                        Tone =
                            SeverityTone(
                                issue.Severity),
                        Title = issue.Title,
                        Detail = issue.Detail,
                        VariantId =
                            issue.VariantId,
                        Sku = issue.Sku
                    })
                .ToArray()
        };
    }

    private static int CountOverlaps(
        PriceCampaign campaign,
        IReadOnlyCollection<PriceCampaign> campaigns)
    {
        var campaignVariantIds = campaign.CampaignItems
            .Select(item => item.VariantId)
            .ToHashSet();

        if (campaignVariantIds.Count == 0)
        {
            return 0;
        }

        return campaigns.Count(other =>
            other.Id != campaign.Id
            && other.Status is
                PriceCampaignStatus.Confirmed
                or PriceCampaignStatus.Scheduled
                or PriceCampaignStatus.Active
            && WindowsOverlap(campaign, other)
            && other.CampaignItems.Any(item =>
                campaignVariantIds.Contains(
                    item.VariantId)));
    }

    private static bool WindowsOverlap(
        PriceCampaign left,
        PriceCampaign right)
    {
        var leftEnd =
            left.EndDate ?? DateTime.MaxValue;
        var rightEnd =
            right.EndDate ?? DateTime.MaxValue;

        return left.StartDate < rightEnd
            && right.StartDate < leftEnd;
    }

    private static string BuildControlLabel(
        PriceCampaign campaign,
        PriceRiskAssessment assessment)
    {
        if (assessment.HasBlockingIssues)
        {
            return "Đang có lỗi chặn";
        }

        if (assessment.RequiresSecondApprover)
        {
            return campaign.Status == PriceCampaignStatus.Draft
                ? assessment.CanCurrentActorConfirm
                    ? "Có thể xác nhận với maker-checker"
                    : "Cần quản trị viên khác xác nhận"
                : string.Equals(
                    campaign.CreatedBy,
                    campaign.ConfirmedBy,
                    StringComparison.OrdinalIgnoreCase)
                    ? "Không đạt maker-checker"
                    : "Đã có người xác nhận thứ hai";
        }

        return "Kiểm soát thông thường";
    }

    private string GetActor()
    {
        return string.IsNullOrWhiteSpace(
            User.Identity?.Name)
            ? "Admin"
            : User.Identity.Name.Trim();
    }

    private static DateTime ToBusinessTime(
        DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local =>
                value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(
                value,
                DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(
            utc,
            BusinessTimeZone);
    }

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        foreach (var identifier in new[]
        {
            "Asia/Ho_Chi_Minh",
            "SE Asia Standard Time"
        })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    identifier);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private static string StatusLabel(
        PriceCampaignStatus status)
    {
        return status switch
        {
            PriceCampaignStatus.Draft =>
                "Bản nháp",
            PriceCampaignStatus.Confirmed =>
                "Đã xác nhận",
            PriceCampaignStatus.Scheduled =>
                "Đã lên lịch",
            PriceCampaignStatus.Active =>
                "Đang hiệu lực",
            _ => status.ToString()
        };
    }

    private static string RiskLabel(
        PriceRiskLevel level)
    {
        return level switch
        {
            PriceRiskLevel.Critical =>
                "Nghiêm trọng",
            PriceRiskLevel.High =>
                "Cao",
            PriceRiskLevel.Medium =>
                "Trung bình",
            _ => "Thấp"
        };
    }

    private static string RiskTone(
        PriceRiskLevel level)
    {
        return level switch
        {
            PriceRiskLevel.Critical =>
                "danger",
            PriceRiskLevel.High =>
                "warning",
            PriceRiskLevel.Medium =>
                "info",
            _ => "success"
        };
    }

    private static string SeverityLabel(
        PriceRiskIssueSeverity severity)
    {
        return severity switch
        {
            PriceRiskIssueSeverity.Critical =>
                "Chặn",
            PriceRiskIssueSeverity.High =>
                "Rủi ro cao",
            PriceRiskIssueSeverity.Warning =>
                "Cảnh báo",
            _ => "Thông tin"
        };
    }

    private static string SeverityTone(
        PriceRiskIssueSeverity severity)
    {
        return severity switch
        {
            PriceRiskIssueSeverity.Critical =>
                "danger",
            PriceRiskIssueSeverity.High =>
                "warning",
            PriceRiskIssueSeverity.Warning =>
                "info",
            _ => "neutral"
        };
    }

    private sealed record RecentPriceHistoryRow(
        int VariantId,
        decimal OldPrice,
        decimal NewPrice,
        DateTime EffectiveAtUtc);

    private sealed record ActiveVariantRow(
        int Id,
        int ProductId,
        string ProductName,
        string Sku,
        decimal ListPrice,
        decimal CurrentPrice,
        EffectivePriceSourceType SourceType,
        int? SourceId);

    private sealed record EffectiveCampaignItemRow(
        int VariantId,
        int CampaignId,
        string CampaignCode,
        decimal NewPrice);

    private sealed record ProjectionDriftResult(
        int TotalCount,
        IReadOnlyList<PriceProjectionDriftRowViewModel>
            Rows);
}
