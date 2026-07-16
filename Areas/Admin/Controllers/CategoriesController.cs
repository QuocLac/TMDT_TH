using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Categories;
using WebApplication2.Models;
using WebApplication2.Services.Catalog;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class CategoriesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CategoriesController> _logger;
    private readonly TimeProvider _timeProvider;

    public CategoriesController(
        ApplicationDbContext context,
        ILogger<CategoriesController> logger,
        TimeProvider timeProvider)
    {
        _context = context;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categoryData = await _context.Categories
            .AsNoTracking()
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .Select(category => new
            {
                category.Id,
                category.Name,
                category.Slug,
                category.IconKey,
                category.DisplayOrder,
                category.IsVisible,
                ParentName = category.Parent != null ? category.Parent.Name : null,
                ProductCount = category.Products.Count,
                ChildCount = category.Children.Count
            })
            .ToListAsync(cancellationToken);

        var categories = categoryData
            .Select(category => new CategoryListItemViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                IconKey = CategoryIconCatalog.NormalizeKey(category.IconKey),
                IconCssClass = CategoryIconCatalog.ResolveCssClass(category.IconKey),
                DisplayOrder = category.DisplayOrder,
                IsVisible = category.IsVisible,
                ParentName = category.ParentName,
                ProductCount = category.ProductCount,
                ChildCount = category.ChildCount
            })
            .ToArray();

        var model = new CategoryIndexPageViewModel
        {
            Categories = categories,
            ParentOptions = categories
                .Select(category => new CategoryOptionViewModel
                {
                    Id = category.Id,
                    Name = category.Name
                })
                .ToArray()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> GetCategory(
        int id,
        CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new CategoryInputModel
            {
                Id = item.Id,
                Name = item.Name,
                ParentId = item.ParentId,
                Slug = item.Slug,
                IconKey = item.IconKey,
                DisplayOrder = item.DisplayOrder,
                IsVisible = item.IsVisible,
                MetaTitle = item.MetaTitle,
                MetaDescription = item.MetaDescription
            })
            .SingleOrDefaultAsync(cancellationToken);

        return category is null
            ? NotFound(new
            {
                success = false,
                message = "Không tìm thấy danh mục."
            })
            : Json(new
            {
                success = true,
                data = category with
                {
                    IconKey = CategoryIconCatalog.NormalizeKey(category.IconKey)
                }
            });
    }

    [HttpGet]
    public IActionResult IconCatalog(
        string? query = null,
        string? group = null,
        int limit = 60)
    {
        var icons = CategoryIconCatalog.Search(query, group, limit);

        return Json(new
        {
            success = true,
            data = new
            {
                groups = CategoryIconCatalog.Groups.Select(item => new
                {
                    key = item.Key,
                    label = item.Label
                }),
                items = icons.Select(item => new
                {
                    key = item.Key,
                    label = item.Label,
                    cssClass = item.CssClass,
                    groupKey = item.GroupKey,
                    groupLabel = item.GroupLabel
                }),
                total = icons.Count
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveCategory(
        [FromBody] CategoryInputModel request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = ModelState.Values
                    .SelectMany(value => value.Errors)
                    .Select(error => error.ErrorMessage)
                    .FirstOrDefault()
                    ?? "Dữ liệu danh mục không hợp lệ."
            });
        }

        var normalizedName = request.Name.Trim();
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();

        if (!CategoryIconCatalog.TryGet(request.IconKey, out var icon))
        {
            return BadRequest(new
            {
                success = false,
                message = "Biểu tượng đã chọn không thuộc thư viện danh mục được hỗ trợ."
            });
        }

        if (request.ParentId == request.Id && request.Id != 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Danh mục không thể là cha của chính nó."
            });
        }

        var parentValidationMessage = await ValidateParentAsync(
            request.Id,
            request.ParentId,
            cancellationToken);

        if (parentValidationMessage is not null)
        {
            return BadRequest(new
            {
                success = false,
                message = parentValidationMessage
            });
        }

        var slugExists = await _context.Categories
            .AsNoTracking()
            .AnyAsync(
                category =>
                    category.Slug == normalizedSlug
                    && category.Id != request.Id,
                cancellationToken);

        if (slugExists)
        {
            return Conflict(new
            {
                success = false,
                message = "Đường dẫn SEO đã được sử dụng bởi danh mục khác."
            });
        }

        try
        {
            if (request.Id == 0)
            {
                _context.Categories.Add(new Category
                {
                    Name = normalizedName,
                    ParentId = request.ParentId,
                    Slug = normalizedSlug,
                    IconKey = icon.Key,
                    DisplayOrder = request.DisplayOrder,
                    IsVisible = request.IsVisible,
                    MetaTitle = request.MetaTitle?.Trim() ?? string.Empty,
                    MetaDescription = request.MetaDescription?.Trim() ?? string.Empty
                });
            }
            else
            {
                var category = await _context.Categories
                    .SingleOrDefaultAsync(
                        item => item.Id == request.Id,
                        cancellationToken);

                if (category is null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Danh mục không tồn tại."
                    });
                }

                category.Name = normalizedName;
                category.ParentId = request.ParentId;
                category.Slug = normalizedSlug;
                category.IconKey = icon.Key;
                category.DisplayOrder = request.DisplayOrder;
                category.IsVisible = request.IsVisible;
                category.MetaTitle = request.MetaTitle?.Trim() ?? string.Empty;
                category.MetaDescription = request.MetaDescription?.Trim() ?? string.Empty;
                category.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Json(new
            {
                success = true,
                message = request.Id == 0
                    ? "Đã tạo danh mục mới."
                    : "Đã cập nhật danh mục."
            });
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not save category {CategoryId}.",
                request.Id);

            return Conflict(new
            {
                success = false,
                message = "Không thể lưu danh mục vì dữ liệu vừa thay đổi hoặc bị trùng."
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .Include(item => item.Children)
            .Include(item => item.Products)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

        if (category is null)
        {
            return NotFound(new
            {
                success = false,
                message = "Danh mục không tồn tại."
            });
        }

        if (category.Children.Count != 0 || category.Products.Count != 0)
        {
            return Conflict(new
            {
                success = false,
                message = "Không thể xóa danh mục đang có danh mục con hoặc sản phẩm."
            });
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            success = true,
            message = "Đã xóa danh mục."
        });
    }

    private async Task<string?> ValidateParentAsync(
        int categoryId,
        int? parentId,
        CancellationToken cancellationToken)
    {
        if (!parentId.HasValue)
        {
            return null;
        }

        var hierarchy = await _context.Categories
            .AsNoTracking()
            .Select(category => new
            {
                category.Id,
                category.ParentId
            })
            .ToDictionaryAsync(
                category => category.Id,
                category => category.ParentId,
                cancellationToken);

        if (!hierarchy.ContainsKey(parentId.Value))
        {
            return "Danh mục cha không tồn tại.";
        }

        var visited = new HashSet<int>();
        var currentId = parentId;

        while (currentId.HasValue)
        {
            if (!visited.Add(currentId.Value))
            {
                return "Cây danh mục hiện có vòng lặp và cần được kiểm tra dữ liệu.";
            }

            if (categoryId != 0 && currentId.Value == categoryId)
            {
                return "Không thể chọn danh mục con làm danh mục cha.";
            }

            currentId = hierarchy.GetValueOrDefault(currentId.Value);
        }

        return null;
    }
}
