using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.ViewModels.Storefront.Products;

namespace WebApplication2.Controllers;

[Route("products")]
public sealed class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ProductsController(ApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.IsActive && item.Slug == slug)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Slug,
                item.Description,
                item.MetaTitle,
                item.MetaDescription,
                CategoryName = item.Category.Name,
                CategorySlug = item.Category.Slug,
                BrandName = item.Brand != null ? item.Brand.Name : null,
                Images = item.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => new ProductMediaViewModel
                    {
                        Url = image.ImageUrl,
                        IsMain = image.IsMain
                    })
                    .ToList(),
                Variants = item.Variants
                    .Where(variant => variant.IsActive && variant.Price > 0)
                    .OrderBy(variant => variant.Id)
                    .Select(variant => new
                    {
                        variant.Id,
                        variant.SKU,
                        variant.Color,
                        variant.Size,
                        variant.ImageUrl,
                        variant.Price,
                        variant.StockQuantity,
                        EffectivePrice = variant.CampaignItems
                            .Where(campaignItem =>
                                campaignItem.Campaign.IsActive &&
                                campaignItem.Campaign.StartDate <= utcNow &&
                                campaignItem.Campaign.EndDate > utcNow &&
                                campaignItem.NewPrice > 0 &&
                                campaignItem.NewPrice < variant.Price)
                            .Select(campaignItem => (decimal?)campaignItem.NewPrice)
                            .Min()
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var model = new ProductDetailsViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            Description = product.Description,
            MetaTitle = product.MetaTitle,
            MetaDescription = product.MetaDescription,
            CategoryName = product.CategoryName,
            CategorySlug = product.CategorySlug,
            BrandName = product.BrandName,
            Images = product.Images.Count == 0
                ? [new ProductMediaViewModel { Url = "/images/no-image.png", IsMain = true }]
                : product.Images,
            Variants = product.Variants.Select(variant =>
            {
                var effectivePrice = variant.EffectivePrice ?? variant.Price;
                return new ProductVariantDetailsViewModel
                {
                    Id = variant.Id,
                    Sku = variant.SKU,
                    Color = variant.Color,
                    Size = variant.Size,
                    ImageUrl = variant.ImageUrl,
                    OriginalPrice = variant.Price,
                    EffectivePrice = effectivePrice,
                    IsOnSale = effectivePrice < variant.Price,
                    StockQuantity = variant.StockQuantity
                };
            }).ToList()
        };

        return View(model);
    }

    [HttpGet("/Product/Details/{id:int}")]
    public async Task<IActionResult> LegacyDetails(int id, CancellationToken cancellationToken)
    {
        var slug = await _context.Products
            .AsNoTracking()
            .Where(product => product.Id == id && product.IsActive)
            .Select(product => product.Slug)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(slug)
            ? NotFound()
            : RedirectToActionPermanent(nameof(Details), new { slug });
    }
}
