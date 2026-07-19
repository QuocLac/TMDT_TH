using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Catalog;

public sealed class ProductOptionCombinationGenerator
    : IProductOptionCombinationGenerator
{
    public const int MaximumCombinationCount = 300;

    private readonly ApplicationDbContext _context;
    private readonly IProductOptionIntegrityService _integrity;
    private readonly TimeProvider _timeProvider;

    public ProductOptionCombinationGenerator(
        ApplicationDbContext context,
        IProductOptionIntegrityService integrity,
        TimeProvider timeProvider)
    {
        _context = context;
        _integrity = integrity;
        _timeProvider = timeProvider;
    }

    public async Task<ProductOptionCombinationPreview> PreviewAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(
            productId,
            tracking: false,
            cancellationToken);

        if (state is null)
        {
            throw new ProductOptionGenerationException(
                "Không tìm thấy sản phẩm.");
        }

        var analysis = Analyze(state, throwOnDuplicate: false);

        return new ProductOptionCombinationPreview(
            analysis.Groups.Count,
            analysis.TotalCombinationCount,
            analysis.ExistingSignatures.Count,
            Math.Max(
                0,
                analysis.TotalCombinationCount
                - analysis.ExistingSignatures.Count),
            state.Items
                .Where(item => item.Price > 0)
                .Select(item => item.Price)
                .DefaultIfEmpty(0m)
                .Min(),
            analysis.RequiredGroupsWithoutValues,
            analysis.HasExistingConflict,
            analysis.TotalCombinationCount > MaximumCombinationCount,
            MaximumCombinationCount);
    }

    public async Task<ProductOptionCombinationGenerationResult> GenerateMissingAsync(
        ProductOptionCombinationGenerationCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ProductId <= 0)
        {
            throw new ProductOptionGenerationException(
                "Sản phẩm không hợp lệ.");
        }

        if (command.ListPrice <= 0)
        {
            throw new ProductOptionGenerationException(
                "Giá niêm yết phải lớn hơn 0.");
        }

        if (command.StockQuantity < 0)
        {
            throw new ProductOptionGenerationException(
                "Số lượng có thể bán không được âm.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var state = await LoadStateAsync(
                command.ProductId,
                tracking: true,
                cancellationToken)
                ?? throw new ProductOptionGenerationException(
                    "Không tìm thấy sản phẩm.");

            var analysis = Analyze(state, throwOnDuplicate: true);

            if (analysis.Groups.Count == 0)
            {
                throw new ProductOptionGenerationException(
                    "Sản phẩm chưa có nhóm lựa chọn mua đang hoạt động.");
            }

            if (analysis.RequiredGroupsWithoutValues.Count > 0)
            {
                throw new ProductOptionGenerationException(
                    "Các nhóm bắt buộc chưa có giá trị: "
                    + string.Join(
                        ", ",
                        analysis.RequiredGroupsWithoutValues)
                    + ".");
            }

            if (analysis.TotalCombinationCount > MaximumCombinationCount)
            {
                throw new ProductOptionGenerationException(
                    $"Tổng số tổ hợp là {analysis.TotalCombinationCount:N0}, "
                    + $"vượt giới hạn {MaximumCombinationCount:N0}. "
                    + "Hãy giảm số giá trị trong các nhóm lựa chọn.");
            }

            var combinations = BuildCombinations(analysis.Groups);
            var missing = combinations
                .Where(combination =>
                    !analysis.ExistingSignatures.Contains(
                        BuildSignature(
                            analysis.Groups,
                            combination)))
                .ToArray();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var created = new List<ProductVariant>(missing.Length);

            foreach (var combination in missing)
            {
                var item = new ProductVariant
                {
                    ProductId = command.ProductId,
                    SKU = VariantSkuGenerator.CreateTemporarySku(),
                    Price = command.ListPrice,
                    CurrentPrice = command.ListPrice,
                    CurrentPriceSourceType =
                        EffectivePriceSourceType.ListPrice,
                    CurrentPriceSourceId = null,
                    CurrentPriceEffectiveFrom = null,
                    CurrentPriceEffectiveTo = null,
                    StockQuantity = command.StockQuantity,
                    IsActive = command.ActivateNewItems,
                    Color = null,
                    Size = null,
                    CreatedAt = now,
                    UpdatedAt = now,
                    OptionSelections = combination
                        .Where(selection => selection.Value.HasValue)
                        .Select(selection =>
                            new ProductVariantOptionSelection
                            {
                                OptionGroupId = selection.Key,
                                OptionValueId = selection.Value.Value
                            })
                        .ToList()
                };

                _context.ProductVariants.Add(item);
                created.Add(item);
            }

            if (created.Count > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);

                foreach (var item in created)
                {
                    item.SKU = VariantSkuGenerator.CreateSku(
                        item.ProductId,
                        item.Id);
                }

                await _context.SaveChangesAsync(cancellationToken);
            }

            var integrity = await _integrity.RebuildProductKeysAsync(
                command.ProductId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ProductOptionCombinationGenerationResult(
                analysis.TotalCombinationCount,
                analysis.ExistingSignatures.Count,
                created.Count,
                integrity.CompleteItemCount,
                integrity.IncompleteItemCount);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ProductOptionState?> LoadStateAsync(
        int productId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> query = _context.Products
            .AsSplitQuery()
            .Include(product => product.OptionGroups)
                .ThenInclude(group => group.Values)
            .Include(product => product.Variants)
                .ThenInclude(item => item.OptionSelections);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        var product = await query.SingleOrDefaultAsync(
            item => item.Id == productId,
            cancellationToken);

        return product is null
            ? null
            : new ProductOptionState(
                product,
                product.OptionGroups
                    .Where(group => group.IsActive)
                    .OrderBy(group => group.DisplayOrder)
                    .ThenBy(group => group.Id)
                    .ToArray(),
                product.Variants
                    .OrderBy(item => item.Id)
                    .ToArray());
    }

    private static ProductOptionAnalysis Analyze(
        ProductOptionState state,
        bool throwOnDuplicate)
    {
        var groups = state.Groups
            .Select(group => new ProductOptionChoiceGroup(
                group.Id,
                group.Name,
                group.IsRequired,
                group.Values
                    .Where(value => value.IsActive)
                    .OrderBy(value => value.DisplayOrder)
                    .ThenBy(value => value.Id)
                    .Select(value => value.Id)
                    .ToArray()))
            .ToArray();

        var requiredWithoutValues = groups
            .Where(group =>
                group.IsRequired
                && group.ValueIds.Count == 0)
            .Select(group => group.Name)
            .ToArray();

        var total = groups.Count == 0
            ? 0L
            : 1L;

        foreach (var group in groups)
        {
            var choiceCount = group.ValueIds.Count
                + (group.IsRequired ? 0 : 1);

            if (choiceCount == 0)
            {
                total = 0;
                break;
            }

            if (total > MaximumCombinationCount)
            {
                total = MaximumCombinationCount + 1L;
                break;
            }

            total *= choiceCount;

            if (total > MaximumCombinationCount)
            {
                total = MaximumCombinationCount + 1L;
                break;
            }
        }

        var activeValueIdsByGroup = groups.ToDictionary(
            group => group.Id,
            group => group.ValueIds.ToHashSet());

        var existingSignatures = new HashSet<string>(
            StringComparer.Ordinal);

        var ownerBySignature = new Dictionary<string, string>(
            StringComparer.Ordinal);
        var hasExistingConflict = false;

        foreach (var item in state.Items)
        {
            var selectedByGroup = item.OptionSelections
                .Where(selection =>
                    activeValueIdsByGroup.TryGetValue(
                        selection.OptionGroupId,
                        out var activeValueIds)
                    && activeValueIds.Contains(
                        selection.OptionValueId))
                .GroupBy(selection => selection.OptionGroupId)
                .ToDictionary(
                    group => group.Key,
                    group => (int?)group.First().OptionValueId);

            var hasAllRequired = groups
                .Where(group => group.IsRequired)
                .All(group =>
                    selectedByGroup.ContainsKey(group.Id));

            if (!hasAllRequired)
            {
                continue;
            }

            var signature = BuildSignature(
                groups,
                selectedByGroup);

            if (ownerBySignature.TryGetValue(
                    signature,
                    out var existingSku))
            {
                hasExistingConflict = true;

                if (throwOnDuplicate)
                {
                    throw new ProductOptionCombinationConflictException(
                        existingSku,
                        item.SKU);
                }

                continue;
            }

            ownerBySignature[signature] = item.SKU;
            existingSignatures.Add(signature);
        }

        return new ProductOptionAnalysis(
            groups,
            total,
            requiredWithoutValues,
            existingSignatures,
            hasExistingConflict);
    }

    private static IReadOnlyList<Dictionary<int, int?>>
        BuildCombinations(
            IReadOnlyList<ProductOptionChoiceGroup> groups)
    {
        var result = new List<Dictionary<int, int?>>();
        var current = new Dictionary<int, int?>();

        void Build(int index)
        {
            if (index >= groups.Count)
            {
                result.Add(new Dictionary<int, int?>(current));
                return;
            }

            var group = groups[index];

            if (!group.IsRequired)
            {
                current[group.Id] = null;
                Build(index + 1);
            }

            foreach (var valueId in group.ValueIds)
            {
                current[group.Id] = valueId;
                Build(index + 1);
            }

            current.Remove(group.Id);
        }

        Build(0);
        return result;
    }

    private static string BuildSignature(
        IReadOnlyList<ProductOptionChoiceGroup> groups,
        IReadOnlyDictionary<int, int?> selections) =>
        string.Join(
            "|",
            groups.Select(group =>
                $"{group.Id}:"
                + (selections.TryGetValue(
                        group.Id,
                        out var valueId)
                    && valueId.HasValue
                        ? valueId.Value
                        : 0)));

    private sealed record ProductOptionState(
        Product Product,
        IReadOnlyList<ProductOptionGroup> Groups,
        IReadOnlyList<ProductVariant> Items);

    private sealed record ProductOptionChoiceGroup(
        int Id,
        string Name,
        bool IsRequired,
        IReadOnlyList<int> ValueIds);

    private sealed record ProductOptionAnalysis(
        IReadOnlyList<ProductOptionChoiceGroup> Groups,
        long TotalCombinationCount,
        IReadOnlyList<string> RequiredGroupsWithoutValues,
        IReadOnlySet<string> ExistingSignatures,
        bool HasExistingConflict);
}
