using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Services.Catalog;
using WebApplication2.Tests.Support;

namespace WebApplication2.Tests.Catalog;

public sealed class ProductOptionCombinationGeneratorTests
{
    [Fact]
    public async Task Generator_creates_only_missing_required_combinations()
    {
        await using var context = TestDbContextFactory.Create();

        var category = CatalogTestData.CreateCategory("combination");
        var product = new Product
        {
            Name = "Áo thử nghiệm tổ hợp",
            Slug = "ao-thu-nghiem-to-hop",
            Category = category,
            IsActive = false,
            OptionGroups =
            [
                new ProductOptionGroup
                {
                    Code = "mau_sac",
                    Name = "Màu sắc",
                    DisplayOrder = 10,
                    IsRequired = true,
                    IsActive = true,
                    Values =
                    [
                        new ProductOptionValue
                        {
                            Code = "den",
                            Label = "Đen",
                            DisplayOrder = 10,
                            IsActive = true
                        },
                        new ProductOptionValue
                        {
                            Code = "trang",
                            Label = "Trắng",
                            DisplayOrder = 20,
                            IsActive = true
                        }
                    ]
                }
            ]
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var integrity = new ProductOptionIntegrityService(context);
        var generator = new ProductOptionCombinationGenerator(
            context,
            integrity,
            TimeProvider.System);

        var preview = await generator.PreviewAsync(
            product.Id,
            CancellationToken.None);

        Assert.Equal(2, preview.TotalCombinationCount);
        Assert.Equal(2, preview.MissingCombinationCount);
        Assert.False(preview.IsOverLimit);

        var result = await generator.GenerateMissingAsync(
            new ProductOptionCombinationGenerationCommand(
                product.Id,
                ListPrice: 199_000m,
                StockQuantity: 5,
                ActivateNewItems: true),
            CancellationToken.None);

        Assert.Equal(2, result.CreatedItemCount);
        Assert.Equal(2, result.CompleteItemCount);
        Assert.Equal(0, result.IncompleteItemCount);

        var items = await context.ProductVariants
            .Include(item => item.OptionSelections)
            .Where(item => item.ProductId == product.Id)
            .OrderBy(item => item.Id)
            .ToArrayAsync();

        Assert.Equal(2, items.Length);
        Assert.All(items, item =>
        {
            Assert.True(item.IsActive);
            Assert.Equal(199_000m, item.Price);
            Assert.Equal(199_000m, item.CurrentPrice);
            Assert.Equal(5, item.StockQuantity);
            Assert.Single(item.OptionSelections);
            Assert.NotNull(item.OptionCombinationKey);
            Assert.Equal(64, item.OptionCombinationKey!.Length);
        });

        var secondRun = await generator.GenerateMissingAsync(
            new ProductOptionCombinationGenerationCommand(
                product.Id,
                ListPrice: 250_000m,
                StockQuantity: 8,
                ActivateNewItems: true),
            CancellationToken.None);

        Assert.Equal(0, secondRun.CreatedItemCount);
        Assert.Equal(2, await context.ProductVariants.CountAsync());
    }

    [Fact]
    public async Task Optional_group_includes_no_selection_as_a_valid_choice()
    {
        await using var context = TestDbContextFactory.Create();

        var category = CatalogTestData.CreateCategory("optional");
        var product = new Product
        {
            Name = "Sản phẩm có nhóm không bắt buộc",
            Slug = "san-pham-co-nhom-khong-bat-buoc",
            Category = category,
            IsActive = false,
            OptionGroups =
            [
                new ProductOptionGroup
                {
                    Code = "mau_sac",
                    Name = "Màu sắc",
                    DisplayOrder = 10,
                    IsRequired = true,
                    IsActive = true,
                    Values =
                    [
                        new ProductOptionValue
                        {
                            Code = "den",
                            Label = "Đen",
                            DisplayOrder = 10,
                            IsActive = true
                        },
                        new ProductOptionValue
                        {
                            Code = "trang",
                            Label = "Trắng",
                            DisplayOrder = 20,
                            IsActive = true
                        }
                    ]
                },
                new ProductOptionGroup
                {
                    Code = "goi_qua",
                    Name = "Gói quà",
                    DisplayOrder = 20,
                    IsRequired = false,
                    IsActive = true,
                    Values =
                    [
                        new ProductOptionValue
                        {
                            Code = "hop_qua",
                            Label = "Hộp quà",
                            DisplayOrder = 10,
                            IsActive = true
                        }
                    ]
                }
            ]
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var integrity = new ProductOptionIntegrityService(context);
        var generator = new ProductOptionCombinationGenerator(
            context,
            integrity,
            TimeProvider.System);

        var preview = await generator.PreviewAsync(
            product.Id,
            CancellationToken.None);

        Assert.Equal(4, preview.TotalCombinationCount);

        var result = await generator.GenerateMissingAsync(
            new ProductOptionCombinationGenerationCommand(
                product.Id,
                ListPrice: 100_000m,
                StockQuantity: 0,
                ActivateNewItems: false),
            CancellationToken.None);

        Assert.Equal(4, result.CreatedItemCount);

        var selectionCounts = await context.ProductVariants
            .Where(item => item.ProductId == product.Id)
            .Select(item => item.OptionSelections.Count)
            .OrderBy(count => count)
            .ToArrayAsync();

        Assert.Equal([1, 1, 2, 2], selectionCounts);
    }
}
