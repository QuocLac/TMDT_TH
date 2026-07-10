using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.PriceCampaigns;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class PriceCampaignsController : Controller
{
    private const string ConcurrencyMessage =
        "Dữ liệu vừa được thay đổi ở nơi khác. Vui lòng tải lại và thử lại.";

    private static readonly PriceCampaignStatus[] PricingBlockingStatuses =
    [
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

    private readonly ApplicationDbContext _context;
    private readonly IEffectivePriceService _effectivePriceService;
    private readonly IPriceCampaignWorkflowService _workflowService;
    private readonly ILogger<PriceCampaignsController> _logger;
    private readonly TimeProvider _timeProvider;

    public PriceCampaignsController(
        ApplicationDbContext context,
        IEffectivePriceService effectivePriceService,
        IPriceCampaignWorkflowService workflowService,
        ILogger<PriceCampaignsController> logger,
        TimeProvider timeProvider)
    {
        _context = context;
        _effectivePriceService = effectivePriceService;
        _workflowService = workflowService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var rows = await _context.PriceCampaigns
            .AsNoTracking()
            .OrderByDescending(campaign => campaign.CreatedAt)
            .Select(campaign => new PriceCampaignListItemViewModel
            {
                Id = campaign.Id,
                Code = campaign.Code,
                Name = campaign.Name,
                Description = campaign.Description,
                StartDateUtc = campaign.StartDate,
                EndDateUtc = campaign.EndDate,
                CreatedAtUtc = campaign.CreatedAt,
                ConfirmedAtUtc = campaign.ConfirmedAt,
                CancelledAtUtc = campaign.CancelledAt,
                Mode = campaign.Mode,
                Status = campaign.Status,
                SourceType = campaign.SourceType,
                ConflictPolicy = campaign.ConflictPolicy,
                VariantCount = campaign.CampaignItems.Count,
                SupersededByCampaignId = campaign.SupersededByCampaignId,
                CreatedBy = campaign.CreatedBy,
                RowVersion = Convert.ToBase64String(campaign.RowVersion)
            })
            .ToListAsync(cancellationToken);

        return View(new PriceCampaignIndexPageViewModel
        {
            UtcNow = nowUtc,
            Items = rows
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(
        int? id,
        CancellationToken cancellationToken)
    {
        var categoryRows = await _context.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new { category.Id, category.Name })
            .ToListAsync(cancellationToken);

        var brandRows = await _context.Brands
            .AsNoTracking()
            .Where(brand => brand.IsActive)
            .OrderBy(brand => brand.Name)
            .Select(brand => new { brand.Id, brand.Name })
            .ToListAsync(cancellationToken);

        return View(new PriceCampaignEditorPageViewModel
        {
            CampaignId = id.GetValueOrDefault(),
            CategoryOptions = categoryRows
                .Select(category => new SelectListItem(
                    category.Name,
                    category.Id.ToString()))
                .ToList(),
            BrandOptions = brandRows
                .Select(brand => new SelectListItem(
                    brand.Name,
                    brand.Id.ToString()))
                .ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetCampaign(
        int id,
        CancellationToken cancellationToken)
    {
        var campaign = await _context.PriceCampaigns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.CampaignItems)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .FirstOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (campaign is null)
        {
            return Json(new
            {
                success = false,
                message = "Không tìm thấy kế hoạch giá."
            });
        }

        return Json(new
        {
            success = true,
            data = new
            {
                id = campaign.Id,
                code = campaign.Code,
                name = campaign.Name,
                description = campaign.Description ?? string.Empty,
                mode = campaign.Mode.ToString(),
                status = campaign.Status.ToString(),
                reason = campaign.Reason,
                sourceType = campaign.SourceType.ToString(),
                conflictPolicy = campaign.ConflictPolicy.ToString(),
                clientRequestId = campaign.ClientRequestId,
                startDateUtc = ToUtcIso(campaign.StartDate),
                endDateUtc = ToUtcIso(campaign.EndDate),
                rowVersion = Convert.ToBase64String(campaign.RowVersion),
                items = campaign.CampaignItems
                    .OrderBy(item => item.Variant.SKU)
                    .Select(item => new
                    {
                        productId = item.Variant.ProductId,
                        productName = item.Variant.Product.Name,
                        variantId = item.VariantId,
                        sku = item.Variant.SKU,
                        name = item.Variant.Product.Name,
                        attributes = string.Join(
                            " - ",
                            new[]
                            {
                                item.Variant.Color,
                                item.Variant.Size
                            }.Where(value =>
                                !string.IsNullOrWhiteSpace(value))),
                        originalPrice = item.Variant.Price,
                        currentPrice = item.Variant.CurrentPrice,
                        newPrice = item.NewPrice,
                        adjustmentType =
                            item.AdjustmentType.ToString(),
                        adjustmentValue = item.AdjustmentValue,
                        variantRowVersion = Convert.ToBase64String(
                            item.Variant.RowVersion)
                    })
                    .ToList()
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetTimeline(
        int id,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return Json(new
            {
                success = false,
                message = "ID kế hoạch không hợp lệ."
            });
        }

        var campaign = await _context.PriceCampaigns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.SupersededByCampaign)
            .Include(item => item.SupersededCampaigns)
            .Include(item => item.CampaignItems)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (campaign is null)
        {
            return Json(new
            {
                success = false,
                message = "Không tìm thấy kế hoạch giá."
            });
        }

        var historyRows = await _context.PriceHistories
            .AsNoTracking()
            .Where(item => item.SourceId == id)
            .Include(item => item.ProductVariant)
                .ThenInclude(variant => variant.Product)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

        var events = new List<CampaignTimelineEvent>();
        events.Add(new CampaignTimelineEvent(
            campaign.CreatedAt,
            "Created",
            "Tạo kế hoạch",
            $"{campaign.CreatedBy} đã tạo kế hoạch {campaign.Code}.",
            null,
            null,
            null,
            campaign.CreatedBy,
            null));

        if (campaign.ConfirmedAt.HasValue)
        {
            events.Add(new CampaignTimelineEvent(
                campaign.ConfirmedAt.Value,
                "Confirmed",
                "Xác nhận kế hoạch",
                $"Kế hoạch được xác nhận với chính sách {campaign.ConflictPolicy}.",
                null,
                null,
                null,
                campaign.ConfirmedBy,
                null));

            events.Add(new CampaignTimelineEvent(
                campaign.StartDate,
                "EffectiveStart",
                "Bắt đầu hiệu lực",
                "Mốc thời gian kế hoạch bắt đầu tham gia tính giá hiệu lực.",
                null,
                null,
                null,
                null,
                null));
        }

        if (campaign.Status == PriceCampaignStatus.Completed
            && campaign.EndDate.HasValue)
        {
            events.Add(new CampaignTimelineEvent(
                campaign.EndDate.Value,
                "Completed",
                "Kết thúc hiệu lực",
                "Kế hoạch kết thúc theo thời gian đã cấu hình.",
                null,
                null,
                null,
                null,
                null));
        }

        if (campaign.CancelledAt.HasValue)
        {
            events.Add(new CampaignTimelineEvent(
                campaign.CancelledAt.Value,
                "Cancelled",
                "Hủy kế hoạch",
                "Kế hoạch bị dừng và giá hiệu lực được tính lại.",
                null,
                null,
                null,
                campaign.CancelledBy,
                null));
        }

        if (campaign.SupersededByCampaign is not null)
        {
            events.Add(new CampaignTimelineEvent(
                campaign.UpdatedAt ?? campaign.SupersededByCampaign.CreatedAt,
                "Superseded",
                "Bị thay thế",
                $"Được thay thế bởi {campaign.SupersededByCampaign.Code} - {campaign.SupersededByCampaign.Name}.",
                null,
                null,
                null,
                null,
                null));
        }

        events.AddRange(historyRows.Select(history =>
            new CampaignTimelineEvent(
                history.CreatedAt,
                history.EventType.ToString(),
                TranslateHistoryEvent(history.EventType),
                history.Note,
                history.OldPrice,
                history.NewPrice,
                history.ProductVariant.SKU,
                history.ChangedBy,
                history.CorrelationId)));

        return Json(new
        {
            success = true,
            data = new
            {
                campaign = new
                {
                    id = campaign.Id,
                    code = campaign.Code,
                    name = campaign.Name,
                    description = campaign.Description,
                    reason = campaign.Reason,
                    mode = campaign.Mode.ToString(),
                    status = campaign.Status.ToString(),
                    sourceType = campaign.SourceType.ToString(),
                    conflictPolicy = campaign.ConflictPolicy.ToString(),
                    startDateUtc = ToUtcIso(campaign.StartDate),
                    endDateUtc = ToUtcIso(campaign.EndDate),
                    createdAtUtc = ToUtcIso(campaign.CreatedAt),
                    confirmedAtUtc = ToUtcIso(campaign.ConfirmedAt),
                    cancelledAtUtc = ToUtcIso(campaign.CancelledAt),
                    createdBy = campaign.CreatedBy,
                    confirmedBy = campaign.ConfirmedBy,
                    cancelledBy = campaign.CancelledBy,
                    rowVersion = Convert.ToBase64String(campaign.RowVersion)
                },
                supersededBy = campaign.SupersededByCampaign is null
                    ? null
                    : new
                    {
                        id = campaign.SupersededByCampaign.Id,
                        code = campaign.SupersededByCampaign.Code,
                        name = campaign.SupersededByCampaign.Name,
                        status = campaign.SupersededByCampaign.Status.ToString()
                    },
                supersededCampaigns = campaign.SupersededCampaigns
                    .OrderBy(item => item.Id)
                    .Select(item => new
                    {
                        id = item.Id,
                        code = item.Code,
                        name = item.Name,
                        status = item.Status.ToString()
                    }),
                variants = campaign.CampaignItems
                    .OrderBy(item => item.Variant.Product.Name)
                    .ThenBy(item => item.Variant.SKU)
                    .Select(item => new
                    {
                        productName = item.Variant.Product.Name,
                        sku = item.Variant.SKU,
                        attributes = string.Join(
                            " - ",
                            new[] { item.Variant.Color, item.Variant.Size }
                                .Where(value => !string.IsNullOrWhiteSpace(value))),
                        listPrice = item.ListPriceSnapshot,
                        previousPrice = item.PreviousEffectivePriceSnapshot,
                        campaignPrice = item.NewPrice,
                        currentPrice = item.Variant.CurrentPrice,
                        adjustmentType = item.AdjustmentType.ToString(),
                        adjustmentValue = item.AdjustmentValue
                    }),
                events = events
                    .OrderByDescending(item => item.OccurredAtUtc)
                    .ThenByDescending(item => item.Title)
                    .Select(item => new
                    {
                        occurredAtUtc = ToUtcIso(item.OccurredAtUtc),
                        kind = item.Kind,
                        title = item.Title,
                        description = item.Description,
                        oldPrice = item.OldPrice,
                        newPrice = item.NewPrice,
                        sku = item.Sku,
                        changedBy = item.ChangedBy,
                        correlationId = item.CorrelationId
                    })
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        string? keyword,
        int? categoryId,
        int? brandId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 50);
        var normalizedKeyword = keyword?.Trim() ?? string.Empty;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var query = _context.Products
            .AsNoTracking()
            .Where(product =>
                product.IsActive
                && product.Variants.Any(variant => variant.IsActive));

        if (!string.IsNullOrEmpty(normalizedKeyword))
        {
            query = query.Where(product =>
                product.Name.Contains(normalizedKeyword)
                || product.Variants.Any(variant =>
                    variant.SKU.Contains(normalizedKeyword)));
        }

        if (categoryId is > 0)
        {
            query = query.Where(product =>
                product.CategoryId == categoryId.Value);
        }

        if (brandId is > 0)
        {
            query = query.Where(product =>
                product.BrandId == brandId.Value);
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new
            {
                product.Id,
                product.Name,
                Category = product.Category.Name,
                Brand = product.Brand != null
                    ? product.Brand.Name
                    : "Không có thương hiệu",
                Image = product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                VariantCount = product.Variants.Count(variant =>
                    variant.IsActive),
                MinListPrice = product.Variants
                    .Where(variant => variant.IsActive)
                    .Select(variant => (decimal?)variant.Price)
                    .Min(),
                MaxListPrice = product.Variants
                    .Where(variant => variant.IsActive)
                    .Select(variant => (decimal?)variant.Price)
                    .Max(),
                MinCurrentPrice = product.Variants
                    .Where(variant => variant.IsActive)
                    .Select(variant => (decimal?)variant.CurrentPrice)
                    .Min(),
                MaxCurrentPrice = product.Variants
                    .Where(variant => variant.IsActive)
                    .Select(variant => (decimal?)variant.CurrentPrice)
                    .Max(),
                ConfiguredVariantCount = product.Variants.Count(variant =>
                    variant.IsActive
                    && variant.CampaignItems.Any(item =>
                        PricingBlockingStatuses.Contains(item.Campaign.Status)
                        && (!item.Campaign.EndDate.HasValue
                            || item.Campaign.EndDate.Value > nowUtc)))
            })
            .ToListAsync(cancellationToken);

        return Json(new
        {
            success = true,
            data = new
            {
                page,
                pageSize,
                totalItems,
                totalPages,
                items = rows.Select(product => new
                {
                    id = product.Id,
                    name = product.Name,
                    category = product.Category,
                    brand = product.Brand,
                    image = string.IsNullOrWhiteSpace(product.Image)
                        ? "/images/no-image.png"
                        : product.Image,
                    variantCount = product.VariantCount,
                    configuredVariantCount = product.ConfiguredVariantCount,
                    minListPrice = product.MinListPrice,
                    maxListPrice = product.MaxListPrice,
                    minCurrentPrice = product.MinCurrentPrice,
                    maxCurrentPrice = product.MaxCurrentPrice
                })
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetProductVariants(
        int productId,
        CancellationToken cancellationToken)
    {
        if (productId <= 0)
        {
            return Json(new
            {
                success = false,
                message = "ID sản phẩm không hợp lệ."
            });
        }

        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.Id == productId && item.IsActive)
            .Select(item => new
            {
                item.Id,
                item.Name
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return Json(new
            {
                success = false,
                message = "Không tìm thấy sản phẩm."
            });
        }

        var variantRows = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.ProductId == productId
                && variant.IsActive)
            .OrderBy(variant => variant.SKU)
            .Select(variant => new
            {
                variant.Id,
                variant.SKU,
                variant.Color,
                variant.Size,
                variant.Price,
                variant.CurrentPrice,
                variant.CurrentPriceSourceType,
                variant.CurrentPriceSourceId,
                variant.CurrentPriceEffectiveFrom,
                variant.CurrentPriceEffectiveTo,
                variant.StockQuantity,
                variant.RowVersion
            })
            .ToListAsync(cancellationToken);

        var variantIds = variantRows
            .Select(variant => variant.Id)
            .ToArray();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var planRows = await _context.PriceCampaignItems
            .AsNoTracking()
            .Where(item =>
                variantIds.Contains(item.VariantId)
                && PricingBlockingStatuses.Contains(item.Campaign.Status)
                && (!item.Campaign.EndDate.HasValue
                    || item.Campaign.EndDate.Value > nowUtc))
            .OrderBy(item => item.Campaign.StartDate)
            .ThenBy(item => item.CampaignId)
            .Select(item => new
            {
                item.VariantId,
                item.CampaignId,
                item.Campaign.Code,
                item.Campaign.Name,
                item.Campaign.Status,
                item.Campaign.StartDate,
                item.Campaign.EndDate,
                item.NewPrice
            })
            .ToListAsync(cancellationToken);

        var plansByVariantId = planRows
            .GroupBy(item => item.VariantId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return Json(new
        {
            success = true,
            data = new
            {
                productId = product.Id,
                productName = product.Name,
                variants = variantRows.Select(variant =>
                {
                    plansByVariantId.TryGetValue(
                        variant.Id,
                        out var plans);
                    plans ??= [];

                    return new
                    {
                        id = variant.Id,
                        sku = variant.SKU,
                        attributes = string.Join(
                            " - ",
                            new[] { variant.Color, variant.Size }
                                .Where(value =>
                                    !string.IsNullOrWhiteSpace(value))),
                        listPrice = variant.Price,
                        currentPrice = variant.CurrentPrice,
                        priceSource = variant.CurrentPriceSourceType.ToString(),
                        priceSourceId = variant.CurrentPriceSourceId,
                        effectiveFromUtc = ToUtcIso(
                            variant.CurrentPriceEffectiveFrom),
                        effectiveToUtc = ToUtcIso(
                            variant.CurrentPriceEffectiveTo),
                        stockQuantity = variant.StockQuantity,
                        rowVersion = Convert.ToBase64String(
                            variant.RowVersion),
                        plans = plans.Select(plan => new
                        {
                            id = plan.CampaignId,
                            code = plan.Code,
                            name = plan.Name,
                            status = plan.Status.ToString(),
                            startDateUtc = ToUtcIso(plan.StartDate),
                            endDateUtc = ToUtcIso(plan.EndDate),
                            newPrice = plan.NewPrice
                        })
                    };
                })
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewPlan(
        [FromBody] PricePlanPreviewRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = CreateCorrelationId();
        Response.Headers["X-Correlation-ID"] = correlationId;

        if (request is null
            || request.Items is null
            || request.Items.Count == 0)
        {
            return Failure(
                "Vui lòng chọn ít nhất một biến thể để xem trước.",
                correlationId,
                "EMPTY_ITEMS");
        }

        if (!TryNormalizePlanTimes(
                request.Mode,
                request.StartDate,
                request.EndDate,
                out var startDateUtc,
                out var endDateUtc,
                out var timeError))
        {
            return Failure(
                timeError ?? "Thời gian kế hoạch không hợp lệ.",
                correlationId,
                "INVALID_TIME");
        }

        if (!TryCreatePreviewInputs(
                request.Items,
                out var inputs,
                out var inputError))
        {
            return Failure(
                inputError ?? "Dữ liệu biến thể không hợp lệ.",
                correlationId,
                "INVALID_ITEMS");
        }

        try
        {
            var preview = await _effectivePriceService.PreviewCampaignAsync(
                request.CampaignId,
                request.Mode,
                startDateUtc,
                endDateUtc,
                request.ConflictPolicy,
                inputs,
                cancellationToken);

            return new JsonResult(new
            {
                success = preview.IsValid,
                canConfirm = preview.CanConfirm,
                message = preview.ErrorMessage,
                errorCode = preview.ErrorCode,
                correlationId,
                data = CreatePreviewPayload(preview)
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
                "Pricing preview failure. CorrelationId={CorrelationId}.",
                correlationId);
            return Failure(
                "Không thể tính bản xem trước giá. Mã tra cứu: "
                    + correlationId,
                correlationId,
                "PREVIEW_FAILED");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(
        [FromBody] SavePriceCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = CreateCorrelationId();
        Response.Headers["X-Correlation-ID"] = correlationId;

        var requestError = ValidateRequest(request);
        if (requestError is not null)
        {
            return Failure(
                requestError,
                correlationId,
                "INVALID_REQUEST");
        }

        if (!TryNormalizePlanTimes(
                request!.Mode,
                request.StartDate,
                request.EndDate,
                out var startDateUtc,
                out var endDateUtc,
                out var timeError))
        {
            return Failure(
                timeError ?? "Thời gian kế hoạch không hợp lệ.",
                correlationId,
                "INVALID_TIME");
        }

        if (!TryCreatePreviewInputs(
                request.Items,
                out var inputs,
                out var inputError))
        {
            return Failure(
                inputError ?? "Dữ liệu biến thể không hợp lệ.",
                correlationId,
                "INVALID_ITEMS");
        }

        byte[]? expectedCampaignRowVersion = null;
        if (request.Id > 0
            && !TryDecodeRowVersion(
                request.RowVersion,
                true,
                out expectedCampaignRowVersion,
                out var rowVersionError))
        {
            return Failure(
                rowVersionError ?? ConcurrencyMessage,
                correlationId,
                "INVALID_ROW_VERSION");
        }

        var result = await _workflowService.SaveDraftAsync(
            new PriceCampaignDraftCommand(
                request.Id,
                request.Name,
                request.Description,
                request.Mode,
                startDateUtc,
                endDateUtc,
                request.Reason,
                request.SourceType,
                request.ConflictPolicy,
                NormalizeClientRequestId(request.ClientRequestId)
                    ?? Guid.NewGuid().ToString("N"),
                expectedCampaignRowVersion,
                inputs,
                GetActor(),
                correlationId),
            cancellationToken);

        return CreateWorkflowResponse(result, correlationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDraft(
        [FromBody] ConfirmPriceCampaignDraftRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = CreateCorrelationId();
        Response.Headers["X-Correlation-ID"] = correlationId;

        if (request is null || request.Id <= 0)
        {
            return Failure(
                "ID bản nháp không hợp lệ.",
                correlationId,
                "INVALID_REQUEST");
        }

        if (!TryDecodeRowVersion(
                request.RowVersion,
                true,
                out var expectedRowVersion,
                out var rowVersionError))
        {
            return Failure(
                rowVersionError ?? ConcurrencyMessage,
                correlationId,
                "INVALID_ROW_VERSION");
        }

        var result = await _workflowService.ConfirmDraftAsync(
            new ConfirmPriceCampaignCommand(
                request.Id,
                expectedRowVersion!,
                GetActor(),
                correlationId),
            cancellationToken);

        return CreateWorkflowResponse(result, correlationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelCampaign(
        [FromBody] CancelPriceCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = CreateCorrelationId();
        Response.Headers["X-Correlation-ID"] = correlationId;

        if (request is null
            || request.Id <= 0
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Failure(
                "Vui lòng nhập đầy đủ lý do hủy kế hoạch.",
                correlationId,
                "INVALID_REQUEST");
        }

        if (!TryDecodeRowVersion(
                request.RowVersion,
                true,
                out var expectedRowVersion,
                out var rowVersionError))
        {
            return Failure(
                rowVersionError ?? ConcurrencyMessage,
                correlationId,
                "INVALID_ROW_VERSION");
        }

        var result = await _workflowService.CancelAsync(
            new CancelPriceCampaignCommand(
                request.Id,
                expectedRowVersion!,
                request.Reason,
                GetActor(),
                correlationId),
            cancellationToken);

        return CreateWorkflowResponse(result, correlationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoverCampaign(
        [FromBody] RecoverPriceCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = CreateCorrelationId();
        Response.Headers["X-Correlation-ID"] = correlationId;

        var clientRequestId = NormalizeClientRequestId(request?.ClientRequestId);
        if (request is null
            || request.Id <= 0
            || string.IsNullOrWhiteSpace(request.Reason)
            || string.IsNullOrWhiteSpace(clientRequestId))
        {
            return Failure(
                "Vui lòng nhập đầy đủ lý do và khóa yêu cầu phục hồi.",
                correlationId,
                "INVALID_REQUEST");
        }

        if (!TryDecodeRowVersion(
                request.RowVersion,
                true,
                out var expectedRowVersion,
                out var rowVersionError))
        {
            return Failure(
                rowVersionError ?? ConcurrencyMessage,
                correlationId,
                "INVALID_ROW_VERSION");
        }

        var result = await _workflowService.RecoverAsync(
            new RecoverPriceCampaignCommand(
                request.Id,
                expectedRowVersion!,
                request.Reason,
                clientRequestId!,
                GetActor(),
                correlationId),
            cancellationToken);

        return CreateWorkflowResponse(result, correlationId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SaveCampaign(
        [FromBody] SavePriceCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        // Compatibility endpoint: all writes now pass through the draft workflow.
        // Confirmed, scheduled and active campaigns are immutable and must be
        // replaced through the lifecycle policies instead of being edited in place.
        return SaveDraft(request, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyNow(
        [FromBody] ApplyPriceCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = CreateCorrelationId();
        Response.Headers["X-Correlation-ID"] = correlationId;

        if (request is null || request.Id <= 0)
        {
            return Failure(
                "ID kế hoạch không hợp lệ.",
                correlationId,
                "INVALID_REQUEST");
        }

        if (!TryDecodeRowVersion(
                request.RowVersion,
                true,
                out var expectedRowVersion,
                out var rowVersionError))
        {
            return Failure(
                rowVersionError ?? ConcurrencyMessage,
                correlationId,
                "INVALID_ROW_VERSION");
        }

        var result = await _workflowService.ActivateNowAsync(
            new ActivatePriceCampaignCommand(
                request.Id,
                expectedRowVersion!,
                GetActor(),
                correlationId),
            cancellationToken);

        return CreateWorkflowResponse(result, correlationId);
    }

    private static object CreatePreviewPayload(
        PricePlanPreviewResult preview)
    {
        return new
        {
            summary = new
            {
                productCount = preview.Summary.ProductCount,
                variantCount = preview.Summary.VariantCount,
                increaseCount = preview.Summary.IncreaseCount,
                decreaseCount = preview.Summary.DecreaseCount,
                unchangedCount = preview.Summary.UnchangedCount,
                conflictCount = preview.Summary.ConflictCount,
                conflictCampaignCount = preview.Summary.ConflictCampaignCount,
                partialConflictCampaignCount = preview.Summary.PartialConflictCampaignCount,
                staleCount = preview.Summary.StaleCount,
                currentTotal = preview.Summary.CurrentTotal,
                newTotal = preview.Summary.NewTotal
            },
            items = preview.Items.Select(item => new
            {
                productId = item.ProductId,
                productName = item.ProductName,
                variantId = item.VariantId,
                sku = item.Sku,
                attributes = item.Attributes,
                rowVersion = Convert.ToBase64String(item.RowVersion),
                listPrice = item.ListPrice,
                currentPrice = item.CurrentPrice,
                newPrice = item.NewPrice,
                deltaAmount = item.DeltaAmount,
                deltaPercent = item.DeltaPercent,
                adjustmentType = item.AdjustmentType.ToString(),
                adjustmentValue = item.AdjustmentValue,
                isStale = item.IsStale,
                conflicts = item.Conflicts.Select(conflict => new
                {
                    campaignId = conflict.CampaignId,
                    code = conflict.CampaignCode,
                    name = conflict.CampaignName,
                    status = conflict.Status.ToString(),
                    startDateUtc = ToUtcIso(conflict.StartDateUtc),
                    endDateUtc = ToUtcIso(conflict.EndDateUtc),
                    campaignVariantCount = conflict.CampaignVariantCount,
                    coveredVariantCount = conflict.CoveredVariantCount,
                    isFullyCovered = conflict.IsFullyCovered
                })
            })
        };
    }

    private static IActionResult CreateWorkflowResponse(
        PriceCampaignWorkflowResult result,
        string correlationId)
    {
        return new JsonResult(new
        {
            success = result.Success,
            message = result.Message,
            errorCode = result.ErrorCode,
            correlationId,
            campaignId = result.CampaignId,
            code = result.CampaignCode,
            status = result.Status.ToString(),
            rowVersion = result.RowVersion.Length > 0
                ? Convert.ToBase64String(result.RowVersion)
                : null,
            clientRequestId = result.ClientRequestId,
            canConfirm = result.Preview?.CanConfirm,
            data = result.Preview is null
                ? null
                : CreatePreviewPayload(result.Preview)
        });
    }

    private static bool TryCreatePreviewInputs(
        IReadOnlyCollection<PricePlanPreviewItemRequest> requestItems,
        out PricePlanPreviewInput[] inputs,
        out string? errorMessage)
    {
        var result = new List<PricePlanPreviewInput>(requestItems.Count);

        foreach (var item in requestItems)
        {
            if (!TryDecodeRowVersion(
                    item.VariantRowVersion,
                    false,
                    out var rowVersion,
                    out errorMessage))
            {
                inputs = [];
                return false;
            }

            result.Add(new PricePlanPreviewInput(
                item.VariantId,
                item.AdjustmentType,
                item.AdjustmentValue,
                rowVersion));
        }

        inputs = result.ToArray();
        errorMessage = null;
        return true;
    }

    private static bool TryCreatePreviewInputs(
        IReadOnlyCollection<SavePriceCampaignItemRequest> requestItems,
        out PricePlanPreviewInput[] inputs,
        out string? errorMessage)
    {
        var result = new List<PricePlanPreviewInput>(requestItems.Count);

        foreach (var item in requestItems)
        {
            if (!TryDecodeRowVersion(
                    item.VariantRowVersion,
                    false,
                    out var rowVersion,
                    out errorMessage))
            {
                inputs = [];
                return false;
            }

            var adjustmentValue = item.AdjustmentValue > 0
                ? item.AdjustmentValue
                : item.NewPrice;

            result.Add(new PricePlanPreviewInput(
                item.VariantId,
                item.AdjustmentType,
                adjustmentValue,
                rowVersion));
        }

        inputs = result.ToArray();
        errorMessage = null;
        return true;
    }

    private static bool TryDecodeRowVersion(
        string? encodedValue,
        bool required,
        out byte[]? rowVersion,
        out string? errorMessage)
    {
        rowVersion = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(encodedValue))
        {
            if (required)
            {
                errorMessage = "Thiếu phiên bản dữ liệu. Vui lòng tải lại trang.";
                return false;
            }

            return true;
        }

        try
        {
            var decoded = Convert.FromBase64String(encodedValue);
            if (decoded.Length != 8)
            {
                errorMessage = "Phiên bản dữ liệu không hợp lệ.";
                return false;
            }

            rowVersion = decoded;
            return true;
        }
        catch (FormatException)
        {
            errorMessage = "Phiên bản dữ liệu không hợp lệ.";
            return false;
        }
    }

    private static bool TryNormalizePlanTimes(
        PriceCampaignMode mode,
        DateTime startDate,
        DateTime? endDate,
        out DateTime startDateUtc,
        out DateTime? endDateUtc,
        out string? errorMessage)
    {
        startDateUtc = default;
        endDateUtc = null;
        errorMessage = null;

        if (!TryNormalizeToUtc(startDate, out startDateUtc))
        {
            errorMessage = "Thời gian bắt đầu không hợp lệ.";
            return false;
        }

        if (mode == PriceCampaignMode.FixedWindow)
        {
            if (!TryNormalizeToUtc(endDate, out endDateUtc))
            {
                errorMessage = "Thời gian kết thúc không hợp lệ.";
                return false;
            }

            if (endDateUtc.Value <= startDateUtc)
            {
                errorMessage = "Thời gian kết thúc phải sau thời gian bắt đầu.";
                return false;
            }
        }
        else if (endDate.HasValue)
        {
            errorMessage = "Kế hoạch không thời hạn không được có thời gian kết thúc.";
            return false;
        }

        return true;
    }

    private string? ValidateRequest(
        SavePriceCampaignRequest? request)
    {
        if (request is null)
        {
            return "Dữ liệu kế hoạch giá không hợp lệ.";
        }

        if (!ModelState.IsValid)
        {
            return ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault(message =>
                    !string.IsNullOrWhiteSpace(message))
                ?? "Dữ liệu kế hoạch giá không hợp lệ.";
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Vui lòng nhập tên kế hoạch giá.";
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return "Vui lòng nhập lý do thay đổi giá.";
        }

        if (request.StartDate == default)
        {
            return "Vui lòng nhập thời gian bắt đầu.";
        }

        if (request.Mode
            == PriceCampaignMode.FixedWindow
            && !request.EndDate.HasValue)
        {
            return "Kế hoạch có thời hạn phải có thời gian kết thúc.";
        }

        if (request.Mode
            == PriceCampaignMode.OpenEnded
            && request.EndDate.HasValue)
        {
            return "Kế hoạch không thời hạn không được có thời gian kết thúc.";
        }

        if (request.Id > 0
            && string.IsNullOrWhiteSpace(
                request.RowVersion))
        {
            return "Thiếu phiên bản dữ liệu. Vui lòng tải lại trang.";
        }

        if (request.Items is null
            || request.Items.Count == 0)
        {
            return "Vui lòng chọn ít nhất một biến thể.";
        }

        return null;
    }

    private async Task<PriceCampaign?>
        FindIdempotentResultAsync(
            string clientRequestId,
            CancellationToken cancellationToken)
    {
        return await _context.PriceCampaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.ClientRequestId
                    == clientRequestId,
                cancellationToken);
    }

    private bool TryApplyExpectedRowVersion(
        PriceCampaign campaign,
        string? encodedRowVersion,
        out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(
                encodedRowVersion))
        {
            errorMessage =
                "Thiếu phiên bản dữ liệu. Vui lòng tải lại trang.";
            return false;
        }

        try
        {
            var expectedRowVersion =
                Convert.FromBase64String(
                    encodedRowVersion);

            if (expectedRowVersion.Length != 8)
            {
                errorMessage =
                    "Phiên bản dữ liệu không hợp lệ.";
                return false;
            }

            _context.Entry(campaign)
                .Property(item => item.RowVersion)
                .OriginalValue = expectedRowVersion;

            return true;
        }
        catch (FormatException)
        {
            errorMessage =
                "Phiên bản dữ liệu không hợp lệ.";
            return false;
        }
    }

    private void UpdateCampaignItems(
        PriceCampaign campaign,
        IReadOnlyCollection<
            SavePriceCampaignItemRequest> requestedItems,
        IReadOnlyDictionary<int, ProductVariant>
            variantsById)
    {
        var requestedByVariantId =
            requestedItems.ToDictionary(
                item => item.VariantId);

        var removedItems =
            campaign.CampaignItems
                .Where(item =>
                    !requestedByVariantId.ContainsKey(
                        item.VariantId))
                .ToArray();

        _context.PriceCampaignItems.RemoveRange(
            removedItems);

        var existingByVariantId =
            campaign.CampaignItems
                .Where(item =>
                    requestedByVariantId.ContainsKey(
                        item.VariantId))
                .ToDictionary(
                    item => item.VariantId);

        foreach (var requestedItem in requestedItems)
        {
            var variant =
                variantsById[requestedItem.VariantId];

            if (existingByVariantId.TryGetValue(
                    requestedItem.VariantId,
                    out var existingItem))
            {
                ApplyItemConfiguration(
                    existingItem,
                    variant,
                    requestedItem);
            }
            else
            {
                campaign.CampaignItems.Add(
                    CreateCampaignItem(
                        variant,
                        requestedItem));
            }
        }
    }

    private static PriceCampaignItem
        CreateCampaignItem(
            ProductVariant variant,
            SavePriceCampaignItemRequest request)
    {
        var item = new PriceCampaignItem
        {
            VariantId = variant.Id
        };

        ApplyItemConfiguration(
            item,
            variant,
            request);

        return item;
    }

    private static void ApplyItemConfiguration(
        PriceCampaignItem item,
        ProductVariant variant,
        SavePriceCampaignItemRequest request)
    {
        item.ListPriceSnapshot = variant.Price;
        item.EffectivePriceSnapshot =
            variant.CurrentPrice;
        item.PreviousEffectivePriceSnapshot =
            variant.CurrentPrice;
        item.AdjustmentType =
            request.AdjustmentType;
        item.AdjustmentValue =
            request.AdjustmentValue > 0
                ? request.AdjustmentValue
                : request.NewPrice;
        item.NewPrice = request.NewPrice;
        item.Currency = "VND";
    }

    private string GetActor()
    {
        var identity = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(identity)
            ? "Admin UI"
            : identity.Trim();
    }

    private static IActionResult Failure(
        string message,
        string correlationId,
        string errorCode)
    {
        return new JsonResult(new
        {
            success = false,
            message,
            errorCode,
            correlationId
        });
    }

    private static IActionResult Success(
        PriceCampaign campaign,
        string correlationId,
        string message)
    {
        return new JsonResult(new
        {
            success = true,
            campaignId = campaign.Id,
            code = campaign.Code,
            status = campaign.Status.ToString(),
            rowVersion =
                Convert.ToBase64String(
                    campaign.RowVersion),
            clientRequestId =
                campaign.ClientRequestId,
            correlationId,
            message
        });
    }

    private static DatabaseError MapDatabaseError(
        DbUpdateException exception)
    {
        var sqlException = FindSqlException(exception);

        return sqlException?.Number switch
        {
            2601 or 2627 => new DatabaseError(
                "Dữ liệu bị trùng. Vui lòng tải lại danh sách trước khi thử lại.",
                "UNIQUE_CONSTRAINT"),
            547 => new DatabaseError(
                "Dữ liệu liên quan không còn hợp lệ hoặc vi phạm quy tắc giá.",
                "REFERENCE_OR_CHECK_CONSTRAINT"),
            1205 => new DatabaseError(
                "Hệ thống đang xử lý một thay đổi giá khác. Vui lòng thử lại.",
                "DATABASE_DEADLOCK"),
            _ => new DatabaseError(
                "Không thể ghi dữ liệu giá. Hãy dùng mã tra cứu trong phản hồi để kiểm tra log.",
                "DATABASE_WRITE_FAILED")
        };
    }

    private static SqlException? FindSqlException(
        Exception exception)
    {
        Exception? current = exception;

        while (current is not null)
        {
            if (current is SqlException sqlException)
            {
                return sqlException;
            }

            current = current.InnerException;
        }

        return null;
    }

    private static string CreateCampaignCode(
        DateTime nowUtc)
    {
        var suffix = Guid.NewGuid()
            .ToString("N")[..10]
            .ToUpperInvariant();

        return $"PC-{nowUtc:yyyyMMdd}-{suffix}";
    }

    private string CreateCorrelationId()
    {
        var traceIdentifier =
            HttpContext.TraceIdentifier;

        if (!string.IsNullOrWhiteSpace(
                traceIdentifier))
        {
            var normalized =
                traceIdentifier.Replace(
                    ":",
                    string.Empty,
                    StringComparison.Ordinal);

            return normalized.Length <= 64
                ? normalized
                : normalized[..64];
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string? NormalizeClientRequestId(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 64
            ? normalized
            : normalized[..64];
    }

    private static bool TryNormalizeToUtc(
        DateTime value,
        out DateTime utcValue)
    {
        utcValue = default;

        if (value == default)
        {
            return false;
        }

        try
        {
            utcValue = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local =>
                    value.ToUniversalTime(),
                _ => TimeZoneInfo.ConvertTimeToUtc(
                    value,
                    TimeZoneInfo.Local)
            };
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryNormalizeToUtc(
        DateTime? value,
        out DateTime? utcValue)
    {
        utcValue = null;

        if (!value.HasValue)
        {
            return false;
        }

        if (!TryNormalizeToUtc(
                value.Value,
                out var normalized))
        {
            return false;
        }

        utcValue = normalized;
        return true;
    }

    private static string ToUtcIso(
        DateTime value)
    {
        return DateTime.SpecifyKind(
            value,
            DateTimeKind.Utc).ToString("O");
    }

    private static string? ToUtcIso(
        DateTime? value)
    {
        return value.HasValue
            ? ToUtcIso(value.Value)
            : null;
    }

    private static string? CleanNullable(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string TranslateHistoryEvent(
        PriceHistoryEventType eventType)
    {
        return eventType switch
        {
            PriceHistoryEventType.Applied => "Áp dụng giá",
            PriceHistoryEventType.Restored => "Khôi phục giá",
            PriceHistoryEventType.Replaced => "Thay thế giá",
            PriceHistoryEventType.Cancelled => "Hủy và tính lại giá",
            PriceHistoryEventType.ListPriceChanged => "Đổi giá niêm yết",
            _ => "Biến động giá"
        };
    }

    private sealed record CampaignTimelineEvent(
        DateTime OccurredAtUtc,
        string Kind,
        string Title,
        string Description,
        decimal? OldPrice,
        decimal? NewPrice,
        string? Sku,
        string? ChangedBy,
        string? CorrelationId);

    private sealed record DatabaseError(
        string Message,
        string Code);
}
