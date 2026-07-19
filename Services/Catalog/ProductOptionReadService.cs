using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Catalog;

public sealed class ProductOptionReadService : IProductOptionReadService
{
    private const int MaximumSnapshotLength = 255;
    private const string NoSelectionLabel = "Không áp dụng";

    private readonly ApplicationDbContext _context;

    public ProductOptionReadService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<int, string>> GetVariantLabelsAsync(
        IEnumerable<int> variantIds,
        CancellationToken cancellationToken)
    {
        var ids = variantIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<int, string>();
        }

        var rows = await _context.Set<ProductVariantOptionSelection>()
            .AsNoTracking()
            .Where(selection =>
                ids.Contains(selection.VariantId)
                && selection.OptionGroup.IsActive
                && selection.OptionValue.IsActive)
            .Select(selection => new
            {
                selection.VariantId,
                GroupOrder = selection.OptionGroup.DisplayOrder,
                selection.OptionGroupId,
                ValueOrder = selection.OptionValue.DisplayOrder,
                selection.OptionValue.Label
            })
            .OrderBy(selection => selection.VariantId)
            .ThenBy(selection => selection.GroupOrder)
            .ThenBy(selection => selection.OptionGroupId)
            .ThenBy(selection => selection.ValueOrder)
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(row => row.VariantId)
            .ToDictionary(
                group => group.Key,
                group => LimitSnapshot(
                    string.Join(
                        " · ",
                        group.Select(row => row.Label)
                            .Where(label => !string.IsNullOrWhiteSpace(label)))));
    }

    public async Task<ProductOptionPickerSnapshot?> GetProductPickerAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.Id == productId && item.IsActive)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Slug,
                ImageUrl = item.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var groups = await _context.Set<ProductOptionGroup>()
            .AsNoTracking()
            .Where(group =>
                group.ProductId == productId
                && group.IsActive)
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.Id)
            .Select(group => new
            {
                group.Id,
                group.Code,
                group.Name,
                group.DisplayOrder,
                group.IsRequired
            })
            .ToArrayAsync(cancellationToken);

        // Chưa chuyển sang cấu trúc mới: để lớp giỏ hàng dùng luồng tương thích cũ.
        if (groups.Length == 0)
        {
            return null;
        }

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.ProductId == productId
                && variant.IsActive
                && variant.Price > 0
                && variant.CurrentPrice > 0)
            .OrderBy(variant => variant.Id)
            .Select(variant => new
            {
                variant.Id,
                variant.SKU,
                variant.ImageUrl,
                OriginalPrice = variant.Price,
                EffectivePrice = variant.CurrentPrice,
                variant.StockQuantity
            })
            .ToArrayAsync(cancellationToken);

        var variantIds = variants
            .Select(variant => variant.Id)
            .ToArray();

        var selectionRows = await _context.Set<ProductVariantOptionSelection>()
            .AsNoTracking()
            .Where(selection =>
                variantIds.Contains(selection.VariantId)
                && selection.OptionGroup.IsActive
                && selection.OptionValue.IsActive)
            .Select(selection => new
            {
                selection.VariantId,
                selection.OptionGroupId,
                GroupCode = selection.OptionGroup.Code,
                GroupOrder = selection.OptionGroup.DisplayOrder,
                selection.OptionValueId,
                ValueLabel = selection.OptionValue.Label,
                ValueOrder = selection.OptionValue.DisplayOrder
            })
            .OrderBy(selection => selection.VariantId)
            .ThenBy(selection => selection.GroupOrder)
            .ThenBy(selection => selection.OptionGroupId)
            .ThenBy(selection => selection.ValueOrder)
            .ToArrayAsync(cancellationToken);

        var requiredGroupIds = groups
            .Where(group => group.IsRequired)
            .Select(group => group.Id)
            .ToHashSet();

        var selectionsByVariantId = selectionRows
            .GroupBy(row => row.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray());

        // Chỉ đưa lên cửa hàng các mã hàng đã có đủ lựa chọn.
        var completeVariants = variants
            .Where(variant =>
                selectionsByVariantId.TryGetValue(variant.Id, out var rows)
                && requiredGroupIds.All(groupId =>
                    rows.Any(row => row.OptionGroupId == groupId)))
            .ToArray();

        var completeVariantIds = completeVariants
            .Select(variant => variant.Id)
            .ToHashSet();

        var visibleRows = selectionRows
            .Where(row => completeVariantIds.Contains(row.VariantId))
            .ToArray();

        var groupSnapshots = groups
            .Select(group =>
            {
                var values = visibleRows
                    .Where(row => row.OptionGroupId == group.Id)
                    .OrderBy(row => row.ValueOrder)
                    .ThenBy(row => row.OptionValueId)
                    .Select(row => row.ValueLabel)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (!group.IsRequired
                    && completeVariants.Any(variant =>
                        !selectionsByVariantId.TryGetValue(variant.Id, out var rows)
                        || rows.All(row => row.OptionGroupId != group.Id)))
                {
                    values.Add(NoSelectionLabel);
                }

                return new ProductOptionGroupSnapshot(
                    group.Code,
                    group.Name,
                    values);
            })
            .ToArray();

        var itemSnapshots = completeVariants
            .Select(variant =>
            {
                selectionsByVariantId.TryGetValue(
                    variant.Id,
                    out var rows);

                rows ??= [];

                var selections = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var group in groups)
                {
                    var row = rows.FirstOrDefault(item =>
                        item.OptionGroupId == group.Id);

                    if (row is not null)
                    {
                        selections[group.Code] = row.ValueLabel;
                    }
                    else if (!group.IsRequired)
                    {
                        selections[group.Code] = NoSelectionLabel;
                    }
                }

                return new ProductOptionItemSnapshot(
                    variant.Id,
                    variant.SKU,
                    string.IsNullOrWhiteSpace(variant.ImageUrl)
                        ? product.ImageUrl ?? "/images/no-image.png"
                        : variant.ImageUrl,
                    variant.OriginalPrice,
                    variant.EffectivePrice,
                    Math.Max(0, variant.StockQuantity),
                    selections);
            })
            .ToArray();

        return new ProductOptionPickerSnapshot(
            product.Id,
            product.Name,
            product.Slug,
            product.ImageUrl ?? "/images/no-image.png",
            itemSnapshots.Length == 0
                ? 0m
                : itemSnapshots.Min(item => item.EffectivePrice),
            itemSnapshots.Count(item => item.StockQuantity > 0),
            groupSnapshots,
            itemSnapshots);
    }

    private static string LimitSnapshot(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= MaximumSnapshotLength
            ? normalized
            : normalized[..MaximumSnapshotLength];
    }
}
