using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Services.Catalog;
using WebApplication2.ViewModels.Shared;
using WebApplication2.ViewModels.Storefront.Home;

namespace WebApplication2.Controllers;

public sealed class HomeController : Controller
{
    private const int SuggestedProductLimit = 24;
    private const int FlashSaleLimit = 8;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public HomeController(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? query,
        int? categoryId,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var categoryData = await _context.Categories
            .AsNoTracking()
            .Where(category =>
                category.ParentId == null
                && category.IsVisible)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .Select(category => new
            {
                category.Id,
                category.Name,
                category.Slug,
                category.IconKey,
                ProductCount = category.Products.Count(product => product.IsActive)
            })
            .Take(12)
            .ToListAsync(cancellationToken);

        var categories = categoryData
            .Select(category => new CategoryCardViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                IconKey = CategoryIconCatalog.NormalizeKey(category.IconKey),
                IconCssClass = CategoryIconCatalog.ResolveCssClass(category.IconKey),
                ProductCount = category.ProductCount
            })
            .ToArray();

        var effectiveCategoryId = categoryId is > 0
            && categories.Any(category => category.Id == categoryId.Value)
                ? categoryId
                : null;

        var suggestedQuery = _context.Products
            .AsNoTracking()
            .Where(product =>
                product.IsActive
                && product.Variants.Any(variant =>
                    variant.IsActive
                    && variant.Price > 0
                    && variant.CurrentPrice > 0));

        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            suggestedQuery = suggestedQuery.Where(product =>
                product.Name.Contains(normalizedQuery)
                || product.Variants.Any(variant =>
                    variant.SKU.Contains(normalizedQuery)));
        }

        if (effectiveCategoryId is > 0)
        {
            suggestedQuery = suggestedQuery.Where(product =>
                product.CategoryId == effectiveCategoryId.Value);
        }

        var suggestedProducts = await LoadProductCardsAsync(
            suggestedQuery.OrderByDescending(product => product.CreatedAt),
            SuggestedProductLimit,
            nowUtc,
            cancellationToken);

        IReadOnlyList<ProductCardViewModel> flashSaleProducts = [];

        if (string.IsNullOrEmpty(normalizedQuery) && categoryId is null)
        {
            var flashSaleQuery = _context.Products
                .AsNoTracking()
                .Where(product =>
                    product.IsActive
                    && product.Variants.Any(variant =>
                        variant.IsActive
                        && variant.CurrentPrice > 0
                        && variant.CurrentPrice < variant.Price
                        && variant.CampaignItems.Any(item =>
                            item.Campaign.IsActive
                            && item.Campaign.StartDate <= nowUtc
                            && item.Campaign.EndDate > nowUtc
                            && item.NewPrice == variant.CurrentPrice)))
                .OrderBy(product => product.Id);

            flashSaleProducts = await LoadProductCardsAsync(
                flashSaleQuery,
                FlashSaleLimit,
                nowUtc,
                cancellationToken);
        }

        var model = new HomePageViewModel
        {
            Query = normalizedQuery,
            CategoryId = effectiveCategoryId,
            Categories = categories,
            FlashSaleProducts = flashSaleProducts,
            SuggestedProducts = suggestedProducts,
            FlashSaleEndsAt = flashSaleProducts
                .Where(product => product.SaleEndsAt.HasValue)
                .Select(product => product.SaleEndsAt)
                .Min()
        };

        return View(model);
    }

    [HttpGet]
    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error(int? statusCode = null)
    {
        var effectiveStatusCode =
            statusCode ?? StatusCodes.Status500InternalServerError;

        Response.StatusCode = effectiveStatusCode;

        return View(new ErrorPageViewModel
        {
            StatusCode = effectiveStatusCode,
            Title = effectiveStatusCode == StatusCodes.Status404NotFound
                ? "Không tìm thấy trang"
                : "Đã xảy ra lỗi",
            Message = effectiveStatusCode == StatusCodes.Status404NotFound
                ? "Nội dung bạn cần không tồn tại hoặc đã được chuyển sang địa chỉ khác."
                : "Hệ thống chưa thể xử lý yêu cầu. Vui lòng thử lại sau.",
            RequestId =
                Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

    private static async Task<IReadOnlyList<ProductCardViewModel>> LoadProductCardsAsync(
        IQueryable<Product> query,
        int limit,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var products = await query
            .Take(limit)
            .Select(product => new
            {
                product.Id,
                product.Slug,
                product.Name,
                ImageUrl = product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                Variants = product.Variants
                    .Where(variant =>
                        variant.IsActive
                        && variant.Price > 0
                        && variant.CurrentPrice > 0)
                    .Select(variant => new
                    {
                        variant.Price,
                        variant.CurrentPrice,
                        variant.StockQuantity,
                        SaleEndsAt = variant.CampaignItems
                            .Where(item =>
                                item.Campaign.IsActive
                                && item.Campaign.StartDate <= nowUtc
                                && item.Campaign.EndDate > nowUtc
                                && item.NewPrice == variant.CurrentPrice)
                            .OrderByDescending(item => item.Campaign.StartDate)
                            .ThenByDescending(item => item.CampaignId)
                            .Select(item => (DateTime?)item.Campaign.EndDate)
                            .FirstOrDefault()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return products
            .Select(product =>
            {
                var lowestPriceVariant = product.Variants
                    .OrderBy(variant => variant.CurrentPrice)
                    .ThenBy(variant => variant.Price)
                    .FirstOrDefault();

                if (lowestPriceVariant is null)
                {
                    return null;
                }

                var isOnSale =
                    lowestPriceVariant.CurrentPrice < lowestPriceVariant.Price;

                var discountPercentage = isOnSale
                    ? (int)Math.Round(
                        (1 - lowestPriceVariant.CurrentPrice
                            / lowestPriceVariant.Price) * 100,
                        MidpointRounding.AwayFromZero)
                    : 0;

                return new ProductCardViewModel
                {
                    Id = product.Id,
                    Slug = product.Slug,
                    Name = product.Name,
                    ImageUrl = string.IsNullOrWhiteSpace(product.ImageUrl)
                        ? "/images/no-image.png"
                        : product.ImageUrl,
                    OriginalPrice = lowestPriceVariant.Price,
                    EffectivePrice = lowestPriceVariant.CurrentPrice,
                    DiscountPercentage = discountPercentage,
                    IsOnSale = isOnSale,
                    StockQuantity = product.Variants.Sum(
                        variant => Math.Max(0, variant.StockQuantity)),
                    VariantCount = product.Variants.Count,
                    AvailableVariantCount = product.Variants.Count(
                        variant => variant.StockQuantity > 0),
                    SaleEndsAt = lowestPriceVariant.SaleEndsAt.HasValue
                        ? new DateTimeOffset(
                            DateTime.SpecifyKind(
                                lowestPriceVariant.SaleEndsAt.Value,
                                DateTimeKind.Utc))
                        : null
                };
            })
            .Where(product => product is not null)
            .Cast<ProductCardViewModel>()
            .ToList();
    }
}
