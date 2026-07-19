using WebApplication2.Models;

namespace WebApplication2.Tests.Support;

internal static class CatalogTestData
{
    public static Category CreateCategory(string suffix) =>
        new()
        {
            Name = $"Danh mục {suffix}",
            Slug = $"danh-muc-{suffix}",
            IconKey = "folder",
            IsVisible = true,
            MetaTitle = $"Danh mục {suffix}",
            MetaDescription = $"Mô tả danh mục {suffix}"
        };

    public static Product CreateValidProduct(
        Category category,
        string suffix,
        bool isActive = false) =>
        new()
        {
            Name = $"Sản phẩm {suffix}",
            Slug = $"san-pham-{suffix}",
            Category = category,
            IsActive = isActive,
            MetaTitle = $"Sản phẩm {suffix}",
            MetaDescription = $"Mô tả tìm kiếm {suffix}",
            Images =
            [
                new ProductImage
                {
                    ImageUrl = $"/uploads/products/{suffix}.jpg",
                    IsMain = true
                }
            ],
            Variants =
            [
                new ProductVariant
                {
                    SKU = $"SKU-{suffix.ToUpperInvariant()}",
                    Price = 250_000m,
                    CurrentPrice = 250_000m,
                    StockQuantity = 10,
                    IsActive = true
                }
            ]
        };
}
