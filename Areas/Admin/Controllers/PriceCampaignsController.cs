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
                Mode = campaign.Mode,
                Status = campaign.Status,
                SourceType = campaign.SourceType,
                VariantCount = campaign.CampaignItems.Count,
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
    public async Task<IActionResult> SaveCampaign(
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

        if (!TryNormalizeToUtc(
                request!.StartDate,
                out var startDateUtc))
        {
            return Failure(
                "Thời gian bắt đầu không hợp lệ.",
                correlationId,
                "INVALID_START_TIME");
        }

        DateTime? endDateUtc = null;
        if (request.Mode == PriceCampaignMode.FixedWindow)
        {
            if (!TryNormalizeToUtc(
                    request.EndDate,
                    out endDateUtc))
            {
                return Failure(
                    "Thời gian kết thúc không hợp lệ.",
                    correlationId,
                    "INVALID_END_TIME");
            }
        }

        var clientRequestId =
            NormalizeClientRequestId(request.ClientRequestId)
            ?? Guid.NewGuid().ToString("N");

        if (!TryCreatePreviewInputs(
                request.Items,
                out var authoritativeInputs,
                out var authoritativeInputError))
        {
            return Failure(
                authoritativeInputError ?? "Dữ liệu biến thể không hợp lệ.",
                correlationId,
                "INVALID_ITEMS");
        }

        var authoritativePreview =
            await _effectivePriceService.PreviewCampaignAsync(
                request.Id,
                request.Mode,
                startDateUtc,
                endDateUtc,
                request.ConflictPolicy,
                authoritativeInputs,
                cancellationToken);

        if (!authoritativePreview.IsValid
            || !authoritativePreview.CanConfirm)
        {
            return Failure(
                authoritativePreview.ErrorMessage
                    ?? "Kế hoạch giá chưa đủ điều kiện để lưu.",
                correlationId,
                authoritativePreview.ErrorCode
                    ?? "PRICING_VALIDATION_FAILED");
        }

        var authoritativeByVariantId = authoritativePreview.Items
            .ToDictionary(item => item.VariantId);

        foreach (var requestItem in request.Items)
        {
            requestItem.NewPrice =
                authoritativeByVariantId[requestItem.VariantId].NewPrice;
        }

        var requestedItems = request.Items
            .Select(item => new CampaignPriceInput(
                item.VariantId,
                item.NewPrice))
            .ToArray();

        var actor = GetActor();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (request.Id == 0)
        {
            var previousResult =
                await FindIdempotentResultAsync(
                    clientRequestId,
                    cancellationToken);

            if (previousResult is not null)
            {
                return Success(
                    previousResult,
                    correlationId,
                    "Yêu cầu này đã được xử lý trước đó.");
            }
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            PriceCampaign campaign;
            int[] oldVariantIds;

            if (request.Id == 0)
            {
                var duplicateRequest =
                    await _context.PriceCampaigns
                        .FirstOrDefaultAsync(
                            item =>
                                item.ClientRequestId
                                == clientRequestId,
                            cancellationToken);

                if (duplicateRequest is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Success(
                        duplicateRequest,
                        correlationId,
                        "Yêu cầu này đã được xử lý trước đó.");
                }

                campaign = new PriceCampaign
                {
                    Code = CreateCampaignCode(nowUtc),
                    Name = request.Name.Trim(),
                    Description =
                        CleanNullable(request.Description),
                    Mode = request.Mode,
                    Status =
                        PriceCampaignLifecycle
                            .ResolveConfirmedStatus(
                                startDateUtc,
                                endDateUtc,
                                nowUtc),
                    StartDate = startDateUtc,
                    EndDate = endDateUtc,
                    Reason = request.Reason.Trim(),
                    SourceType = request.SourceType,
                    ConflictPolicy =
                        request.ConflictPolicy,
                    ClientRequestId = clientRequestId,
                    ConfirmedAt = nowUtc,
                    ConfirmedBy = actor,
                    CreatedBy = actor,
                    CampaignItems = []
                };
                campaign.IsActive =
                    PriceCampaignLifecycle
                        .IsCompatibilityActive(
                            campaign.Status);
                oldVariantIds = [];
            }
            else
            {
                campaign = await _context.PriceCampaigns
                    .Include(item => item.CampaignItems)
                    .FirstOrDefaultAsync(
                        item => item.Id == request.Id,
                        cancellationToken);

                if (campaign is null)
                {
                    return Failure(
                        "Không tìm thấy kế hoạch giá.",
                        correlationId,
                        "CAMPAIGN_NOT_FOUND");
                }

                if (PriceCampaignLifecycle.IsTerminal(
                        campaign.Status))
                {
                    return Failure(
                        "Kế hoạch đã kết thúc hoặc bị hủy nên không thể chỉnh sửa.",
                        correlationId,
                        "TERMINAL_CAMPAIGN");
                }

                if (campaign.Status
                    == PriceCampaignStatus.Active)
                {
                    return Failure(
                        "Kế hoạch đang có hiệu lực. Hãy dùng thao tác thay thế giá ở phase tiếp theo thay vì sửa lịch sử.",
                        correlationId,
                        "ACTIVE_CAMPAIGN_IMMUTABLE");
                }

                if (!TryApplyExpectedRowVersion(
                        campaign,
                        request.RowVersion,
                        out var rowVersionError))
                {
                    return Failure(
                        rowVersionError
                            ?? ConcurrencyMessage,
                        correlationId,
                        "INVALID_ROW_VERSION");
                }

                oldVariantIds = campaign.CampaignItems
                    .Select(item => item.VariantId)
                    .ToArray();
            }

            var validation =
                await _effectivePriceService
                    .ValidateCampaignAsync(
                        request.Id,
                        request.Mode,
                        startDateUtc,
                        endDateUtc,
                        request.ConflictPolicy,
                        requestedItems,
                        cancellationToken);

            if (!validation.IsValid)
            {
                return Failure(
                    validation.ErrorMessage
                        ?? "Dữ liệu giá không hợp lệ.",
                    correlationId,
                    validation.ErrorCode
                        ?? "PRICING_VALIDATION_FAILED");
            }

            var variantIds = requestedItems
                .Select(item => item.VariantId)
                .Distinct()
                .ToArray();

            var variantsById =
                await _context.ProductVariants
                    .Where(variant =>
                        variantIds.Contains(variant.Id))
                    .ToDictionaryAsync(
                        variant => variant.Id,
                        cancellationToken);

            if (request.Id == 0)
            {
                foreach (var requestItem in request.Items)
                {
                    var variant =
                        variantsById[requestItem.VariantId];
                    campaign.CampaignItems.Add(
                        CreateCampaignItem(
                            variant,
                            requestItem));
                }

                _context.PriceCampaigns.Add(campaign);
            }
            else
            {
                campaign.Name = request.Name.Trim();
                campaign.Description =
                    CleanNullable(request.Description);
                campaign.Mode = request.Mode;
                campaign.StartDate = startDateUtc;
                campaign.EndDate = endDateUtc;
                campaign.Reason =
                    request.Reason.Trim();
                campaign.SourceType =
                    request.SourceType;
                campaign.ConflictPolicy =
                    request.ConflictPolicy;
                campaign.Status =
                    PriceCampaignLifecycle
                        .ResolveConfirmedStatus(
                            startDateUtc,
                            endDateUtc,
                            nowUtc);
                campaign.IsActive =
                    PriceCampaignLifecycle
                        .IsCompatibilityActive(
                            campaign.Status);
                campaign.UpdatedAt = nowUtc;

                UpdateCampaignItems(
                    campaign,
                    request.Items,
                    variantsById);
            }

            await _context.SaveChangesAsync(
                cancellationToken);

            var affectedVariantIds = oldVariantIds
                .Concat(variantIds)
                .Distinct()
                .ToArray();

            await _effectivePriceService
                .RecalculateVariantsAsync(
                    affectedVariantIds,
                    actor,
                    request.Id == 0
                        ? "Xác nhận kế hoạch giá"
                        : "Cập nhật kế hoạch giá đã lên lịch",
                    correlationId,
                    cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            _logger.LogInformation(
                "Pricing save succeeded. CorrelationId={CorrelationId}, CampaignId={CampaignId}, ClientRequestId={ClientRequestId}, Status={Status}, VariantCount={VariantCount}.",
                correlationId,
                campaign.Id,
                clientRequestId,
                campaign.Status,
                campaign.CampaignItems.Count);

            return Success(
                campaign,
                correlationId,
                "Đã lưu và xác nhận kế hoạch giá.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            _logger.LogWarning(
                exception,
                "Pricing save concurrency conflict. CorrelationId={CorrelationId}, CampaignId={CampaignId}.",
                correlationId,
                request.Id);

            return Failure(
                ConcurrencyMessage,
                correlationId,
                "CONCURRENCY_CONFLICT");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            var error = MapDatabaseError(exception);

            _logger.LogError(
                exception,
                "Pricing database error. CorrelationId={CorrelationId}, CampaignId={CampaignId}, ClientRequestId={ClientRequestId}, ErrorCode={ErrorCode}.",
                correlationId,
                request.Id,
                clientRequestId,
                error.Code);

            return Failure(
                error.Message,
                correlationId,
                error.Code);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            _logger.LogError(
                exception,
                "Unexpected pricing save failure. CorrelationId={CorrelationId}, CampaignId={CampaignId}, ClientRequestId={ClientRequestId}.",
                correlationId,
                request.Id,
                clientRequestId);

            return Failure(
                "Không thể lưu kế hoạch giá. Mã tra cứu: "
                + correlationId,
                correlationId,
                "UNEXPECTED_ERROR");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyNow(
        [FromBody] ApplyPriceCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        var correlationId = CreateCorrelationId();
        Response.Headers["X-Correlation-ID"] =
            correlationId;

        if (request is null
            || request.Id <= 0
            || string.IsNullOrWhiteSpace(
                request.RowVersion))
        {
            return Failure(
                "Dữ liệu kế hoạch giá không hợp lệ.",
                correlationId,
                "INVALID_REQUEST");
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var campaign =
                await _context.PriceCampaigns
                    .Include(item => item.CampaignItems)
                    .FirstOrDefaultAsync(
                        item => item.Id == request.Id,
                        cancellationToken);

            if (campaign is null)
            {
                return Failure(
                    "Không tìm thấy kế hoạch giá.",
                    correlationId,
                    "CAMPAIGN_NOT_FOUND");
            }

            if (PriceCampaignLifecycle.IsTerminal(
                    campaign.Status))
            {
                return Failure(
                    "Kế hoạch đã kết thúc hoặc bị hủy.",
                    correlationId,
                    "TERMINAL_CAMPAIGN");
            }

            if (!TryApplyExpectedRowVersion(
                    campaign,
                    request.RowVersion,
                    out var rowVersionError))
            {
                return Failure(
                    rowVersionError
                        ?? ConcurrencyMessage,
                    correlationId,
                    "INVALID_ROW_VERSION");
            }

            var nowUtc =
                _timeProvider.GetUtcNow().UtcDateTime;

            if (campaign.EndDate.HasValue
                && campaign.EndDate.Value <= nowUtc)
            {
                return Failure(
                    "Kế hoạch đã hết hạn. Hãy tạo kế hoạch thay thế.",
                    correlationId,
                    "CAMPAIGN_EXPIRED");
            }

            var requestedItems =
                campaign.CampaignItems
                    .Select(item =>
                        new CampaignPriceInput(
                            item.VariantId,
                            item.NewPrice))
                    .ToArray();

            var validation =
                await _effectivePriceService
                    .ValidateCampaignAsync(
                        campaign.Id,
                        campaign.Mode,
                        nowUtc,
                        campaign.EndDate,
                        campaign.ConflictPolicy,
                        requestedItems,
                        cancellationToken);

            if (!validation.IsValid)
            {
                return Failure(
                    validation.ErrorMessage
                        ?? "Không thể kích hoạt kế hoạch.",
                    correlationId,
                    validation.ErrorCode
                        ?? "PRICING_VALIDATION_FAILED");
            }

            campaign.StartDate = nowUtc;
            campaign.Status =
                PriceCampaignStatus.Active;
            campaign.IsActive = true;
            campaign.ConfirmedAt ??= nowUtc;
            campaign.ConfirmedBy ??= GetActor();
            campaign.UpdatedAt = nowUtc;

            await _context.SaveChangesAsync(
                cancellationToken);

            await _effectivePriceService
                .RecalculateVariantsAsync(
                    requestedItems
                        .Select(item => item.VariantId)
                        .ToArray(),
                    GetActor(),
                    "Kích hoạt kế hoạch giá ngay",
                    correlationId,
                    cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return Success(
                campaign,
                correlationId,
                "Đã kích hoạt kế hoạch giá.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            _logger.LogWarning(
                exception,
                "Pricing activation concurrency conflict. CorrelationId={CorrelationId}, CampaignId={CampaignId}.",
                correlationId,
                request.Id);

            return Failure(
                ConcurrencyMessage,
                correlationId,
                "CONCURRENCY_CONFLICT");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            var error = MapDatabaseError(exception);

            _logger.LogError(
                exception,
                "Pricing activation database error. CorrelationId={CorrelationId}, CampaignId={CampaignId}, ErrorCode={ErrorCode}.",
                correlationId,
                request.Id,
                error.Code);

            return Failure(
                error.Message,
                correlationId,
                error.Code);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            _logger.LogError(
                exception,
                "Unexpected pricing activation failure. CorrelationId={CorrelationId}, CampaignId={CampaignId}.",
                correlationId,
                request.Id);

            return Failure(
                "Không thể kích hoạt kế hoạch giá. Mã tra cứu: "
                + correlationId,
                correlationId,
                "UNEXPECTED_ERROR");
        }
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
                    endDateUtc = ToUtcIso(conflict.EndDateUtc)
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

    private sealed record DatabaseError(
        string Message,
        string Code);
}
