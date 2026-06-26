namespace WebApplication2.ViewModels.Storefront.Products;

public sealed class ProductDetailsViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public string CategorySlug { get; init; } = string.Empty;

    public string? BrandName { get; init; }

    public string? MetaTitle { get; init; }

    public string? MetaDescription { get; init; }

    public IReadOnlyList<ProductMediaViewModel> Images { get; init; } = [];

    public IReadOnlyList<ProductVariantDetailsViewModel> Variants { get; init; } = [];
}

public sealed class ProductMediaViewModel
{
    public string Url { get; init; } = "/images/no-image.png";

    public bool IsMain { get; init; }
}

public sealed class ProductVariantDetailsViewModel
{
    public int Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string? Color { get; init; }

    public string? Size { get; init; }

    public string? ImageUrl { get; init; }

    public decimal OriginalPrice { get; init; }

    public decimal EffectivePrice { get; init; }

    public bool IsOnSale { get; init; }

    public int StockQuantity { get; init; }
}
