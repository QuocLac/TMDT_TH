namespace WebApplication2.ViewModels.Storefront.Home;

public sealed class HomePageViewModel
{
    public string Query { get; init; } = string.Empty;

    public int? CategoryId { get; init; }

    public IReadOnlyList<CategoryCardViewModel> Categories { get; init; } = [];

    public IReadOnlyList<ProductCardViewModel> FlashSaleProducts { get; init; } = [];

    public IReadOnlyList<ProductCardViewModel> SuggestedProducts { get; init; } = [];

    public DateTimeOffset? FlashSaleEndsAt { get; init; }
}

public sealed class CategoryCardViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;
}

public sealed class ProductCardViewModel
{
    public int Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public decimal OriginalPrice { get; init; }

    public decimal EffectivePrice { get; init; }

    public int DiscountPercentage { get; init; }

    public bool IsOnSale { get; init; }

    public int StockQuantity { get; init; }

    public int VariantCount { get; init; }

    public int AvailableVariantCount { get; init; }

    public bool HasAvailableStock => AvailableVariantCount > 0;

    public bool ShowFromPrice => VariantCount > 1;

    public DateTimeOffset? SaleEndsAt { get; init; }
}
