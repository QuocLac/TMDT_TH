using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.CatalogReadiness;
using WebApplication2.Models;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/CatalogReadiness")]
public sealed class CatalogReadinessController : Controller
{
    private const int PageSize = 24;

    private readonly ApplicationDbContext _context;

    public CatalogReadinessController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? query,
        string? status,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var normalizedStatus = NormalizeStatus(status);

        var baseQuery = _context.Products
            .AsNoTracking()
            .Where(product =>
                string.IsNullOrWhiteSpace(normalizedQuery)
                || product.Name.Contains(normalizedQuery)
                || product.Slug.Contains(normalizedQuery)
                || product.Variants.Any(item =>
                    item.SKU.Contains(normalizedQuery)));

        var summary = new CatalogReadinessSummaryViewModel
        {
            TotalProductCount = await baseQuery.CountAsync(cancellationToken),
            ReadyCount = await baseQuery.CountAsync(
                ReadyExpression(),
                cancellationToken),
            AttentionCount = await baseQuery.CountAsync(
                AttentionExpression(),
                cancellationToken),
            UnavailableCount = await baseQuery.CountAsync(
                UnavailableExpression(),
                cancellationToken),
            HiddenCount = await baseQuery.CountAsync(
                product => !product.IsActive,
                cancellationToken)
        };

        var filteredQuery = normalizedStatus switch
        {
            "ready" => baseQuery.Where(ReadyExpression()),
            "attention" => baseQuery.Where(AttentionExpression()),
            "unavailable" => baseQuery.Where(UnavailableExpression()),
            "hidden" => baseQuery.Where(product => !product.IsActive),
            _ => baseQuery
        };

        var totalItems = await filteredQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalItems / (double)PageSize));

        page = Math.Min(page, totalPages);

        var rows = await filteredQuery
            .OrderByDescending(product => product.UpdatedAt ?? product.CreatedAt)
            .ThenBy(product => product.Name)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Slug,
                product.IsActive,
                CategoryName = product.Category.Name,
                BrandName = product.Brand != null
                    ? product.Brand.Name
                    : null,
                ImageUrl = product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                HasMainImage = product.Images.Any(image => image.IsMain),
                HasSearchContent =
                    product.MetaTitle != null
                    && product.MetaTitle != ""
                    && product.MetaDescription != null
                    && product.MetaDescription != "",
                ItemCount = product.Variants.Count,
                ActiveItemCount = product.Variants.Count(item => item.IsActive),
                InStockItemCount = product.Variants.Count(item =>
                    item.IsActive
                    && item.StockQuantity > 0
                    && item.CurrentPrice > 0),
                RequiredOptionGroupCount = product.OptionGroups.Count(group =>
                    group.IsActive
                    && group.IsRequired),
                RequiredOptionGroupWithoutValuesCount =
                    product.OptionGroups.Count(group =>
                        group.IsActive
                        && group.IsRequired
                        && !group.Values.Any(value => value.IsActive)),
                IncompleteActiveItemCount = product.Variants.Count(item =>
                    item.IsActive
                    && product.OptionGroups.Any(group =>
                        group.IsActive
                        && group.IsRequired
                        && !item.OptionSelections.Any(selection =>
                            selection.OptionGroupId == group.Id
                            && selection.OptionValue.IsActive))),
                RequiredInformationCount =
                    product.Category.ProductAttributeAssignments.Count(assignment =>
                        assignment.IsRequired
                        && assignment.AttributeDefinition.IsActive),
                MissingRequiredInformationCount =
                    product.Category.ProductAttributeAssignments.Count(assignment =>
                        assignment.IsRequired
                        && assignment.AttributeDefinition.IsActive
                        && !product.AttributeValues.Any(value =>
                            value.AttributeDefinitionId
                            == assignment.AttributeDefinitionId))
            })
            .ToArrayAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            var issues = BuildIssues(
                row.IsActive,
                row.HasMainImage,
                row.HasSearchContent,
                row.ActiveItemCount,
                row.InStockItemCount,
                row.RequiredOptionGroupWithoutValuesCount,
                row.IncompleteActiveItemCount,
                row.MissingRequiredInformationCount);

            var (state, stateLabel) = ResolveState(
                row.IsActive,
                row.HasMainImage,
                row.ActiveItemCount,
                row.InStockItemCount,
                row.RequiredOptionGroupWithoutValuesCount,
                row.IncompleteActiveItemCount,
                row.MissingRequiredInformationCount,
                row.HasSearchContent);

            return new CatalogReadinessItemViewModel
            {
                ProductId = row.Id,
                ProductName = row.Name,
                ProductSlug = row.Slug,
                CategoryName = row.CategoryName,
                BrandName = row.BrandName,
                ImageUrl = string.IsNullOrWhiteSpace(row.ImageUrl)
                    ? "/images/no-image.png"
                    : row.ImageUrl,
                ProductIsActive = row.IsActive,
                HasMainImage = row.HasMainImage,
                HasSearchContent = row.HasSearchContent,
                ItemCount = row.ItemCount,
                ActiveItemCount = row.ActiveItemCount,
                InStockItemCount = row.InStockItemCount,
                RequiredOptionGroupCount = row.RequiredOptionGroupCount,
                RequiredOptionGroupWithoutValuesCount =
                    row.RequiredOptionGroupWithoutValuesCount,
                IncompleteActiveItemCount = row.IncompleteActiveItemCount,
                RequiredInformationCount = row.RequiredInformationCount,
                MissingRequiredInformationCount =
                    row.MissingRequiredInformationCount,
                State = state,
                StateLabel = stateLabel,
                Issues = issues
            };
        }).ToArray();

        return View(new CatalogReadinessPageViewModel
        {
            Query = normalizedQuery,
            Status = normalizedStatus,
            Page = page,
            TotalPages = totalPages,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        });
    }

    private static Expression<Func<Product, bool>> ReadyExpression() =>
        product =>
            product.IsActive
            && product.Images.Any(image => image.IsMain)
            && product.Variants.Any(item =>
                item.IsActive
                && item.StockQuantity > 0
                && item.CurrentPrice > 0)
            && !product.OptionGroups.Any(group =>
                group.IsActive
                && group.IsRequired
                && !group.Values.Any(value => value.IsActive))
            && !product.Variants.Any(item =>
                item.IsActive
                && product.OptionGroups.Any(group =>
                    group.IsActive
                    && group.IsRequired
                    && !item.OptionSelections.Any(selection =>
                        selection.OptionGroupId == group.Id
                        && selection.OptionValue.IsActive)))
            && !product.Category.ProductAttributeAssignments.Any(assignment =>
                assignment.IsRequired
                && assignment.AttributeDefinition.IsActive
                && !product.AttributeValues.Any(value =>
                    value.AttributeDefinitionId
                    == assignment.AttributeDefinitionId));

    private static Expression<Func<Product, bool>> UnavailableExpression() =>
        product =>
            product.IsActive
            && !product.Variants.Any(item =>
                item.IsActive
                && item.StockQuantity > 0
                && item.CurrentPrice > 0);

    private static Expression<Func<Product, bool>> AttentionExpression() =>
        product =>
            product.IsActive
            && product.Variants.Any(item =>
                item.IsActive
                && item.StockQuantity > 0
                && item.CurrentPrice > 0)
            && (
                !product.Images.Any(image => image.IsMain)
                || product.OptionGroups.Any(group =>
                    group.IsActive
                    && group.IsRequired
                    && !group.Values.Any(value => value.IsActive))
                || product.Variants.Any(item =>
                    item.IsActive
                    && product.OptionGroups.Any(group =>
                        group.IsActive
                        && group.IsRequired
                        && !item.OptionSelections.Any(selection =>
                            selection.OptionGroupId == group.Id
                            && selection.OptionValue.IsActive)))
                || product.Category.ProductAttributeAssignments.Any(assignment =>
                    assignment.IsRequired
                    && assignment.AttributeDefinition.IsActive
                    && !product.AttributeValues.Any(value =>
                        value.AttributeDefinitionId
                        == assignment.AttributeDefinitionId))
                || product.MetaTitle == null
                || product.MetaTitle == ""
                || product.MetaDescription == null
                || product.MetaDescription == ""
            );

    private static IReadOnlyList<string> BuildIssues(
        bool isActive,
        bool hasMainImage,
        bool hasSearchContent,
        int activeItemCount,
        int inStockItemCount,
        int groupsWithoutValues,
        int incompleteItems,
        int missingInformation)
    {
        var issues = new List<string>();

        if (!isActive)
        {
            issues.Add("Sản phẩm đang ẩn");
        }

        if (!hasMainImage)
        {
            issues.Add("Chưa có ảnh đại diện");
        }

        if (activeItemCount == 0)
        {
            issues.Add("Chưa có mã hàng đang bán");
        }
        else if (inStockItemCount == 0)
        {
            issues.Add("Không có mã hàng còn số lượng bán");
        }

        if (groupsWithoutValues > 0)
        {
            issues.Add($"{groupsWithoutValues} nhóm bắt buộc chưa có giá trị");
        }

        if (incompleteItems > 0)
        {
            issues.Add($"{incompleteItems} mã hàng thiếu lựa chọn bắt buộc");
        }

        if (missingInformation > 0)
        {
            issues.Add($"{missingInformation} thông tin ngành hàng còn thiếu");
        }

        if (!hasSearchContent)
        {
            issues.Add("Nội dung tìm kiếm chưa hoàn chỉnh");
        }

        return issues;
    }

    private static (string State, string Label) ResolveState(
        bool isActive,
        bool hasMainImage,
        int activeItemCount,
        int inStockItemCount,
        int groupsWithoutValues,
        int incompleteItems,
        int missingInformation,
        bool hasSearchContent)
    {
        if (!isActive)
        {
            return ("hidden", "Đang ẩn");
        }

        if (activeItemCount == 0 || inStockItemCount == 0)
        {
            return ("unavailable", "Chưa thể bán");
        }

        if (!hasMainImage
            || groupsWithoutValues > 0
            || incompleteItems > 0
            || missingInformation > 0
            || !hasSearchContent)
        {
            return ("attention", "Cần hoàn thiện");
        }

        return ("ready", "Sẵn sàng bán");
    }

    private static string NormalizeStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "ready" => "ready",
            "attention" => "attention",
            "unavailable" => "unavailable",
            "hidden" => "hidden",
            _ => "all"
        };
}
