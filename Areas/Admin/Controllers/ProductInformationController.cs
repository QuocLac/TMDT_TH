using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.ProductInformation;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/ProductInformation")]
public sealed class ProductInformationController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProductInformationController> _logger;

    public ProductInformationController(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<ProductInformationController> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpGet("definitions")]
    public async Task<IActionResult> Definitions(
        CancellationToken cancellationToken)
    {
        var items = await _context.Set<ProductAttributeDefinition>()
            .AsNoTracking()
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.Name)
            .Select(item => new ProductAttributeDefinitionItemViewModel
            {
                Id = item.Id,
                Code = item.Code,
                Name = item.Name,
                HelpText = item.HelpText,
                DataType = item.DataType,
                Unit = item.Unit,
                IsFilterable = item.IsFilterable,
                IsComparable = item.IsComparable,
                IsCustomerVisible = item.IsCustomerVisible,
                IsActive = item.IsActive,
                CategoryCount = item.CategoryAssignments.Count,
                ProductValueCount = item.ProductValues.Count,
                Options = item.Options
                    .Where(option => option.IsActive)
                    .OrderBy(option => option.DisplayOrder)
                    .ThenBy(option => option.Label)
                    .Select(option => option.Label)
                    .ToArray()
            })
            .ToArrayAsync(cancellationToken);

        return View(new ProductAttributeDefinitionIndexViewModel
        {
            Items = items
        });
    }

    [HttpPost("definitions/save")]
    public async Task<IActionResult> SaveDefinition(
        [Bind(Prefix = "Input")] ProductAttributeDefinitionInput input,
        CancellationToken cancellationToken)
    {
        NormalizeDefinition(input);

        ModelState.Clear();
        TryValidateModel(input);

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Definitions));
        }

        var definitions = _context.Set<ProductAttributeDefinition>();
        var duplicate = await definitions.AnyAsync(
            item => item.Id != input.Id && item.Code == input.Code,
            cancellationToken);

        if (duplicate)
        {
            TempData["ErrorMessage"] = "Mã trường dữ liệu đã được sử dụng.";
            return RedirectToAction(nameof(Definitions));
        }

        var choiceLabels = ParseChoiceLabels(input.ChoiceLabels);
        if (input.DataType == ProductAttributeDataType.SingleChoice
            && choiceLabels.Count == 0)
        {
            TempData["ErrorMessage"] =
                "Trường dạng lựa chọn phải có ít nhất một giá trị.";
            return RedirectToAction(nameof(Definitions));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            ProductAttributeDefinition definition;

            if (input.Id == 0)
            {
                definition = new ProductAttributeDefinition();
                definitions.Add(definition);
            }
            else
            {
                definition = await definitions
                    .Include(item => item.Options)
                    .SingleOrDefaultAsync(item => item.Id == input.Id, cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Không tìm thấy trường dữ liệu cần cập nhật.");

                if (definition.DataType != input.DataType)
                {
                    var hasValues = await _context.Set<ProductAttributeValue>()
                        .AnyAsync(
                            item => item.AttributeDefinitionId == definition.Id,
                            cancellationToken);

                    if (hasValues)
                    {
                        throw new InvalidOperationException(
                            "Không thể đổi kiểu dữ liệu khi trường này đã có dữ liệu sản phẩm.");
                    }
                }
            }

            definition.Code = input.Code;
            definition.Name = input.Name;
            definition.HelpText = CleanNullable(input.HelpText);
            definition.DataType = input.DataType;
            definition.Unit = input.DataType == ProductAttributeDataType.Number
                ? CleanNullable(input.Unit)
                : null;
            definition.IsFilterable = input.IsFilterable;
            definition.IsComparable = input.IsComparable;
            definition.IsCustomerVisible = input.IsCustomerVisible;
            definition.IsActive = input.IsActive;
            definition.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _context.SaveChangesAsync(cancellationToken);

            await SynchronizeOptionsAsync(
                definition,
                choiceLabels,
                input.DataType == ProductAttributeDataType.SingleChoice,
                cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            TempData["SuccessMessage"] = input.Id == 0
                ? "Đã tạo trường thông tin sản phẩm."
                : "Đã cập nhật trường thông tin sản phẩm.";
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
                "Database rejected product information definition {DefinitionId}.",
                input.Id);
            TempData["ErrorMessage"] =
                "Không thể lưu trường thông tin. Hãy kiểm tra mã và các giá trị lựa chọn.";
        }

        return RedirectToAction(nameof(Definitions));
    }

    [HttpPost("definitions/{id:int}/toggle")]
    public async Task<IActionResult> ToggleDefinition(
        int id,
        CancellationToken cancellationToken)
    {
        var definition = await _context.Set<ProductAttributeDefinition>()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (definition is null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy trường thông tin.";
            return RedirectToAction(nameof(Definitions));
        }

        definition.IsActive = !definition.IsActive;
        definition.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = definition.IsActive
            ? "Đã cho phép sử dụng trường thông tin."
            : "Đã ngừng sử dụng trường thông tin.";

        return RedirectToAction(nameof(Definitions));
    }

    [HttpGet("category-template")]
    public async Task<IActionResult> CategoryTemplate(
        int? categoryId,
        CancellationToken cancellationToken)
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .Where(item => item.IsVisible)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Id,
                item.Name
            })
            .ToArrayAsync(cancellationToken);

        if (categories.Length == 0)
        {
            return View(new CategoryProductInformationViewModel());
        }

        var selectedCategoryId = categoryId.HasValue
            && categories.Any(item => item.Id == categoryId.Value)
                ? categoryId.Value
                : categories[0].Id;

        var assignments = await _context.Set<CategoryProductAttribute>()
            .AsNoTracking()
            .Where(item => item.CategoryId == selectedCategoryId)
            .ToDictionaryAsync(
                item => item.AttributeDefinitionId,
                cancellationToken);

        var definitions = await _context.Set<ProductAttributeDefinition>()
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .ToArrayAsync(cancellationToken);

        var selectedCategory = categories.Single(item => item.Id == selectedCategoryId);

        return View(new CategoryProductInformationViewModel
        {
            CategoryId = selectedCategoryId,
            CategoryName = selectedCategory.Name,
            CategoryOptions = categories
                .Select(item => new SelectListItem(
                    item.Name,
                    item.Id.ToString(CultureInfo.InvariantCulture),
                    item.Id == selectedCategoryId))
                .ToArray(),
            Attributes = definitions
                .Select((definition, index) =>
                {
                    assignments.TryGetValue(definition.Id, out var assignment);

                    return new CategoryAttributeAssignmentInput
                    {
                        AttributeDefinitionId = definition.Id,
                        Code = definition.Code,
                        Name = definition.Name,
                        DataTypeText = ProductInformationDisplay.DataTypeText(
                            definition.DataType),
                        Selected = assignment is not null,
                        GroupName = assignment?.GroupName,
                        IsRequired = assignment?.IsRequired ?? false,
                        DisplayOrder = assignment?.DisplayOrder ?? ((index + 1) * 10)
                    };
                })
                .ToList()
        });
    }

    [HttpPost("category-template")]
    public async Task<IActionResult> SaveCategoryTemplate(
        CategoryProductInformationViewModel model,
        CancellationToken cancellationToken)
    {
        var categoryExists = await _context.Categories
            .AnyAsync(item => item.Id == model.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            TempData["ErrorMessage"] = "Không tìm thấy danh mục.";
            return RedirectToAction(nameof(CategoryTemplate));
        }

        var selected = model.Attributes
            .Where(item => item.Selected)
            .GroupBy(item => item.AttributeDefinitionId)
            .Select(group => group.First())
            .ToArray();

        var selectedIds = selected
            .Select(item => item.AttributeDefinitionId)
            .ToArray();

        var validDefinitionIds = await _context.Set<ProductAttributeDefinition>()
            .Where(item => item.IsActive && selectedIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);

        if (validDefinitionIds.Length != selected.Length)
        {
            TempData["ErrorMessage"] =
                "Một hoặc nhiều trường thông tin không còn được phép sử dụng.";
            return RedirectToAction(
                nameof(CategoryTemplate),
                new { categoryId = model.CategoryId });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var current = await _context.Set<CategoryProductAttribute>()
            .Where(item => item.CategoryId == model.CategoryId)
            .ToArrayAsync(cancellationToken);

        _context.Set<CategoryProductAttribute>().RemoveRange(current);

        foreach (var row in selected)
        {
            _context.Set<CategoryProductAttribute>().Add(
                new CategoryProductAttribute
                {
                    CategoryId = model.CategoryId,
                    AttributeDefinitionId = row.AttributeDefinitionId,
                    GroupName = CleanNullable(row.GroupName),
                    IsRequired = row.IsRequired,
                    DisplayOrder = row.DisplayOrder
                });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["SuccessMessage"] =
            "Đã cập nhật bộ thông tin dành cho danh mục.";

        return RedirectToAction(
            nameof(CategoryTemplate),
            new { categoryId = model.CategoryId });
    }

    [HttpGet("product/{productId:int}")]
    public async Task<IActionResult> EditProduct(
        int productId,
        CancellationToken cancellationToken)
    {
        var model = await BuildProductEditModelAsync(
            productId,
            cancellationToken);

        return model is null ? NotFound() : View(model);
    }

    [HttpPost("product/{productId:int}")]
    public async Task<IActionResult> EditProduct(
        int productId,
        ProductInformationEditViewModel model,
        CancellationToken cancellationToken)
    {
        if (productId != model.ProductId)
        {
            return NotFound();
        }

        var product = await _context.Products
            .Include(item => item.AttributeValues)
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var assignments = await _context.Set<CategoryProductAttribute>()
            .AsNoTracking()
            .Where(item =>
                item.CategoryId == product.CategoryId
                && item.AttributeDefinition.IsActive)
            .Include(item => item.AttributeDefinition)
                .ThenInclude(item => item.Options)
            .OrderBy(item => item.DisplayOrder)
            .ToArrayAsync(cancellationToken);

        var allowedIds = assignments
            .Select(item => item.AttributeDefinitionId)
            .ToHashSet();

        var submittedByDefinition = model.Fields
            .GroupBy(item => item.AttributeDefinitionId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var assignment in assignments)
        {
            submittedByDefinition.TryGetValue(
                assignment.AttributeDefinitionId,
                out var submitted);

            ValidateSubmittedValue(assignment, submitted);
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildProductEditModelAsync(
                productId,
                cancellationToken,
                submittedByDefinition);

            return View(invalidModel!);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var staleValues = product.AttributeValues
            .Where(item => !allowedIds.Contains(item.AttributeDefinitionId))
            .ToArray();

        _context.Set<ProductAttributeValue>().RemoveRange(staleValues);

        foreach (var assignment in assignments)
        {
            var definition = assignment.AttributeDefinition;
            submittedByDefinition.TryGetValue(definition.Id, out var submitted);

            var existing = product.AttributeValues
                .SingleOrDefault(item => item.AttributeDefinitionId == definition.Id);

            if (submitted is null || IsEmpty(definition.DataType, submitted))
            {
                if (existing is not null)
                {
                    _context.Set<ProductAttributeValue>().Remove(existing);
                }

                continue;
            }

            existing ??= new ProductAttributeValue
            {
                ProductId = product.Id,
                AttributeDefinitionId = definition.Id
            };

            if (existing.Id == 0)
            {
                _context.Set<ProductAttributeValue>().Add(existing);
            }

            ClearValue(existing);
            ApplyValue(existing, definition.DataType, submitted);
            existing.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }

        product.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["SuccessMessage"] =
            "Đã cập nhật thông tin theo ngành hàng.";

        return RedirectToAction(nameof(EditProduct), new { productId });
    }

    private async Task<ProductInformationEditViewModel?> BuildProductEditModelAsync(
        int productId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, ProductInformationFieldInput>? submitted = null)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.CategoryId,
                CategoryName = item.Category.Name
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var assignments = await _context.Set<CategoryProductAttribute>()
            .AsNoTracking()
            .Where(item =>
                item.CategoryId == product.CategoryId
                && item.AttributeDefinition.IsActive)
            .Include(item => item.AttributeDefinition)
                .ThenInclude(item => item.Options)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.AttributeDefinition.Name)
            .ToArrayAsync(cancellationToken);

        var values = await _context.Set<ProductAttributeValue>()
            .AsNoTracking()
            .Where(item => item.ProductId == productId)
            .ToDictionaryAsync(
                item => item.AttributeDefinitionId,
                cancellationToken);

        return new ProductInformationEditViewModel
        {
            ProductId = product.Id,
            ProductName = product.Name,
            CategoryId = product.CategoryId,
            CategoryName = product.CategoryName,
            Fields = assignments.Select(assignment =>
            {
                ProductInformationFieldInput? submittedField = null;
                var hasSubmittedValue = false;

                if (submitted is not null
                    && submitted.TryGetValue(
                        assignment.AttributeDefinitionId,
                        out var submittedValue))
                {
                    hasSubmittedValue = true;
                    submittedField = submittedValue;
                }

                values.TryGetValue(
                    assignment.AttributeDefinitionId,
                    out var value);

                var selectedOptionId = hasSubmittedValue
                    ? submittedField!.OptionId
                    : value?.OptionId;

                return new ProductInformationFieldInput
                {
                    AttributeDefinitionId = assignment.AttributeDefinitionId,
                    Code = assignment.AttributeDefinition.Code,
                    Name = assignment.AttributeDefinition.Name,
                    HelpText = assignment.AttributeDefinition.HelpText,
                    DataType = assignment.AttributeDefinition.DataType,
                    Unit = assignment.AttributeDefinition.Unit,
                    GroupName = string.IsNullOrWhiteSpace(assignment.GroupName)
                        ? "Thông tin chung"
                        : assignment.GroupName,
                    IsRequired = assignment.IsRequired,
                    DisplayOrder = assignment.DisplayOrder,
                    TextValue = hasSubmittedValue
                        ? submittedField!.TextValue
                        : value?.TextValue,
                    NumberValue = hasSubmittedValue
                        ? submittedField!.NumberValue
                        : value?.NumberValue,
                    BooleanValue = hasSubmittedValue
                        ? submittedField!.BooleanValue
                        : value?.BooleanValue,
                    DateValue = hasSubmittedValue
                        ? submittedField!.DateValue
                        : value?.DateValue,
                    OptionId = selectedOptionId,
                    OptionItems = assignment.AttributeDefinition.Options
                        .Where(option =>
                            option.IsActive || option.Id == selectedOptionId)
                        .OrderBy(option => option.DisplayOrder)
                        .ThenBy(option => option.Label)
                        .Select(option => new SelectListItem(
                            option.Label,
                            option.Id.ToString(CultureInfo.InvariantCulture),
                            selectedOptionId == option.Id))
                        .ToArray()
                };
            }).ToList()
        };
    }

    private void ValidateSubmittedValue(
        CategoryProductAttribute assignment,
        ProductInformationFieldInput? submitted)
    {
        var definition = assignment.AttributeDefinition;

        if (assignment.IsRequired
            && (submitted is null || IsEmpty(definition.DataType, submitted)))
        {
            ModelState.AddModelError(
                string.Empty,
                $"Vui lòng nhập {definition.Name}.");
            return;
        }

        if (submitted is null || IsEmpty(definition.DataType, submitted))
        {
            return;
        }

        if (definition.DataType == ProductAttributeDataType.SingleChoice)
        {
            var validOption = definition.Options.Any(option =>
                option.Id == submitted.OptionId && option.IsActive);

            if (!validOption)
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"{definition.Name} không có giá trị hợp lệ.");
            }
        }

        if (definition.DataType is
            ProductAttributeDataType.ShortText
            or ProductAttributeDataType.LongText
            && submitted.TextValue?.Trim().Length > 2_000)
        {
            ModelState.AddModelError(
                string.Empty,
                $"{definition.Name} vượt quá 2.000 ký tự.");
        }
    }

    private static bool IsEmpty(
        ProductAttributeDataType dataType,
        ProductInformationFieldInput input) =>
        dataType switch
        {
            ProductAttributeDataType.ShortText
                or ProductAttributeDataType.LongText =>
                string.IsNullOrWhiteSpace(input.TextValue),
            ProductAttributeDataType.Number => !input.NumberValue.HasValue,
            ProductAttributeDataType.Boolean => !input.BooleanValue.HasValue,
            ProductAttributeDataType.Date => !input.DateValue.HasValue,
            ProductAttributeDataType.SingleChoice => !input.OptionId.HasValue,
            _ => true
        };

    private static void ApplyValue(
        ProductAttributeValue target,
        ProductAttributeDataType dataType,
        ProductInformationFieldInput input)
    {
        switch (dataType)
        {
            case ProductAttributeDataType.ShortText:
            case ProductAttributeDataType.LongText:
                target.TextValue = input.TextValue?.Trim();
                break;
            case ProductAttributeDataType.Number:
                target.NumberValue = input.NumberValue;
                break;
            case ProductAttributeDataType.Boolean:
                target.BooleanValue = input.BooleanValue;
                break;
            case ProductAttributeDataType.Date:
                target.DateValue = input.DateValue;
                break;
            case ProductAttributeDataType.SingleChoice:
                target.OptionId = input.OptionId;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(dataType),
                    dataType,
                    "Kiểu dữ liệu không được hỗ trợ.");
        }
    }

    private static void ClearValue(ProductAttributeValue value)
    {
        value.TextValue = null;
        value.NumberValue = null;
        value.BooleanValue = null;
        value.DateValue = null;
        value.OptionId = null;
    }

    private async Task SynchronizeOptionsAsync(
        ProductAttributeDefinition definition,
        IReadOnlyList<string> labels,
        bool keepOptions,
        CancellationToken cancellationToken)
    {
        var options = definition.Options.Count > 0
            ? definition.Options
            : await _context.Set<ProductAttributeOption>()
                .Where(item => item.AttributeDefinitionId == definition.Id)
                .ToListAsync(cancellationToken);

        foreach (var option in options)
        {
            option.IsActive = false;
            option.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }

        if (!keepOptions)
        {
            return;
        }

        var usedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < labels.Count; index++)
        {
            var label = labels[index];
            var value = NormalizeCode(label);

            if (string.IsNullOrWhiteSpace(value) || !usedValues.Add(value))
            {
                throw new InvalidOperationException(
                    $"Giá trị lựa chọn “{label}” bị trùng sau khi chuẩn hóa.");
            }

            var option = options.SingleOrDefault(item =>
                string.Equals(
                    item.Value,
                    value,
                    StringComparison.OrdinalIgnoreCase));

            if (option is null)
            {
                option = new ProductAttributeOption
                {
                    AttributeDefinitionId = definition.Id,
                    Value = value
                };

                _context.Set<ProductAttributeOption>().Add(option);
                options.Add(option);
            }

            option.Label = label;
            option.DisplayOrder = (index + 1) * 10;
            option.IsActive = true;
            option.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }
    }

    private static IReadOnlyList<string> ParseChoiceLabels(string? value) =>
        (value ?? string.Empty)
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void NormalizeDefinition(
        ProductAttributeDefinitionInput input)
    {
        input.Code = input.Code.Trim().ToLowerInvariant();
        input.Name = input.Name.Trim();
        input.HelpText = CleanNullable(input.HelpText);
        input.Unit = CleanNullable(input.Unit);
    }

    private static string NormalizeCode(string value)
    {
        var normalized = value
            .Trim()
            .ToLowerInvariant()
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormD);

        var buffer = new StringBuilder(normalized.Length);
        var previousUnderscore = false;

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9')
            {
                buffer.Append(character);
                previousUnderscore = false;
                continue;
            }

            if (!previousUnderscore)
            {
                buffer.Append('_');
                previousUnderscore = true;
            }
        }

        return buffer.ToString().Trim('_');
    }

    private string FirstModelError() =>
        ModelState.Values
            .SelectMany(item => item.Errors)
            .Select(item => item.ErrorMessage)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
        ?? "Dữ liệu chưa hợp lệ.";

    private static string? CleanNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
