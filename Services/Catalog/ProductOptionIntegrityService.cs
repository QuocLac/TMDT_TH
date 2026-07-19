using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Catalog;

public sealed class ProductOptionIntegrityService
    : IProductOptionIntegrityService
{
    private readonly ApplicationDbContext _context;

    public ProductOptionIntegrityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProductOptionIntegrityResult> RebuildProductKeysAsync(
        int productId,
        CancellationToken cancellationToken)
    {
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
                group.IsRequired
            })
            .ToArrayAsync(cancellationToken);

        var items = await _context.ProductVariants
            .Where(item => item.ProductId == productId)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);

        if (items.Length == 0)
        {
            return new ProductOptionIntegrityResult(0, 0, 0);
        }

        if (groups.Length == 0)
        {
            foreach (var item in items)
            {
                item.OptionCombinationKey = null;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return new ProductOptionIntegrityResult(
                items.Length,
                0,
                items.Length);
        }

        var itemIds = items
            .Select(item => item.Id)
            .ToArray();

        var rows = await _context.Set<ProductVariantOptionSelection>()
            .AsNoTracking()
            .Where(selection =>
                itemIds.Contains(selection.VariantId)
                && selection.OptionGroup.IsActive
                && selection.OptionValue.IsActive)
            .Select(selection => new
            {
                selection.VariantId,
                selection.OptionGroupId,
                selection.OptionValueId
            })
            .ToArrayAsync(cancellationToken);

        var selectionsByItemId = rows
            .GroupBy(row => row.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(
                    row => row.OptionGroupId,
                    row => row.OptionValueId));

        var keyOwners = new Dictionary<string, string>(
            StringComparer.Ordinal);
        var completeCount = 0;

        foreach (var item in items)
        {
            selectionsByItemId.TryGetValue(
                item.Id,
                out var selections);

            selections ??= new Dictionary<int, int>();

            var isComplete = groups
                .Where(group => group.IsRequired)
                .All(group => selections.ContainsKey(group.Id));

            if (!isComplete)
            {
                item.OptionCombinationKey = null;
                continue;
            }

            var signature = string.Join(
                "|",
                groups.Select(group =>
                    $"{group.Id}:{(selections.TryGetValue(group.Id, out var valueId) ? valueId : 0)}"));

            var key = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(signature)));

            if (keyOwners.TryGetValue(key, out var existingSku))
            {
                throw new ProductOptionCombinationConflictException(
                    existingSku,
                    item.SKU);
            }

            keyOwners[key] = item.SKU;
            item.OptionCombinationKey = key;
            completeCount++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new ProductOptionIntegrityResult(
            items.Length,
            completeCount,
            items.Length - completeCount);
    }
}
