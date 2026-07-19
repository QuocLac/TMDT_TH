using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Services.Catalog;
using WebApplication2.Tests.Support;

namespace WebApplication2.Tests.Catalog;

public sealed class ProductPublicationServiceTests
{
    [Fact]
    public async Task Publish_rejects_product_missing_required_sales_data()
    {
        await using var context = TestDbContextFactory.Create();

        var category = CatalogTestData.CreateCategory("invalid");
        var product = new Product
        {
            Name = "Sản phẩm chưa hoàn thiện",
            Slug = "san-pham-chua-hoan-thien",
            Category = category,
            IsActive = false
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var service = new ProductPublicationService(
            context,
            TimeProvider.System);

        var result = await service.SetVisibilityAsync(
            product.Id,
            publish: true,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPublished);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                "ảnh đại diện",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                "mã hàng",
                StringComparison.OrdinalIgnoreCase));

        context.ChangeTracker.Clear();

        var stored = await context.Products
            .SingleAsync(item => item.Id == product.Id);

        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Publish_allows_product_that_is_ready_for_sale()
    {
        await using var context = TestDbContextFactory.Create();

        var category = CatalogTestData.CreateCategory("ready");
        var product = CatalogTestData.CreateValidProduct(
            category,
            "ready",
            isActive: false);

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var service = new ProductPublicationService(
            context,
            TimeProvider.System);

        var result = await service.SetVisibilityAsync(
            product.Id,
            publish: true,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.IsPublished);
        Assert.Empty(result.Issues);

        context.ChangeTracker.Clear();

        var stored = await context.Products
            .SingleAsync(item => item.Id == product.Id);

        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Reconciliation_hides_invalid_published_product_only()
    {
        await using var context = TestDbContextFactory.Create();

        var validCategory = CatalogTestData.CreateCategory("valid-reconcile");
        var validProduct = CatalogTestData.CreateValidProduct(
            validCategory,
            "valid-reconcile",
            isActive: true);

        var invalidCategory = CatalogTestData.CreateCategory("invalid-reconcile");
        var invalidProduct = new Product
        {
            Name = "Sản phẩm cần tự động ẩn",
            Slug = "san-pham-can-tu-dong-an",
            Category = invalidCategory,
            IsActive = true,
            MetaTitle = "Sản phẩm cần tự động ẩn",
            MetaDescription = "Không có ảnh và mã hàng hợp lệ"
        };

        context.Products.AddRange(validProduct, invalidProduct);
        await context.SaveChangesAsync();

        var service = new ProductPublicationService(
            context,
            TimeProvider.System);

        var result = await service.ReconcilePublishedAsync(
            maximumProducts: 1,
            CancellationToken.None);

        Assert.Equal(2, result.ScannedCount);
        Assert.Equal(1, result.HiddenCount);
        Assert.Equal(2, result.BatchCount);
        Assert.Contains(
            result.HiddenItems,
            item => item.ProductId == invalidProduct.Id);

        context.ChangeTracker.Clear();

        var storedProducts = await context.Products
            .OrderBy(item => item.Id)
            .ToDictionaryAsync(item => item.Id);

        Assert.True(storedProducts[validProduct.Id].IsActive);
        Assert.False(storedProducts[invalidProduct.Id].IsActive);
    }
}
