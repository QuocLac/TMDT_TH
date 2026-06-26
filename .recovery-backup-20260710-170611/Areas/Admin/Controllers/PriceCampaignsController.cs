using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.PriceCampaigns;
using WebApplication2.Models;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public class PriceCampaignsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IEffectivePriceService _effectivePriceService;
    private readonly ILogger<PriceCampaignsController> _logger;
    private readonly TimeProvider _timeProvider;

    public PriceCampaignsController(
        ApplicationDbContext context,
        IEffectivePriceService effectivePriceService,
        ILogger<PriceCampaignsController> logger,
        TimeProvider timeProvider)
    {
        _context = context;
        _effectivePriceService = effectivePriceService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var campaigns = await _context.PriceCampaigns
            .AsNoTracking()
            .OrderByDescending(campaign => campaign.CreatedAt)
            .ToListAsync(cancellationToken);

        return View(campaigns);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? id, CancellationToken cancellationToken)
    {
        ViewBag.Categories = await _context.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ToListAsync(cancellationToken);

        ViewBag.Brands = await _context.Brands
            .AsNoTracking()
            .Where(brand => brand.IsActive)
            .OrderBy(brand => brand.Name)
            .ToListAsync(cancellationToken);

        ViewBag.CampaignId = id ?? 0;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetCampaign(int id, CancellationToken cancellationToken)
    {
        var campaign = await _context.PriceCampaigns
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.CampaignItems)
                .ThenInclude(item => item.Variant)
                    .ThenInclude(variant => variant.Product)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (campaign is null)
        {
            return Json(new { success = false, message = "Không tìm thấy chiến dịch." });
        }

        var data = new
        {
            id = campaign.Id,
            name = campaign.Name,
            description = campaign.Description ?? string.Empty,
            startDate = ToLocalInputValue(campaign.StartDate),
            endDate = ToLocalInputValue(campaign.EndDate),
            rowVersion = Convert.ToBase64String(campaign.RowVersion),
            items = campaign.CampaignItems.Select(item => new
            {
                variantId = item.VariantId,
                sku = item.Variant.SKU,
                name = item.Variant.Product.Name,
                attributes = string.Join(" - ", new[] { item.Variant.Color, item.Variant.Size }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
                currentPrice = item.Variant.Price,
                newPrice = item.NewPrice
            }).ToList()
        };

        return Json(new { success = true, data });
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        string? keyword,
        int? categoryId,
        int? brandId,
        CancellationToken cancellationToken)
    {
        var query = _context.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(product => product.Category)
            .Include(product => product.Brand)
            .Include(product => product.Images)
            .Include(product => product.Variants)
            .Where(product => product.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim();
            query = query.Where(product => product.Name.Contains(normalizedKeyword)
                || product.Variants.Any(variant => variant.SKU.Contains(normalizedKeyword)));
        }

        if (categoryId is > 0)
        {
            query = query.Where(product => product.CategoryId == categoryId.Value);
        }

        if (brandId is > 0)
        {
            query = query.Where(product => product.BrandId == brandId.Value);
        }

        var products = await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var data = products.Select(product => new
        {
            id = product.Id,
            name = product.Name,
            category = product.Category?.Name ?? "Trống",
            brand = product.Brand?.Name ?? "Trống",
            image = product.Images.FirstOrDefault(image => image.IsMain)?.ImageUrl ?? "/images/no-image.png",
            variants = product.Variants
                .Where(variant => variant.IsActive)
                .OrderBy(variant => variant.SKU)
                .Select(variant => new
                {
                    id = variant.Id,
                    sku = variant.SKU,
                    attributes = string.Join(" - ", new[] { variant.Color, variant.Size }
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    originalPrice = variant.Price,
                    currentPrice = variant.CurrentPrice
                }).ToList()
        }).ToList();

        return Json(new { success = true, data });
    }

    [HttpPost]
    public async Task<IActionResult> SaveCampaign(
        [FromBody] SavePriceCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        var requestError = ValidateRequest(request);
        if (requestError is not null)
        {
            return Json(new { success = false, message = requestError });
        }

        if (!TryNormalizeToUtc(request!.StartDate, out var startDateUtc)
            || !TryNormalizeToUtc(request.EndDate, out var endDateUtc))
        {
            return Json(new { success = false, message = "Thời gian chiến dịch không hợp lệ." });
        }

        var requestedItems = request.Items
            .Select(item => new CampaignPriceInput(item.VariantId, item.NewPrice))
            .ToArray();

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            PriceCampaign campaign;
            int[] oldVariantIds;

            if (request.Id == 0)
            {
                campaign = new PriceCampaign
                {
                    Name = request.Name.Trim(),
                    Description = request.Description?.Trim(),
                    StartDate = startDateUtc,
                    EndDate = endDateUtc,
                    IsActive = true,
                    CampaignItems = []
                };
                oldVariantIds = [];
            }
            else
            {
                campaign = await _context.PriceCampaigns
                    .Include(item => item.CampaignItems)
                    .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

                if (campaign is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Json(new { success = false, message = "Không tìm thấy chiến dịch." });
                }

                if (!TryApplyExpectedRowVersion(campaign, request.RowVersion, out var rowVersionError))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Json(new { success = false, message = rowVersionError });
                }

                oldVariantIds = campaign.CampaignItems
                    .Select(item => item.VariantId)
                    .ToArray();
            }

            var validation = await _effectivePriceService.ValidateCampaignAsync(
                request.Id,
                startDateUtc,
                endDateUtc,
                requestedItems,
                cancellationToken);

            if (!validation.IsValid)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Json(new { success = false, message = validation.ErrorMessage });
            }

            if (request.Id == 0)
            {
                foreach (var item in requestedItems)
                {
                    campaign.CampaignItems.Add(new PriceCampaignItem
                    {
                        VariantId = item.VariantId,
                        NewPrice = item.NewPrice
                    });
                }

                _context.PriceCampaigns.Add(campaign);
            }
            else
            {
                campaign.Name = request.Name.Trim();
                campaign.Description = request.Description?.Trim();
                campaign.StartDate = startDateUtc;
                campaign.EndDate = endDateUtc;
                campaign.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                UpdateCampaignItems(campaign, requestedItems);
            }

            await _context.SaveChangesAsync(cancellationToken);

            var affectedVariantIds = oldVariantIds
                .Concat(requestedItems.Select(item => item.VariantId))
                .Distinct()
                .ToArray();

            await _effectivePriceService.RecalculateVariantsAsync(
                affectedVariantIds,
                "Hệ thống giá",
                request.Id == 0 ? "Tạo chiến dịch giá" : "Cập nhật chiến dịch giá",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                success = true,
                campaignId = campaign.Id,
                rowVersion = Convert.ToBase64String(campaign.RowVersion),
                message = "Lưu chiến dịch thành công."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(exception, "Concurrent update while saving price campaign {CampaignId}.", request.Id);
            return Json(new
            {
                success = false,
                message = "Chiến dịch vừa được thay đổi ở nơi khác. Vui lòng tải lại dữ liệu và thử lại."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(exception, "Failed to save price campaign {CampaignId}.", request.Id);
            return Json(new
            {
                success = false,
                message = "Không thể lưu chiến dịch lúc này. Vui lòng thử lại."
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ApplyNow(int id, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var campaign = await _context.PriceCampaigns
                .Include(item => item.CampaignItems)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (campaign is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Json(new { success = false, message = "Không tìm thấy chiến dịch." });
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (campaign.EndDate <= nowUtc)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Json(new
                {
                    success = false,
                    message = "Chiến dịch đã hết hạn. Hãy cập nhật thời gian kết thúc trước khi kích hoạt."
                });
            }

            var requestedItems = campaign.CampaignItems
                .Select(item => new CampaignPriceInput(item.VariantId, item.NewPrice))
                .ToArray();

            var validation = await _effectivePriceService.ValidateCampaignAsync(
                campaign.Id,
                nowUtc,
                campaign.EndDate,
                requestedItems,
                cancellationToken);

            if (!validation.IsValid)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Json(new { success = false, message = validation.ErrorMessage });
            }

            campaign.StartDate = nowUtc;
            campaign.IsActive = true;
            campaign.UpdatedAt = nowUtc;
            await _context.SaveChangesAsync(cancellationToken);

            await _effectivePriceService.RecalculateVariantsAsync(
                requestedItems.Select(item => item.VariantId).ToArray(),
                "Hệ thống giá",
                "Kích hoạt chiến dịch ngay",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                success = true,
                message = "Đã kích hoạt chiến dịch theo thời gian kết thúc hiện có."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(exception, "Concurrent update while applying price campaign {CampaignId}.", id);
            return Json(new
            {
                success = false,
                message = "Chiến dịch vừa được thay đổi ở nơi khác. Vui lòng tải lại và thử lại."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(exception, "Failed to apply price campaign {CampaignId}.", id);
            return Json(new
            {
                success = false,
                message = "Không thể kích hoạt chiến dịch lúc này. Vui lòng thử lại."
            });
        }
    }

    private string? ValidateRequest(SavePriceCampaignRequest? request)
    {
        if (request is null)
        {
            return "Dữ liệu chiến dịch không hợp lệ.";
        }

        if (!ModelState.IsValid)
        {
            return ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => error.ErrorMessage)
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                ?? "Dữ liệu chiến dịch không hợp lệ.";
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Vui lòng nhập tên chiến dịch.";
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return "Vui lòng chọn ít nhất một biến thể.";
        }

        return null;
    }

    private bool TryApplyExpectedRowVersion(
        PriceCampaign campaign,
        string? encodedRowVersion,
        out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(encodedRowVersion))
        {
            return true;
        }

        try
        {
            var expectedRowVersion = Convert.FromBase64String(encodedRowVersion);
            if (expectedRowVersion.Length != 8)
            {
                errorMessage = "Phiên bản dữ liệu chiến dịch không hợp lệ.";
                return false;
            }

            _context.Entry(campaign)
                .Property(item => item.RowVersion)
                .OriginalValue = expectedRowVersion;
            return true;
        }
        catch (FormatException)
        {
            errorMessage = "Phiên bản dữ liệu chiến dịch không hợp lệ.";
            return false;
        }
    }

    private void UpdateCampaignItems(
        PriceCampaign campaign,
        IReadOnlyCollection<CampaignPriceInput> requestedItems)
    {
        var requestedByVariantId = requestedItems.ToDictionary(item => item.VariantId);
        var removedItems = campaign.CampaignItems
            .Where(item => !requestedByVariantId.ContainsKey(item.VariantId))
            .ToArray();

        _context.PriceCampaignItems.RemoveRange(removedItems);

        var existingByVariantId = campaign.CampaignItems
            .Where(item => requestedByVariantId.ContainsKey(item.VariantId))
            .ToDictionary(item => item.VariantId);

        foreach (var requestedItem in requestedItems)
        {
            if (existingByVariantId.TryGetValue(requestedItem.VariantId, out var existingItem))
            {
                existingItem.NewPrice = requestedItem.NewPrice;
                continue;
            }

            campaign.CampaignItems.Add(new PriceCampaignItem
            {
                CampaignId = campaign.Id,
                VariantId = requestedItem.VariantId,
                NewPrice = requestedItem.NewPrice
            });
        }
    }

    private static bool TryNormalizeToUtc(DateTime value, out DateTime utcValue)
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
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)
            };
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ToLocalInputValue(DateTime utcValue)
    {
        return DateTime.SpecifyKind(utcValue, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("yyyy-MM-ddTHH:mm");
    }
}
