using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.ProductOptions;
using WebApplication2.Models;
using WebApplication2.Services.Catalog;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/ProductOptions")]
public sealed class ProductOptionsController : Controller
{
    private const int PageSize = 20;
    private const int MaximumValuesPerGroup = 80;
    private const string ReservedNoSelectionLabel = "Không áp dụng";

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IProductOptionIntegrityService _integrity;
    private readonly ILogger<ProductOptionsController> _logger;

    public ProductOptionsController(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        IProductOptionIntegrityService integrity,
        ILogger<ProductOptionsController> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _integrity = integrity;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? query,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        var normalizedQuery = query?.Trim() ?? string.Empty;

        var productsQuery = _context.Products
            .AsNoTracking()
            .Where(product =>
                string.IsNullOrWhiteSpace(normalizedQuery)
                || product.Name.Contains(normalizedQuery)
                || product.Slug.Contains(normalizedQuery)
                || product.Variants.Any(item => item.SKU.Contains(normalizedQuery)));

        var totalItems = await productsQuery.CountAsync(cancellationToken);

        var rows = await productsQuery
            .OrderByDescending(product => product.UpdatedAt ?? product.CreatedAt)
            .ThenBy(product => product.Name)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(product => new ProductOptionIndexItemViewModel
            {
                ProductId = product.Id,
                ProductName = product.Name,
                CategoryName = product.Category.Name,
                BrandName = product.Brand != null ? product.Brand.Name : null,
                ImageUrl = product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault() ?? "/images/no-image.png",
                IsActive = product.IsActive,
                OptionGroupCount = product.OptionGroups.Count(group => group.IsActive),
                OptionValueCount = product.OptionGroups
                    .Where(group => group.IsActive)
                    .SelectMany(group => group.Values)
                    .Count(value => value.IsActive),
                SellableItemCount = product.Variants.Count,
                ConfiguredItemCount = product.Variants.Count(item =>
                    item.OptionSelections.Any(selection =>
                        selection.OptionGroup.IsActive
                        && selection.OptionValue.IsActive))
            })
            .ToArrayAsync(cancellationToken);

        return View(new ProductOptionIndexPageViewModel
        {
            Query = normalizedQuery,
            Page = page,
            TotalPages = (int)Math.Ceiling(totalItems / (double)PageSize),
            TotalItems = totalItems,
            Items = rows
        });
    }

    [HttpGet("{productId:int}")]
    public async Task<IActionResult> Configure(
        int productId,
        CancellationToken cancellationToken)
    {
        var model = await BuildConfigureModelAsync(productId, cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("group/save")]
    public async Task<IActionResult> SaveGroup(
        ProductOptionGroupInput input,
        CancellationToken cancellationToken)
    {
        NormalizeGroupInput(input);

        ModelState.Clear();
        TryValidateModel(input);

        var values = ParseValues(input.ValuesText);
        if (values.Count == 0)
        {
            ModelState.AddModelError(
                nameof(input.ValuesText),
                "Vui lòng nhập ít nhất một giá trị.");
        }
        else if (values.Count > MaximumValuesPerGroup)
        {
            ModelState.AddModelError(
                nameof(input.ValuesText),
                $"Mỗi nhóm được tối đa {MaximumValuesPerGroup} giá trị.");
        }
        else if (values.Any(value => string.Equals(
                     value,
                     ReservedNoSelectionLabel,
                     StringComparison.CurrentCultureIgnoreCase)))
        {
            ModelState.AddModelError(
                nameof(input.ValuesText),
                $"“{ReservedNoSelectionLabel}” là giá trị do hệ thống quản lý cho nhóm không bắt buộc.");
        }

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Configure), new { productId = input.ProductId });
        }

        if (!await _context.Products.AnyAsync(
                product => product.Id == input.ProductId,
                cancellationToken))
        {
            return NotFound();
        }

        var duplicateCode = await _context.Set<ProductOptionGroup>()
            .AnyAsync(
                group =>
                    group.ProductId == input.ProductId
                    && group.Id != input.Id
                    && group.Code == input.Code,
                cancellationToken);

        if (duplicateCode)
        {
            TempData["ErrorMessage"] =
                "Mã nhóm lựa chọn đã được dùng trong sản phẩm này.";
            return RedirectToAction(nameof(Configure), new { productId = input.ProductId });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            ProductOptionGroup group;

            if (input.Id == 0)
            {
                group = new ProductOptionGroup
                {
                    ProductId = input.ProductId
                };
                _context.Set<ProductOptionGroup>().Add(group);
            }
            else
            {
                group = await _context.Set<ProductOptionGroup>()
                    .Include(item => item.Values)
                    .SingleOrDefaultAsync(
                        item => item.Id == input.Id && item.ProductId == input.ProductId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Không tìm thấy nhóm lựa chọn cần cập nhật.");
            }

            group.Code = input.Code!;
            group.Name = input.Name;
            group.DisplayOrder = input.DisplayOrder;
            group.IsRequired = input.IsRequired;
            group.IsActive = input.IsActive;
            group.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _context.SaveChangesAsync(cancellationToken);

            await SynchronizeValuesAsync(group, values, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            var integrity = await _integrity.RebuildProductKeysAsync(
                input.ProductId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            TempData["SuccessMessage"] = input.Id == 0
                ? $"Đã tạo nhóm lựa chọn mua. {integrity.CompleteItemCount} mã hàng đã sẵn sàng."
                : $"Đã cập nhật nhóm lựa chọn mua. {integrity.IncompleteItemCount} mã hàng còn thiếu lựa chọn.";
        }
        catch (ProductOptionCombinationConflictException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Database rejected product option group {GroupId} for product {ProductId}.",
                input.Id,
                input.ProductId);
            TempData["ErrorMessage"] =
                "Không thể lưu nhóm lựa chọn. Hãy kiểm tra tên và các giá trị.";
        }

        return RedirectToAction(nameof(Configure), new { productId = input.ProductId });
    }

    [HttpPost("group/{id:int}/toggle")]
    public async Task<IActionResult> ToggleGroup(
        int id,
        int productId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var group = await _context.Set<ProductOptionGroup>()
                .SingleOrDefaultAsync(
                    item => item.Id == id && item.ProductId == productId,
                    cancellationToken);

            if (group is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return NotFound();
            }

            group.IsActive = !group.IsActive;
            group.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _context.SaveChangesAsync(cancellationToken);

            var integrity = await _integrity.RebuildProductKeysAsync(
                productId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            TempData["SuccessMessage"] = group.IsActive
                ? $"Đã sử dụng lại nhóm lựa chọn mua. {integrity.IncompleteItemCount} mã hàng cần kiểm tra."
                : $"Đã tạm ngừng nhóm lựa chọn mua. {integrity.CompleteItemCount} mã hàng đang sẵn sàng.";
        }
        catch (ProductOptionCombinationConflictException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Could not toggle product option group {GroupId} for product {ProductId}.",
                id,
                productId);
            TempData["ErrorMessage"] =
                "Không thể thay đổi trạng thái nhóm vì dữ liệu mã hàng chưa nhất quán.";
        }

        return RedirectToAction(nameof(Configure), new { productId });
    }

    [HttpPost("selection/save")]
    public async Task<IActionResult> SaveSelections(
        ProductVariantSelectionInput input,
        CancellationToken cancellationToken)
    {
        var item = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.Id == input.VariantId
                && variant.ProductId == input.ProductId)
            .Select(variant => new
            {
                variant.Id,
                variant.SKU
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        var groups = await _context.Set<ProductOptionGroup>()
            .AsNoTracking()
            .Where(group =>
                group.ProductId == input.ProductId
                && group.IsActive)
            .Include(group => group.Values)
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.Id)
            .ToArrayAsync(cancellationToken);

        if (groups.Length == 0)
        {
            TempData["ErrorMessage"] =
                "Sản phẩm chưa có nhóm lựa chọn mua đang hoạt động.";
            return RedirectToAction(nameof(Configure), new { productId = input.ProductId });
        }

        var desired = new Dictionary<int, int>();

        foreach (var group in groups)
        {
            input.Selections.TryGetValue(group.Id, out var selectedValueId);

            if (!selectedValueId.HasValue || selectedValueId.Value <= 0)
            {
                if (group.IsRequired)
                {
                    TempData["ErrorMessage"] =
                        $"Vui lòng chọn {group.Name} cho mã hàng {item.SKU}.";
                    return RedirectToAction(
                        nameof(Configure),
                        new { productId = input.ProductId });
                }

                continue;
            }

            var valueIsValid = group.Values.Any(value =>
                value.Id == selectedValueId.Value && value.IsActive);

            if (!valueIsValid)
            {
                TempData["ErrorMessage"] =
                    $"Giá trị của nhóm {group.Name} không còn hợp lệ.";
                return RedirectToAction(
                    nameof(Configure),
                    new { productId = input.ProductId });
            }

            desired[group.Id] = selectedValueId.Value;
        }

        var desiredSignature = BuildSignature(groups, desired);

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var otherRows = await _context.Set<ProductVariantOptionSelection>()
                .AsNoTracking()
                .Where(selection =>
                    selection.Variant.ProductId == input.ProductId
                    && selection.VariantId != input.VariantId
                    && selection.OptionGroup.IsActive
                    && selection.OptionValue.IsActive)
                .Select(selection => new
                {
                    selection.VariantId,
                    selection.OptionGroupId,
                    selection.OptionValueId,
                    selection.Variant.SKU
                })
                .ToArrayAsync(cancellationToken);

            foreach (var otherItem in otherRows.GroupBy(row => new
                     {
                         row.VariantId,
                         row.SKU
                     }))
            {
                var otherSelections = otherItem.ToDictionary(
                    row => row.OptionGroupId,
                    row => row.OptionValueId);

                if (BuildSignature(groups, otherSelections) == desiredSignature)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    TempData["ErrorMessage"] =
                        $"Tổ hợp lựa chọn này đã được dùng cho mã hàng {otherItem.Key.SKU}.";
                    return RedirectToAction(
                        nameof(Configure),
                        new { productId = input.ProductId });
                }
            }

            var activeGroupIds = groups
                .Select(group => group.Id)
                .ToArray();

            var current = await _context.Set<ProductVariantOptionSelection>()
                .Where(selection =>
                    selection.VariantId == input.VariantId
                    && activeGroupIds.Contains(selection.OptionGroupId))
                .ToArrayAsync(cancellationToken);

            _context.Set<ProductVariantOptionSelection>().RemoveRange(current);

            foreach (var selection in desired)
            {
                _context.Set<ProductVariantOptionSelection>().Add(
                    new ProductVariantOptionSelection
                    {
                        VariantId = input.VariantId,
                        OptionGroupId = selection.Key,
                        OptionValueId = selection.Value
                    });
            }

            await _context.SaveChangesAsync(cancellationToken);

            var integrity = await _integrity.RebuildProductKeysAsync(
                input.ProductId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            TempData["SuccessMessage"] =
                $"Đã cập nhật lựa chọn mua cho mã hàng {item.SKU}. "
                + $"{integrity.CompleteItemCount}/{integrity.TotalItemCount} mã hàng đã sẵn sàng.";
        }
        catch (ProductOptionCombinationConflictException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Could not save product option selections for item {VariantId}.",
                input.VariantId);
            TempData["ErrorMessage"] =
                "Không thể lưu tổ hợp lựa chọn. Dữ liệu vừa thay đổi hoặc tổ hợp đã bị trùng.";
        }

        return RedirectToAction(nameof(Configure), new { productId = input.ProductId });
    }

    [HttpPost("{productId:int}/import-existing")]
    public async Task<IActionResult> ImportExistingSelections(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(item => item.Variants)
            .Include(item => item.OptionGroups)
                .ThenInclude(group => group.Values)
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var hasColor = product.Variants.Any(item => !string.IsNullOrWhiteSpace(item.Color));
        var hasSize = product.Variants.Any(item => !string.IsNullOrWhiteSpace(item.Size));

        if (!hasColor && !hasSize)
        {
            TempData["ErrorMessage"] =
                "Sản phẩm không có dữ liệu màu sắc hoặc quy cách để chuyển đổi.";
            return RedirectToAction(nameof(Configure), new { productId });
        }

        var duplicateLegacyCombination = product.Variants
            .Select(item => new
            {
                item.SKU,
                Signature = string.Join(
                    "\u001f",
                    hasColor ? NormalizeDisplayValue(item.Color) : string.Empty,
                    hasSize ? NormalizeDisplayValue(item.Size) : string.Empty)
            })
            .GroupBy(item => item.Signature, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateLegacyCombination is not null)
        {
            TempData["ErrorMessage"] =
                "Không thể chuyển đổi tự động vì có nhiều mã hàng đang dùng cùng một tổ hợp lựa chọn.";
            return RedirectToAction(nameof(Configure), new { productId });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var groups = new List<(ProductOptionGroup Group, Func<ProductVariant, string?> Selector)>();

            if (hasColor)
            {
                var colorGroup = EnsureGroup(
                    product,
                    "mau_sac",
                    "Màu sắc",
                    10);
                groups.Add((colorGroup, item => item.Color));
            }

            if (hasSize)
            {
                var sizeGroup = EnsureGroup(
                    product,
                    "quy_cach",
                    "Kích cỡ / dung lượng / quy cách",
                    20);
                groups.Add((sizeGroup, item => item.Size));
            }

            await _context.SaveChangesAsync(cancellationToken);

            foreach (var pair in groups)
            {
                var labels = product.Variants
                    .Select(pair.Selector)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                await SynchronizeValuesAsync(pair.Group, labels, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            var groupIds = groups.Select(pair => pair.Group.Id).ToArray();
            var variantIds = product.Variants.Select(item => item.Id).ToArray();

            var existingSelections = await _context.Set<ProductVariantOptionSelection>()
                .Where(selection =>
                    variantIds.Contains(selection.VariantId)
                    && groupIds.Contains(selection.OptionGroupId))
                .ToArrayAsync(cancellationToken);

            _context.Set<ProductVariantOptionSelection>()
                .RemoveRange(existingSelections);

            foreach (var variant in product.Variants)
            {
                foreach (var pair in groups)
                {
                    var label = pair.Selector(variant)?.Trim();
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    var valueCode = NormalizeCode(label);
                    var optionValue = pair.Group.Values.Single(value =>
                        value.Code == valueCode);

                    _context.Set<ProductVariantOptionSelection>().Add(
                        new ProductVariantOptionSelection
                        {
                            VariantId = variant.Id,
                            OptionGroupId = pair.Group.Id,
                            OptionValueId = optionValue.Id
                        });
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            var integrity = await _integrity.RebuildProductKeysAsync(
                productId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            TempData["SuccessMessage"] =
                $"Đã chuyển dữ liệu lựa chọn hiện có. {integrity.CompleteItemCount} mã hàng đã sẵn sàng.";
        }
        catch (ProductOptionCombinationConflictException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Failed to import legacy product options for product {ProductId}.",
                productId);
            TempData["ErrorMessage"] =
                "Không thể chuyển đổi dữ liệu lựa chọn hiện có.";
        }

        return RedirectToAction(nameof(Configure), new { productId });
    }

    [HttpPost("{productId:int}/rebuild-integrity")]
    public async Task<IActionResult> RebuildIntegrity(
        int productId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var result = await _integrity.RebuildProductKeysAsync(
                productId,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            TempData["SuccessMessage"] =
                $"Đã kiểm tra {result.TotalItemCount} mã hàng. "
                + $"{result.CompleteItemCount} mã hàng sẵn sàng, "
                + $"{result.IncompleteItemCount} mã hàng cần bổ sung lựa chọn.";
        }
        catch (ProductOptionCombinationConflictException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning(
                exception,
                "Could not rebuild option integrity for product {ProductId}.",
                productId);
            TempData["ErrorMessage"] =
                "Không thể hoàn tất kiểm tra dữ liệu lựa chọn mua.";
        }

        return RedirectToAction(nameof(Configure), new { productId });
    }

    private async Task<ProductOptionConfigureViewModel?> BuildConfigureModelAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.IsActive,
                CategoryName = item.Category.Name,
                BrandName = item.Brand != null ? item.Brand.Name : null
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var groups = await _context.Set<ProductOptionGroup>()
            .AsNoTracking()
            .Where(group => group.ProductId == productId)
            .Include(group => group.Values)
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.Id)
            .ToArrayAsync(cancellationToken);

        var items = await _context.ProductVariants
            .AsNoTracking()
            .Where(item => item.ProductId == productId)
            .OrderBy(item => item.SKU)
            .Select(item => new
            {
                item.Id,
                item.SKU,
                item.Color,
                item.Size,
                item.CurrentPrice,
                item.StockQuantity,
                item.IsActive
            })
            .ToArrayAsync(cancellationToken);

        var itemIds = items.Select(item => item.Id).ToArray();

        var selections = await _context.Set<ProductVariantOptionSelection>()
            .AsNoTracking()
            .Where(selection => itemIds.Contains(selection.VariantId))
            .Select(selection => new
            {
                selection.VariantId,
                selection.OptionGroupId,
                selection.OptionValueId,
                GroupOrder = selection.OptionGroup.DisplayOrder,
                ValueOrder = selection.OptionValue.DisplayOrder,
                ValueLabel = selection.OptionValue.Label,
                GroupIsActive = selection.OptionGroup.IsActive,
                ValueIsActive = selection.OptionValue.IsActive
            })
            .ToArrayAsync(cancellationToken);

        var activeGroups = groups.Where(group => group.IsActive).ToArray();

        return new ProductOptionConfigureViewModel
        {
            ProductId = product.Id,
            ProductName = product.Name,
            CategoryName = product.CategoryName,
            BrandName = product.BrandName,
            ProductIsActive = product.IsActive,
            HasLegacySelectionData = items.Any(item =>
                !string.IsNullOrWhiteSpace(item.Color)
                || !string.IsNullOrWhiteSpace(item.Size)),
            Groups = groups.Select(group => new ProductOptionGroupItemViewModel
            {
                Id = group.Id,
                Code = group.Code,
                Name = group.Name,
                DisplayOrder = group.DisplayOrder,
                IsRequired = group.IsRequired,
                IsActive = group.IsActive,
                Values = group.Values
                    .OrderBy(value => value.DisplayOrder)
                    .ThenBy(value => value.Label)
                    .Select(value => new ProductOptionValueItemViewModel
                    {
                        Id = value.Id,
                        Code = value.Code,
                        Label = value.Label,
                        DisplayOrder = value.DisplayOrder,
                        IsActive = value.IsActive
                    })
                    .ToArray()
            }).ToArray(),
            Items = items.Select(item =>
            {
                var itemSelections = selections
                    .Where(selection => selection.VariantId == item.Id)
                    .ToArray();

                var activeSelectionMap = itemSelections
                    .Where(selection =>
                        selection.GroupIsActive
                        && selection.ValueIsActive)
                    .ToDictionary(
                        selection => selection.OptionGroupId,
                        selection => selection.OptionValueId);

                var isComplete = activeGroups
                    .Where(group => group.IsRequired)
                    .All(group => activeSelectionMap.ContainsKey(group.Id));

                var selectionLabel = string.Join(
                    " · ",
                    itemSelections
                        .Where(selection =>
                            selection.GroupIsActive
                            && selection.ValueIsActive)
                        .OrderBy(selection => selection.GroupOrder)
                        .ThenBy(selection => selection.ValueOrder)
                        .Select(selection => selection.ValueLabel));

                return new ProductOptionVariantItemViewModel
                {
                    VariantId = item.Id,
                    Sku = item.SKU,
                    LegacyLabel = BuildLegacyLabel(item.Color, item.Size),
                    CurrentPrice = item.CurrentPrice,
                    StockQuantity = item.StockQuantity,
                    IsActive = item.IsActive,
                    IsComplete = isComplete,
                    SelectionLabel = string.IsNullOrWhiteSpace(selectionLabel)
                        ? "Chưa cấu hình"
                        : selectionLabel,
                    SelectionByGroupId = activeSelectionMap
                };
            }).ToArray()
        };
    }

    private ProductOptionGroup EnsureGroup(
        Product product,
        string code,
        string name,
        int displayOrder)
    {
        var group = product.OptionGroups.SingleOrDefault(item => item.Code == code);

        if (group is not null)
        {
            group.Name = name;
            group.DisplayOrder = displayOrder;
            group.IsRequired = true;
            group.IsActive = true;
            group.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            return group;
        }

        group = new ProductOptionGroup
        {
            ProductId = product.Id,
            Code = code,
            Name = name,
            DisplayOrder = displayOrder,
            IsRequired = true,
            IsActive = true
        };

        product.OptionGroups.Add(group);
        return group;
    }

    private async Task SynchronizeValuesAsync(
        ProductOptionGroup group,
        IReadOnlyCollection<string> labels,
        CancellationToken cancellationToken)
    {
        if (group.Values.Count == 0 && group.Id > 0)
        {
            group.Values = await _context.Set<ProductOptionValue>()
                .Where(item => item.OptionGroupId == group.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var value in group.Values)
        {
            value.IsActive = false;
            value.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var rawLabel in labels)
        {
            var label = rawLabel.Trim();
            var code = NormalizeCode(label);

            if (string.IsNullOrWhiteSpace(code) || !usedCodes.Add(code))
            {
                throw new InvalidOperationException(
                    $"Giá trị “{label}” bị trùng sau khi chuẩn hóa.");
            }

            var value = group.Values.SingleOrDefault(item => item.Code == code);
            if (value is null)
            {
                value = new ProductOptionValue
                {
                    OptionGroupId = group.Id,
                    Code = code
                };
                group.Values.Add(value);
            }

            value.Label = label;
            value.DisplayOrder = (++index) * 10;
            value.IsActive = true;
            value.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }
    }

    private static IReadOnlyList<string> ParseValues(string value) =>
        value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildSignature(
        IEnumerable<ProductOptionGroup> groups,
        IReadOnlyDictionary<int, int> selections) =>
        string.Join(
            "\u001f",
            groups
                .OrderBy(group => group.DisplayOrder)
                .ThenBy(group => group.Id)
                .Select(group =>
                    $"{group.Id}:{(selections.TryGetValue(group.Id, out var valueId) ? valueId : 0)}"));

    private static string BuildLegacyLabel(
        string? color,
        string? size)
    {
        var values = new[] { color, size }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

        return values.Length == 0
            ? "Lựa chọn tiêu chuẩn"
            : string.Join(" · ", values);
    }

    private static string NormalizeDisplayValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();

    private static void NormalizeGroupInput(ProductOptionGroupInput input)
    {
        input.Name = input.Name.Trim();
        input.Code = NormalizeCode(
            string.IsNullOrWhiteSpace(input.Code)
                ? input.Name
                : input.Code);
        input.ValuesText = input.ValuesText.Trim();
    }

    private static string NormalizeCode(string value)
    {
        var source = value
            .Trim()
            .ToLowerInvariant()
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormD);

        var output = new StringBuilder(source.Length);
        var previousUnderscore = false;

        foreach (var character in source)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9')
            {
                output.Append(character);
                previousUnderscore = false;
                continue;
            }

            if (!previousUnderscore)
            {
                output.Append('_');
                previousUnderscore = true;
            }
        }

        return output
            .ToString()
            .Trim('_')
            .Normalize(NormalizationForm.FormC);
    }

    private string FirstModelError() =>
        ModelState.Values
            .SelectMany(item => item.Errors)
            .Select(item => item.ErrorMessage)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
        ?? "Dữ liệu chưa hợp lệ.";
}
