using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Catalog;

public sealed class ProductPublicationService
    : IProductPublicationService
{
    private const int MaximumBatchSize = 100;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ProductPublicationService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<ProductPublicationReview?> ReviewAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await LoadProductAsync(
            productId,
            tracking: false,
            cancellationToken);

        return product is null
            ? null
            : BuildReview(product);
    }

    public async Task<ProductPublicationChangeResult> SetVisibilityAsync(
        int productId,
        bool publish,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var product = await LoadProductAsync(
                productId,
                tracking: true,
                cancellationToken);

            if (product is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                return new ProductPublicationChangeResult(
                    false,
                    productId,
                    "Sản phẩm không tồn tại",
                    false,
                    ["Không tìm thấy sản phẩm."]);
            }

            var review = BuildReview(product);

            if (publish && !review.CanPublish)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                return new ProductPublicationChangeResult(
                    false,
                    product.Id,
                    product.Name,
                    product.IsActive,
                    review.Issues);
            }

            product.IsActive = publish;
            product.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ProductPublicationChangeResult(
                true,
                product.Id,
                product.Name,
                publish,
                []);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ProductPublicationBatchResult> SetVisibilityBatchAsync(
        IReadOnlyCollection<int> productIds,
        bool publish,
        CancellationToken cancellationToken)
    {
        var ids = productIds
            .Where(id => id > 0)
            .Distinct()
            .Take(MaximumBatchSize)
            .ToArray();

        if (ids.Length == 0)
        {
            return new ProductPublicationBatchResult(
                0,
                0,
                0,
                []);
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var products = await BuildProductQuery(tracking: true)
                .Where(product => ids.Contains(product.Id))
                .OrderBy(product => product.Name)
                .ToArrayAsync(cancellationToken);

            var productById = products.ToDictionary(product => product.Id);
            var results = new List<ProductPublicationBatchItemResult>(
                ids.Length);

            var changedCount = 0;
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            foreach (var id in ids)
            {
                if (!productById.TryGetValue(id, out var product))
                {
                    results.Add(new ProductPublicationBatchItemResult(
                        id,
                        "Sản phẩm không tồn tại",
                        false,
                        ["Không tìm thấy sản phẩm."]));
                    continue;
                }

                var review = BuildReview(product);

                if (publish && !review.CanPublish)
                {
                    results.Add(new ProductPublicationBatchItemResult(
                        product.Id,
                        product.Name,
                        false,
                        review.Issues));
                    continue;
                }

                if (product.IsActive != publish)
                {
                    product.IsActive = publish;
                    product.UpdatedAt = now;
                    changedCount++;
                }

                results.Add(new ProductPublicationBatchItemResult(
                    product.Id,
                    product.Name,
                    true,
                    []));
            }

            if (changedCount > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return new ProductPublicationBatchResult(
                ids.Length,
                changedCount,
                results.Count(item => !item.Success),
                results);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Product?> LoadProductAsync(
        int productId,
        bool tracking,
        CancellationToken cancellationToken) =>
        await BuildProductQuery(tracking)
            .SingleOrDefaultAsync(
                product => product.Id == productId,
                cancellationToken);

    private IQueryable<Product> BuildProductQuery(bool tracking)
    {
        IQueryable<Product> query = _context.Products
            .AsSplitQuery()
            .Include(product => product.Images)
            .Include(product => product.Variants)
                .ThenInclude(item => item.OptionSelections)
                    .ThenInclude(selection => selection.OptionValue)
            .Include(product => product.OptionGroups)
                .ThenInclude(group => group.Values)
            .Include(product => product.Category)
                .ThenInclude(category =>
                    category.ProductAttributeAssignments)
                    .ThenInclude(assignment =>
                        assignment.AttributeDefinition)
            .Include(product => product.AttributeValues);

        return tracking
            ? query
            : query.AsNoTracking();
    }

    private static ProductPublicationReview BuildReview(Product product)
    {
        var issues = new List<string>();

        if (!product.Images.Any(image =>
                image.IsMain
                && !string.IsNullOrWhiteSpace(image.ImageUrl)))
        {
            issues.Add("Chưa có ảnh đại diện.");
        }

        if (string.IsNullOrWhiteSpace(product.MetaTitle))
        {
            issues.Add("Chưa có tiêu đề tìm kiếm.");
        }

        if (string.IsNullOrWhiteSpace(product.MetaDescription))
        {
            issues.Add("Chưa có mô tả tìm kiếm.");
        }

        var activeItems = product.Variants
            .Where(item => item.IsActive)
            .ToArray();

        if (activeItems.Length == 0)
        {
            issues.Add("Chưa có mã hàng đang bán.");
        }
        else if (!activeItems.Any(item =>
                     item.StockQuantity > 0
                     && item.CurrentPrice > 0))
        {
            issues.Add(
                "Không có mã hàng đang bán với giá và số lượng hợp lệ.");
        }

        var requiredGroups = product.OptionGroups
            .Where(group =>
                group.IsActive
                && group.IsRequired)
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.Id)
            .ToArray();

        foreach (var group in requiredGroups)
        {
            var activeValueIds = group.Values
                .Where(value => value.IsActive)
                .Select(value => value.Id)
                .ToHashSet();

            if (activeValueIds.Count == 0)
            {
                issues.Add(
                    $"Nhóm lựa chọn “{group.Name}” chưa có giá trị đang sử dụng.");
                continue;
            }

            var incompleteCount = activeItems.Count(item =>
                !item.OptionSelections.Any(selection =>
                    selection.OptionGroupId == group.Id
                    && activeValueIds.Contains(
                        selection.OptionValueId)));

            if (incompleteCount > 0)
            {
                issues.Add(
                    $"{incompleteCount} mã hàng chưa chọn “{group.Name}”.");
            }
        }

        var existingInformationIds = product.AttributeValues
            .Select(value => value.AttributeDefinitionId)
            .ToHashSet();

        var missingInformationNames =
            product.Category.ProductAttributeAssignments
                .Where(assignment =>
                    assignment.IsRequired
                    && assignment.AttributeDefinition.IsActive
                    && !existingInformationIds.Contains(
                        assignment.AttributeDefinitionId))
                .OrderBy(assignment => assignment.DisplayOrder)
                .ThenBy(assignment =>
                    assignment.AttributeDefinition.Name)
                .Select(assignment =>
                    assignment.AttributeDefinition.Name)
                .ToArray();

        if (missingInformationNames.Length > 0)
        {
            var visibleNames = string.Join(
                ", ",
                missingInformationNames.Take(4));

            issues.Add(
                missingInformationNames.Length <= 4
                    ? $"Thiếu thông tin bắt buộc: {visibleNames}."
                    : $"Thiếu thông tin bắt buộc: {visibleNames} "
                      + $"và {missingInformationNames.Length - 4} mục khác.");
        }

        return new ProductPublicationReview(
            product.Id,
            product.Name,
            product.IsActive,
            issues.Count == 0,
            issues);
    }
}
